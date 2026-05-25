using Synth.Api.Hubs;
using Synth.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace Synth.Api.Services;

/// <summary>
/// Abstraction over the transport that pushes <see cref="ProgressEvent"/>s to clients.
/// Keeps the agent orchestrator transport-agnostic and trivially testable.
/// </summary>
public interface IProgressPublisher
{
    Task PublishAsync(ProgressEvent evt, CancellationToken ct);
}

public sealed class SignalRProgressPublisher : IProgressPublisher
{
    private readonly IHubContext<TestRunHub> _hub;
    private readonly ILogger<SignalRProgressPublisher> _logger;

    public SignalRProgressPublisher(
        IHubContext<TestRunHub> hub,
        ILogger<SignalRProgressPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task PublishAsync(ProgressEvent evt, CancellationToken ct)
    {
        try
        {
            // Group naming convention: "run:{guid}". Clients join via TestRunHub.SubscribeAsync.
            await _hub.Clients
                .Group(TestRunHub.GroupName(evt.RunId))
                .SendAsync("progress", evt, ct);
        }
        catch (Exception ex)
        {
            // Swallowing here is intentional — SignalR delivery problems must not
            // crash an in-flight test run. Surface them in logs instead.
            _logger.LogWarning(ex, "Failed to publish progress event for run {RunId}", evt.RunId);
        }
    }
}
