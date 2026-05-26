using System.ClientModel;
using Synth.Api.Configuration;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Synth.Api.Agent;

/// <summary>
/// Thin factory that returns a configured <see cref="ChatClient"/>. Encapsulates the
/// OpenAI vs Azure OpenAI selection so callers don't have to care which one is wired up.
/// </summary>
public interface IAgentChatClientFactory
{
    ChatClient Create();
}

public sealed class AgentChatClientFactory : IAgentChatClientFactory
{
    private readonly OpenAIOptions _openAi;
    private readonly AzureOpenAIOptions _azure;

    public AgentChatClientFactory(
        IOptions<OpenAIOptions> openAi,
        IOptions<AzureOpenAIOptions> azure)
    {
        _openAi = openAi.Value;
        _azure = azure.Value;
    }

    public ChatClient Create()
    {
        if (_azure.IsConfigured)
        {
            var azureClient = new AzureOpenAIClient(
                new Uri(_azure.Endpoint),
                new ApiKeyCredential(_azure.ApiKey));
            return azureClient.GetChatClient(_azure.Deployment);
        }

        if (string.IsNullOrWhiteSpace(_openAi.ApiKey))
        {
            throw new InvalidOperationException(
                "No model provider configured. Set OpenAI:ApiKey or AzureOpenAI:Endpoint+ApiKey.");
        }

        var client = new OpenAIClient(new ApiKeyCredential(_openAi.ApiKey));
        return client.GetChatClient(_openAi.Model);
    }
}
