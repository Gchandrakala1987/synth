using System.Diagnostics;
using System.Text.Json;
using Synth.Api.Configuration;
using Synth.Api.Models;
using Synth.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using OpenAI.Chat;

namespace Synth.Api.Agent;

/// <summary>
/// The agent loop. Owns the lifecycle of a single test run:
///   1. Boot a Playwright context via <see cref="IBrowserToolbox"/>.
///   2. Drive an OpenAI tool-calling conversation until the model calls <c>finish_run</c>
///      or we hit the configured step ceiling.
///   3. Emit a <see cref="ProgressEvent"/> for every observable transition.
///
/// Pure orchestration — no Playwright or HTTP code lives here. That keeps the loop unit-testable
/// against fake toolboxes and chat clients (see <c>AgentOrchestratorTests</c>).
/// </summary>
public sealed class AgentOrchestrator
{
    private readonly IAgentChatClientFactory _chatFactory;
    private readonly IProgressPublisher _publisher;
    private readonly ITestRunStore _store;
    private readonly IPlaywright _playwright;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IAgentChatClientFactory chatFactory,
        IProgressPublisher publisher,
        ITestRunStore store,
        IPlaywright playwright,
        IOptions<AgentOptions> options,
        ILogger<AgentOrchestrator> logger)
    {
        _chatFactory = chatFactory;
        _publisher = publisher;
        _store = store;
        _playwright = playwright;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(TestRunRecord record, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        record.Status = TestRunStatus.Running;
        record.StartedAt = DateTimeOffset.UtcNow;
        await _store.UpdateAsync(record, ct);
        await _publisher.PublishAsync(new ProgressEvent(record.Id, ProgressEventKind.RunStarted,
            DateTimeOffset.UtcNow, Status: TestRunStatus.Running), ct);

        await using var toolbox = await PlaywrightBrowserToolbox.CreateAsync(
            _playwright, record.Viewport, _options.Headless, _logger, ct);

        try
        {
            var report = await DriveAgentLoopAsync(record, toolbox, ct);
            record.Report = report with { Duration = sw.Elapsed };
            record.Status = report.Findings.Any(f => f.Severity >= BugSeverity.High)
                ? TestRunStatus.Failed
                : TestRunStatus.Passed;
        }
        catch (OperationCanceledException)
        {
            record.Status = TestRunStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent loop crashed for run {RunId}", record.Id);
            record.Status = TestRunStatus.Errored;
            record.Report = new BugReport(
                Summary: $"Run errored: {ex.Message}",
                OverallSeverity: BugSeverity.Critical,
                Findings: new[]
                {
                    new BugFinding("Agent execution error", ex.Message, BugSeverity.Critical, null, null)
                },
                StepsExecuted: record.Steps.Count,
                Duration: sw.Elapsed);
        }
        finally
        {
            record.FinishedAt = DateTimeOffset.UtcNow;
            await _store.UpdateAsync(record, ct);
            await _publisher.PublishAsync(new ProgressEvent(
                record.Id,
                ProgressEventKind.RunFinished,
                DateTimeOffset.UtcNow,
                Status: record.Status,
                Report: record.Report), ct);
        }
    }

    internal async Task<BugReport> DriveAgentLoopAsync(
        TestRunRecord record,
        IBrowserToolbox toolbox,
        CancellationToken ct)
    {
        var chat = _chatFactory.Create();

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompts.Planner),
            new UserChatMessage(
                $"Starting URL: {record.Url}\nInstruction: {record.Instruction}\n" +
                "Begin by calling navigate, then read_page to orient yourself.")
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.2f,
            ToolChoice = ChatToolChoice.CreateAutoChoice()
        };
        foreach (var tool in AgentTools.All) chatOptions.Tools.Add(tool);

        for (var step = 0; step < _options.MaxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            var completion = await chat.CompleteChatAsync(messages, chatOptions, ct);
            var assistant = completion.Value;
            messages.Add(new AssistantChatMessage(assistant));

            if (assistant.FinishReason == ChatFinishReason.Stop &&
                assistant.ToolCalls.Count == 0)
            {
                // Model declined to call a tool — synthesize a Low-severity finding so the
                // user gets *something* back rather than a silent pass.
                return new BugReport(
                    Summary: "Agent stopped without calling finish_run.",
                    OverallSeverity: BugSeverity.Low,
                    Findings: Array.Empty<BugFinding>(),
                    StepsExecuted: record.Steps.Count,
                    Duration: TimeSpan.Zero);
            }

            foreach (var call in assistant.ToolCalls)
            {
                if (call.FunctionName == AgentTools.Finish)
                {
                    return ParseFinish(call.FunctionArguments, record.Steps.Count);
                }

                var (stepRecord, toolMessage) = await ExecuteToolAsync(record, toolbox, call, ct);
                record.Steps.Add(stepRecord);
                await _store.UpdateAsync(record, ct);
                await _publisher.PublishAsync(new ProgressEvent(
                    record.Id,
                    stepRecord.Status == StepStatus.Failed
                        ? ProgressEventKind.StepFailed
                        : ProgressEventKind.StepCompleted,
                    DateTimeOffset.UtcNow,
                    Step: stepRecord), ct);

                messages.Add(toolMessage);
            }
        }

        // Hit the step ceiling — record a Medium finding so the user knows the run was truncated.
        return new BugReport(
            Summary: $"Reached step ceiling of {_options.MaxSteps} without finishing.",
            OverallSeverity: BugSeverity.Medium,
            Findings: new[]
            {
                new BugFinding(
                    "Step ceiling reached",
                    $"The agent did not call finish_run within {_options.MaxSteps} steps. " +
                    "Consider raising Agent:MaxSteps or narrowing the instruction.",
                    BugSeverity.Medium,
                    Reproduction: null,
                    RelatedStepIndex: record.Steps.Count - 1)
            },
            StepsExecuted: record.Steps.Count,
            Duration: TimeSpan.Zero);
    }

    private async Task<(TestStep step, ToolChatMessage message)> ExecuteToolAsync(
        TestRunRecord record,
        IBrowserToolbox toolbox,
        ChatToolCall call,
        CancellationToken ct)
    {
        var index = record.Steps.Count;
        var args = JsonDocument.Parse(call.FunctionArguments).RootElement;
        var description = args.GetStringOrNull("description") ?? call.FunctionName;

        await _publisher.PublishAsync(new ProgressEvent(
            record.Id,
            ProgressEventKind.StepStarted,
            DateTimeOffset.UtcNow,
            Step: new TestStep(index, call.FunctionName, description, StepStatus.Started, DateTimeOffset.UtcNow)), ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.StepTimeoutSeconds));

        ToolResult result;
        string? screenshot = null;

        try
        {
            (result, screenshot) = call.FunctionName switch
            {
                AgentTools.Navigate =>
                    Lift(await toolbox.NavigateAsync(args.GetString("url"), timeout.Token)),
                AgentTools.Click =>
                    Lift(await toolbox.ClickAsync(args.GetString("selector"), description, timeout.Token)),
                AgentTools.Fill =>
                    Lift(await toolbox.FillAsync(args.GetString("selector"), args.GetString("value"), description, timeout.Token)),
                AgentTools.Press =>
                    Lift(await toolbox.PressAsync(args.GetString("key"), description, timeout.Token)),
                AgentTools.ReadPage =>
                    Lift(await toolbox.ReadPageAsync(timeout.Token)),
                AgentTools.WaitFor =>
                    Lift(await toolbox.WaitForAsync(args.GetString("selector"), args.GetIntOr("timeoutMs", 5_000), timeout.Token)),
                AgentTools.Assert =>
                    Lift(await toolbox.AssertAsync(
                        args.GetString("description"),
                        args.GetStringOrNull("selector"),
                        args.GetStringOrNull("expectedText"),
                        ParseSeverity(args.GetStringOrNull("severityIfFailed")),
                        timeout.Token)),
                AgentTools.Screenshot => await toolbox.ScreenshotAsync(timeout.Token),
                _ => (new ToolResult(false, $"Unknown tool '{call.FunctionName}'."), null)
            };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            result = new ToolResult(false, $"Tool '{call.FunctionName}' exceeded step timeout.");
        }
        catch (Exception ex)
        {
            result = new ToolResult(false, $"Tool '{call.FunctionName}' threw: {ex.Message}");
        }

        var step = new TestStep(
            index,
            call.FunctionName,
            description,
            result.Success ? StepStatus.Succeeded : StepStatus.Failed,
            DateTimeOffset.UtcNow,
            Detail: result.Message,
            ScreenshotBase64: screenshot);

        // Body of the tool message contains both the human-readable result and (if any) the
        // accessibility snapshot — that's what the model uses to pick the next selector.
        var body = result.Snapshot is null
            ? result.Message
            : $"{result.Message}\n\n[accessibility snapshot]\n{result.Snapshot}";
        var toolMessage = new ToolChatMessage(call.Id, body);

        return (step, toolMessage);
    }

    private static (ToolResult, string?) Lift(ToolResult r) => (r, null);

    private static BugSeverity ParseSeverity(string? value) =>
        Enum.TryParse<BugSeverity>(value, ignoreCase: true, out var s) ? s : BugSeverity.Medium;

    private static BugReport ParseFinish(BinaryData arguments, int stepsExecuted)
    {
        var args = JsonDocument.Parse(arguments).RootElement;
        var summary = args.GetStringOrNull("summary") ?? "(no summary)";
        var overall = ParseSeverity(args.GetStringOrNull("overallSeverity"));
        var findings = new List<BugFinding>();

        if (args.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in arr.EnumerateArray())
            {
                findings.Add(new BugFinding(
                    Title: f.GetStringOrNull("title") ?? "Untitled",
                    Description: f.GetStringOrNull("description") ?? "",
                    Severity: ParseSeverity(f.GetStringOrNull("severity")),
                    Reproduction: f.GetStringOrNull("reproduction"),
                    RelatedStepIndex: f.GetIntOrNull("relatedStepIndex")));
            }
        }

        return new BugReport(summary, overall, findings, stepsExecuted, TimeSpan.Zero);
    }
}

internal static class JsonElementExtensions
{
    public static string GetString(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : throw new ArgumentException($"Missing required argument '{name}'.");

    public static string? GetStringOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public static int GetIntOr(this JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : fallback;

    public static int? GetIntOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;
}
