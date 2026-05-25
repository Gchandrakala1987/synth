using System.Text.Json;
using OpenAI.Chat;

namespace Synth.Api.Agent;

/// <summary>
/// The tool schema we expose to the LLM. Kept deliberately small — every extra tool is
/// another path the model can take, and a small toolbox produces more reliable agents.
/// </summary>
internal static class AgentTools
{
    public const string Navigate = "navigate";
    public const string Click = "click";
    public const string Fill = "fill";
    public const string Press = "press";
    public const string ReadPage = "read_page";
    public const string WaitFor = "wait_for";
    public const string Assert = "assert";
    public const string Screenshot = "screenshot";
    public const string Finish = "finish_run";

    public static IReadOnlyList<ChatTool> All { get; } = new[]
    {
        Tool(Navigate,
            "Navigate the browser to a fully-qualified URL.",
            Properties(("url", "string", "The absolute URL to visit."))),

        Tool(Click,
            "Click the element matching the selector. Prefer role/text selectors (e.g. 'role=button[name=\"Add to cart\"]') over CSS paths.",
            Properties(
                ("selector", "string", "Playwright selector for the element."),
                ("description", "string", "Short human-readable description of what you're clicking and why."))),

        Tool(Fill,
            "Fill a text input matching the selector with the given value.",
            Properties(
                ("selector", "string", "Playwright selector for the input."),
                ("value", "string", "Text to type."),
                ("description", "string", "Short human-readable description."))),

        Tool(Press,
            "Press a keyboard key (e.g. 'Enter', 'Tab').",
            Properties(
                ("key", "string", "Key name accepted by Playwright."),
                ("description", "string", "Short human-readable description."))),

        Tool(ReadPage,
            "Return a compact accessibility-tree snapshot of the current page so you can pick selectors.",
            Properties()),

        Tool(WaitFor,
            "Wait until the selector is visible (or timeout). Use sparingly; navigation actions auto-wait.",
            Properties(
                ("selector", "string", "Playwright selector to wait for."),
                ("timeoutMs", "integer", "Maximum wait in milliseconds. Default 5000."))),

        Tool(Assert,
            "Record an assertion about page state. Becomes a bug finding if it fails.",
            Properties(
                ("description", "string", "Plain-English description of what you are asserting."),
                ("selector", "string", "Optional selector whose presence/visibility you expect."),
                ("expectedText", "string", "Optional text that should appear on the page."),
                ("severityIfFailed", "string", "One of: Info, Low, Medium, High, Critical."))),

        Tool(Screenshot,
            "Capture a screenshot of the current viewport. Useful before filing a visual finding.",
            Properties()),

        Tool(Finish,
            "End the run and emit the final bug report. Call this exactly once.",
            Properties(
                ("summary", "string", "1-3 sentence summary of what was tested and the result."),
                ("overallSeverity", "string", "Info, Low, Medium, High, or Critical."),
                ("findings", "array", "Array of {title, description, severity, reproduction, relatedStepIndex} objects.")))
    };

    private static ChatTool Tool(string name, string description, BinaryData schema) =>
        ChatTool.CreateFunctionTool(name, description, schema);

    private static BinaryData Properties(params (string name, string type, string description)[] props)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var (name, type, description) in props)
        {
            properties[name] = new Dictionary<string, object>
            {
                ["type"] = type,
                ["description"] = description
            };
            // Heuristic: 'description' on action tools is required; everything else is optional
            // unless callers need it. Keep schemas permissive — strict required lists make the
            // model hallucinate placeholder values to satisfy them.
            if (name is "url" or "selector" or "value" or "key" or "summary" or "overallSeverity" or "findings")
            {
                required.Add(name);
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };

        return BinaryData.FromString(JsonSerializer.Serialize(schema));
    }
}
