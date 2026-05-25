namespace Synth.Api.Configuration;

public sealed class OpenAIOptions
{
    public const string Section = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

public sealed class AzureOpenAIOptions
{
    public const string Section = "AzureOpenAI";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-4o-mini";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class AgentOptions
{
    public const string Section = "Agent";
    public int MaxSteps { get; set; } = 25;
    public int StepTimeoutSeconds { get; set; } = 20;
    public bool Headless { get; set; } = true;
}

public sealed class CorsOptions
{
    public const string Section = "Cors";
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
