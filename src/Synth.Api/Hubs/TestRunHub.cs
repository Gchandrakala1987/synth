using Microsoft.AspNetCore.SignalR;

namespace Synth.Api.Hubs;

/// <summary>
/// One hub per concept (test runs). Clients call <c>SubscribeAsync(runId)</c> to join
/// the per-run group; the server pushes <see cref="Models.ProgressEvent"/>s into it.
///
/// We keep the hub deliberately thin — no business logic — so the orchestrator
/// remains the single source of truth for state transitions.
/// </summary>
public sealed class TestRunHub : Hub
{
    public static string GroupName(Guid runId) => $"run:{runId:N}";

    public Task SubscribeAsync(Guid runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));

    public Task UnsubscribeAsync(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId));
}
