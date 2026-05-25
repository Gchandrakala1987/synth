using System.Threading.Channels;
using Synth.Api.Agent;
using Synth.Api.Models;
using Synth.Api.Services;

namespace Synth.Api.BackgroundServices;

/// <summary>
/// Decouples HTTP request handling from agent execution. The controller queues a run
/// (instant 202 response) and this worker pulls it from the channel and drives the
/// agent loop. Bounded channel + concurrency cap prevent runaway browser instances.
/// </summary>
public sealed class TestRunQueue
{
    private readonly Channel<Guid> _channel;

    public TestRunQueue()
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity: 100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(Guid runId, CancellationToken ct) =>
        _channel.Writer.WriteAsync(runId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

public sealed class TestRunWorker : BackgroundService
{
    private const int MaxConcurrentRuns = 2;

    private readonly TestRunQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TestRunWorker> _logger;
    private readonly SemaphoreSlim _gate = new(MaxConcurrentRuns, MaxConcurrentRuns);

    public TestRunWorker(
        TestRunQueue queue,
        IServiceScopeFactory scopes,
        ILogger<TestRunWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TestRunWorker started; concurrency = {Concurrency}", MaxConcurrentRuns);

        await foreach (var runId in _queue.ReadAllAsync(stoppingToken))
        {
            await _gate.WaitAsync(stoppingToken);

            // Intentionally not awaited — each run is a fire-and-forget task whose
            // lifecycle is bounded by the semaphore and the stopping token.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var orchestrator = scope.ServiceProvider.GetRequiredService<AgentOrchestrator>();
                    var store = scope.ServiceProvider.GetRequiredService<ITestRunStore>();

                    var record = await store.GetAsync(runId, stoppingToken);
                    if (record is null)
                    {
                        _logger.LogWarning("Queued run {RunId} not found in store", runId);
                        return;
                    }

                    await orchestrator.RunAsync(record, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error executing run {RunId}", runId);
                }
                finally
                {
                    _gate.Release();
                }
            }, stoppingToken);
        }
    }
}
