# Agent design

## The contract

The agent is given:

- A starting URL.
- A natural-language test instruction (e.g. *"verify the checkout flow"*, *"make sure signup
  rejects invalid emails"*).

It must emit, as its **only** terminating action, a structured `BugReport` consisting of:

- A 1–3 sentence summary.
- An overall severity (`Info → Low → Medium → High → Critical`).
- Zero or more findings, each with title, description, severity, and (optionally) a
  reproduction string and related step index.

Everything else is intermediate — tool calls against a real browser.

## The tool surface

A small toolbox is a feature, not a limitation. Every tool added to the surface is a path the
model can choose, which dilutes its attention and increases the rate of degenerate plans. We
ship the minimum that covers ~95% of web flows:

| Tool          | Purpose                                                                  |
|---------------|--------------------------------------------------------------------------|
| `navigate`    | Go to a URL.                                                             |
| `click`       | Click an element by selector.                                            |
| `fill`        | Fill an input.                                                           |
| `press`       | Press a key (Enter, Tab).                                                |
| `read_page`   | Return the accessibility snapshot of the current page.                   |
| `wait_for`    | Wait for a selector to be visible.                                       |
| `assert`      | Record an assertion. Becomes a bug finding if it fails.                  |
| `screenshot`  | Capture the viewport for visual evidence.                                |
| `finish_run`  | Terminate the loop and emit the final report.                            |

Notably absent: `evaluate_javascript`, `set_cookie`, `intercept_network`. They unlock real
power but also let the model bypass our safety rails. Add them deliberately, behind a flag.

## The conversation

Each run starts with two messages:

```
[system]  You are an autonomous QA engineer driving Chromium through tool calls...
[user]    Starting URL: <url>
          Instruction: <plain English>
          Begin by calling navigate, then read_page to orient yourself.
```

From there it's a strict tool-calling loop:

```
while not done and steps < ceiling:
    assistant = openai.chat.completions(messages, tools=AgentTools)
    messages.append(assistant)
    for call in assistant.tool_calls:
        result = execute_tool(call)              # via PlaywrightBrowserToolbox
        messages.append(tool_message(call, result))
        publish_progress_event(call, result)     # SignalR fan-out
    if any call was finish_run:
        return call.report
return truncated_report()
```

Two design choices worth calling out:

1. **`temperature=0.2`** — we want the model deterministic enough to follow a sane plan but
   not so locked down that it can't try alternate selectors when the first one fails.
2. **Tool results carry the accessibility snapshot back to the model.** After every action we
   refresh the page snapshot and embed it in the tool result. The model never operates on a
   stale view of the page.

## Why accessibility trees beat DOM snapshots

Real-world example. Adding a single item to cart on a typical Shopify storefront:

| Input format       | Tokens (gpt-4o-mini) |
|--------------------|----------------------|
| `outerHTML` of body| ~28,000              |
| Accessibility tree | ~3,200               |
| Visible text only  | ~1,800 (but selectors disappear) |

The accessibility tree retains role + name + state — exactly the dimensions the model uses to
pick selectors like `role=button[name="Add to cart"]`. Those selectors also degrade gracefully
when the underlying CSS changes, which keeps our agent more resilient than scripts hand-tuned
to a specific class name.

## Failure modes and how we handle them

| Failure                            | Mitigation                                                                                              |
|------------------------------------|---------------------------------------------------------------------------------------------------------|
| Model never calls `finish_run`     | Hard cap at `Agent:MaxSteps`; orchestrator synthesizes a `Medium` finding explaining truncation.        |
| Tool call hangs                    | Per-step `CancellationToken` with `Agent:StepTimeoutSeconds`; returns a `Failed` step the model sees.   |
| Selector not found                 | Caught in `BrowserToolbox`; tool result is `Failed` with the Playwright error message verbatim.         |
| Page console errors                | Captured passively; surfaced in every `read_page` snapshot so the model can choose to file a finding.   |
| Model emits no tool calls at all   | Loop exits; we synthesize a `Low` finding ("Agent stopped without calling finish_run") for the user.    |
| API key missing                    | Fail fast at first chat call with a clear `InvalidOperationException` rather than per-run crashes.      |
| Page asks for credentials          | System prompt explicitly tells the model it has none; preferred response is a `Medium` finding.         |

## What would change if we wanted a "real" product

- **Per-tenant credential vaults** so the agent can log in (currently out of scope by design).
- **Recorded sessions** — replay the exact sequence of tool calls deterministically, without
  hitting the LLM again. The conversation log is already a recording; just persist it.
- **Vision tool** — Claude/GPT-4o multimodal lets the model see screenshots directly, which
  matters for visual bug categories the accessibility tree misses.
- **Selector library** — let users seed the agent with reusable selectors for known sites
  (faster, cheaper, more deterministic).
- **Cost guardrails** — per-run token budget, with the budget exposed to the model so it can
  decide when to file early vs. keep exploring.
