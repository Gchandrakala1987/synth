// ---------------------------------------------------------------------------
// Synth — AI-driven browser testing engine — Azure infrastructure
//
// Deploys:
//   • Linux App Service Plan (Basic B1) + Web App (container)
//   • Azure OpenAI account + gpt-4o-mini deployment
//   • Log Analytics workspace + Application Insights (connected to the Web App)
//   • Azure Container Registry (Basic) for the API image
//
// Resources are named with a short hash for global uniqueness without polluting RG names.
// Apply with:
//   az group create -n rg-synth -l eastus
//   az deployment group create -g rg-synth -f infra/main.bicep -p namePrefix=synth
// ---------------------------------------------------------------------------

@description('Short prefix used in resource names. Lowercase letters and digits only, 2-10 chars.')
@minLength(2)
@maxLength(10)
param namePrefix string = 'synth'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('SKU for the App Service Plan.')
@allowed([ 'B1', 'B2', 'P1v3', 'P2v3' ])
param appServiceSku string = 'B1'

@description('Container image reference (registry/repo:tag) the Web App should pull.')
param containerImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('OpenAI model deployment name to create.')
param modelName string = 'gpt-4o-mini'

var suffix = uniqueString(resourceGroup().id, namePrefix)
var planName     = '${namePrefix}-plan-${suffix}'
var appName      = '${namePrefix}-api-${suffix}'
var acrName      = toLower('${namePrefix}acr${suffix}')
var openAiName   = '${namePrefix}-openai-${suffix}'
var logsName     = '${namePrefix}-logs-${suffix}'
var appInsights  = '${namePrefix}-ai-${suffix}'

// ---- Observability ----
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logsName
  location: location
  properties: {
    retentionInDays: 30
    sku: { name: 'PerGB2018' }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---- Container registry ----
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: true }
}

// ---- Azure OpenAI ----
resource openAi 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' = {
  name: openAiName
  location: location
  sku: { name: 'S0' }
  kind: 'OpenAI'
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
  parent: openAi
  name: modelName
  sku: { name: 'Standard', capacity: 30 }
  properties: {
    model: { format: 'OpenAI', name: modelName, version: '2024-07-18' }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
  }
}

// ---- Compute ----
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: appServiceSku, tier: startsWith(appServiceSku, 'P') ? 'PremiumV3' : 'Basic' }
  kind: 'linux'
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|${containerImage}'
      alwaysOn: appServiceSku != 'B1'
      ftpsState: 'Disabled'
      webSocketsEnabled: true // required for SignalR over WebSockets
      http20Enabled: true
      appSettings: [
        { name: 'WEBSITES_PORT',                          value: '8080' }
        { name: 'DOCKER_REGISTRY_SERVER_URL',             value: 'https://${acr.properties.loginServer}' }
        { name: 'DOCKER_REGISTRY_SERVER_USERNAME',        value: acr.listCredentials().username }
        { name: 'DOCKER_REGISTRY_SERVER_PASSWORD',        value: acr.listCredentials().passwords[0].value }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING',  value: insights.properties.ConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
        { name: 'AZUREOPENAI__ENDPOINT',                  value: openAi.properties.endpoint }
        { name: 'AZUREOPENAI__APIKEY',                    value: openAi.listKeys().key1 }
        { name: 'AZUREOPENAI__DEPLOYMENT',                value: modelDeployment.name }
        { name: 'AGENT__HEADLESS',                        value: 'true' }
      ]
    }
  }
}

output appUrl string = 'https://${app.properties.defaultHostName}'
output acrLoginServer string = acr.properties.loginServer
output openAiEndpoint string = openAi.properties.endpoint
