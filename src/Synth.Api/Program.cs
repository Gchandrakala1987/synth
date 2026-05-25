using System.Text.Json.Serialization;
using Synth.Api.Agent;
using Synth.Api.BackgroundServices;
using Synth.Api.Configuration;
using Synth.Api.Hubs;
using Synth.Api.Services;
using Microsoft.Playwright;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog gives us readable, structured logs out of the box. Wire it up before anything else
// so startup failures are diagnosable.
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));

// ---- Options binding ----
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection(OpenAIOptions.Section));
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection(AzureOpenAIOptions.Section));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.Section));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.Section));

// ---- Application services ----
builder.Services.AddSingleton<ITestRunStore, InMemoryTestRunStore>();
builder.Services.AddSingleton<TestRunQueue>();
builder.Services.AddSingleton<IProgressPublisher, SignalRProgressPublisher>();
builder.Services.AddSingleton<IAgentChatClientFactory, AgentChatClientFactory>();
builder.Services.AddScoped<AgentOrchestrator>();
builder.Services.AddHostedService<TestRunWorker>();

// Playwright is expensive to construct — a single instance is reused across all runs;
// each run gets its own Browser/Context/Page via BrowserToolbox.CreateAsync.
builder.Services.AddSingleton(_ => Playwright.CreateAsync().GetAwaiter().GetResult());

// ---- Web stack ----
builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddSignalR().AddJsonProtocol(opts =>
{
    opts.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opts.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var corsSection = builder.Configuration.GetSection(CorsOptions.Section).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(policy => policy
        .WithOrigins(corsSection.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()); // required for SignalR sticky connections
});

var app = builder.Build();

// Ensure Chromium is installed for the chosen Playwright version. Idempotent — fast no-op
// on subsequent starts. Skipped in tests / when the env var is set.
if (Environment.GetEnvironmentVariable("AGENT_SKIP_BROWSER_INSTALL") is not "true")
{
    try
    {
        Microsoft.Playwright.Program.Main(new[] { "install", "chromium", "--with-deps" });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Playwright browser install step failed; runs will fail until resolved.");
    }
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRouting();

app.MapControllers();
app.MapHub<TestRunHub>("/hubs/test-runs");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.Run();

// Used by WebApplicationFactory<T> in the test project.
public partial class Program;
