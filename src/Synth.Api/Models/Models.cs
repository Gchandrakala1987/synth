using System.Text.Json.Serialization;

namespace Synth.Api.Models;

/// <summary>
/// Request body for POST /api/test-runs. Kept intentionally small — the agent
/// derives concrete actions from the natural-language <see cref="Instruction"/>.
/// </summary>
public sealed record TestRunRequest(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("instruction")] string Instruction,
    [property: JsonPropertyName("viewport")] Viewport? Viewport = null);

public sealed record Viewport(int Width, int Height)
{
    public static Viewport Default { get; } = new(1280, 800);
}

/// <summary>Stable view of a test run returned by the REST API.</summary>
public sealed record TestRunDto(
    Guid Id,
    string Url,
    string Instruction,
    TestRunStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<TestStep> Steps,
    BugReport? Report);

public enum TestRunStatus
{
    Queued,
    Running,
    Passed,
    Failed,
    Errored,
    Cancelled
}

/// <summary>A single observable step in the agent's execution timeline.</summary>
public sealed record TestStep(
    int Index,
    string Tool,
    string Description,
    StepStatus Status,
    DateTimeOffset At,
    string? Detail = null,
    string? ScreenshotBase64 = null);

public enum StepStatus
{
    Started,
    Succeeded,
    Failed
}

/// <summary>Event payload pushed over SignalR to subscribers of a run.</summary>
public sealed record ProgressEvent(
    Guid RunId,
    ProgressEventKind Kind,
    DateTimeOffset At,
    TestStep? Step = null,
    TestRunStatus? Status = null,
    BugReport? Report = null,
    string? Message = null);

public enum ProgressEventKind
{
    RunStarted,
    StepStarted,
    StepCompleted,
    StepFailed,
    RunFinished,
    LogMessage
}

/// <summary>Final structured bug report emitted by the agent.</summary>
public sealed record BugReport(
    string Summary,
    BugSeverity OverallSeverity,
    IReadOnlyList<BugFinding> Findings,
    int StepsExecuted,
    TimeSpan Duration);

public sealed record BugFinding(
    string Title,
    string Description,
    BugSeverity Severity,
    string? Reproduction,
    int? RelatedStepIndex);

public enum BugSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical
}
