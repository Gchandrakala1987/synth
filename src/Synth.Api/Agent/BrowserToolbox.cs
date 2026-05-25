using System.Diagnostics;
using Synth.Api.Models;
using Microsoft.Playwright;

namespace Synth.Api.Agent;

/// <summary>
/// Wraps a Playwright <see cref="IPage"/> and exposes the operations that map 1:1
/// to <see cref="AgentTools"/>. Each method returns a <see cref="ToolResult"/> the
/// orchestrator can serialize back to the model.
///
/// This is the only class that touches Playwright directly. Keep it that way — the
/// orchestrator stays pure, which makes the agent loop trivially unit-testable with
/// a fake <see cref="IBrowserToolbox"/>.
/// </summary>
public interface IBrowserToolbox : IAsyncDisposable
{
    Task<ToolResult> NavigateAsync(string url, CancellationToken ct);
    Task<ToolResult> ClickAsync(string selector, string description, CancellationToken ct);
    Task<ToolResult> FillAsync(string selector, string value, string description, CancellationToken ct);
    Task<ToolResult> PressAsync(string key, string description, CancellationToken ct);
    Task<ToolResult> ReadPageAsync(CancellationToken ct);
    Task<ToolResult> WaitForAsync(string selector, int timeoutMs, CancellationToken ct);
    Task<ToolResult> AssertAsync(string description, string? selector, string? expectedText, BugSeverity severityIfFailed, CancellationToken ct);
    Task<(ToolResult result, string? screenshotBase64)> ScreenshotAsync(CancellationToken ct);
}

public sealed record ToolResult(bool Success, string Message, string? Snapshot = null);

public sealed class PlaywrightBrowserToolbox : IBrowserToolbox
{
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private readonly ILogger _logger;
    private readonly List<string> _consoleErrors = new();

    private PlaywrightBrowserToolbox(IBrowser browser, IBrowserContext context, IPage page, ILogger logger)
    {
        _browser = browser;
        _context = context;
        _page = page;
        _logger = logger;

        // We surface console errors and uncaught page errors as part of every read_page snapshot.
        // It's a cheap win — the model uses them to file bug findings without explicit prompting.
        _page.Console += (_, msg) =>
        {
            if (msg.Type == "error") _consoleErrors.Add(msg.Text);
        };
        _page.PageError += (_, err) => _consoleErrors.Add($"pageerror: {err}");
    }

    public static async Task<PlaywrightBrowserToolbox> CreateAsync(
        IPlaywright playwright,
        Viewport viewport,
        bool headless,
        ILogger logger,
        CancellationToken ct)
    {
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = viewport.Width, Height = viewport.Height },
            IgnoreHTTPSErrors = true
        });
        var page = await context.NewPageAsync();
        return new PlaywrightBrowserToolbox(browser, context, page, logger);
    }

    public async Task<ToolResult> NavigateAsync(string url, CancellationToken ct)
    {
        try
        {
            var response = await _page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 15_000
            });
            var status = response?.Status ?? 0;
            if (status >= 400)
            {
                return new ToolResult(false, $"Navigation returned HTTP {status}.");
            }
            return new ToolResult(true, $"Loaded {url} (HTTP {status}).");
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
        {
            // Many real sites hold long-lived connections (analytics, websockets) that prevent
            // NetworkIdle. Treat this as a soft success and let the model decide whether the page
            // is usable based on the snapshot.
            return new ToolResult(true, $"Loaded {url} (no networkidle within 15s, proceeding).");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Navigation failed: {ex.Message}");
        }
    }

    public async Task<ToolResult> ClickAsync(string selector, string description, CancellationToken ct)
    {
        try
        {
            await _page.Locator(selector).First.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
            return new ToolResult(true, $"Clicked {selector} ({description}).");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Click on '{selector}' failed: {ex.Message}");
        }
    }

    public async Task<ToolResult> FillAsync(string selector, string value, string description, CancellationToken ct)
    {
        try
        {
            await _page.Locator(selector).First.FillAsync(value, new LocatorFillOptions { Timeout = 5_000 });
            return new ToolResult(true, $"Filled {selector} with '{Truncate(value, 60)}' ({description}).");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Fill on '{selector}' failed: {ex.Message}");
        }
    }

    public async Task<ToolResult> PressAsync(string key, string description, CancellationToken ct)
    {
        try
        {
            await _page.Keyboard.PressAsync(key);
            return new ToolResult(true, $"Pressed {key} ({description}).");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Key press '{key}' failed: {ex.Message}");
        }
    }

    public async Task<ToolResult> ReadPageAsync(CancellationToken ct)
    {
        try
        {
            // Accessibility tree is far cheaper (in tokens) than raw HTML and far more stable
            // across cosmetic markup changes — exactly what we want the agent reasoning over.
            var tree = await _page.Accessibility.SnapshotAsync(new AccessibilitySnapshotOptions
            {
                InterestingOnly = true
            });

            var url = _page.Url;
            var title = await _page.TitleAsync();
            var snapshotJson = System.Text.Json.JsonSerializer.Serialize(tree);
            var truncated = Truncate(snapshotJson, 6_000);

            var errors = _consoleErrors.Count > 0
                ? "\nConsole errors observed so far:\n - " + string.Join("\n - ", _consoleErrors.TakeLast(10))
                : "";

            return new ToolResult(
                true,
                $"URL: {url}\nTitle: {title}{errors}",
                Snapshot: truncated);
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Page read failed: {ex.Message}");
        }
    }

    public async Task<ToolResult> WaitForAsync(string selector, int timeoutMs, CancellationToken ct)
    {
        try
        {
            await _page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = timeoutMs <= 0 ? 5_000 : timeoutMs,
                State = WaitForSelectorState.Visible
            });
            return new ToolResult(true, $"'{selector}' became visible.");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Wait for '{selector}' timed out: {ex.Message}");
        }
    }

    public async Task<ToolResult> AssertAsync(
        string description,
        string? selector,
        string? expectedText,
        BugSeverity severityIfFailed,
        CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(selector))
            {
                var visible = await _page.Locator(selector).First.IsVisibleAsync();
                if (!visible)
                {
                    return new ToolResult(false, $"FAIL ({severityIfFailed}): {description} — selector '{selector}' not visible.");
                }
            }
            if (!string.IsNullOrWhiteSpace(expectedText))
            {
                var body = await _page.InnerTextAsync("body");
                if (!body.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
                {
                    return new ToolResult(false, $"FAIL ({severityIfFailed}): {description} — expected text '{expectedText}' not found.");
                }
            }
            return new ToolResult(true, $"PASS: {description}");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Assertion errored: {ex.Message}");
        }
    }

    public async Task<(ToolResult result, string? screenshotBase64)> ScreenshotAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var bytes = await _page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = false,
                Type = ScreenshotType.Png
            });
            var b64 = Convert.ToBase64String(bytes);
            return (new ToolResult(true, $"Captured screenshot ({bytes.Length} bytes, {sw.ElapsedMilliseconds} ms)."), b64);
        }
        catch (Exception ex)
        {
            return (new ToolResult(false, $"Screenshot failed: {ex.Message}"), null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _context.CloseAsync(); } catch { /* ignore */ }
        try { await _browser.CloseAsync(); } catch { /* ignore */ }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "\n…[truncated]";
}
