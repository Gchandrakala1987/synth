using System.Collections.Concurrent;
using Synth.Api.Models;

namespace Synth.Api.Services;

/// <summary>
/// Persistence boundary for <see cref="TestRunRecord"/>s. The in-memory implementation
/// is intentional for the MVP — swap for Postgres/Cosmos with a single binding change.
/// </summary>
public interface ITestRunStore
{
    Task<TestRunRecord> CreateAsync(TestRunRequest request, CancellationToken ct);
    Task<TestRunRecord?> GetAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(TestRunRecord record, CancellationToken ct);
    Task<IReadOnlyList<TestRunRecord>> ListRecentAsync(int take, CancellationToken ct);
}

/// <summary>
/// Mutable representation kept inside the store. The API surfaces an immutable
/// <see cref="TestRunDto"/> snapshot so callers can't accidentally mutate state.
/// </summary>
public sealed class TestRunRecord
{
    public Guid Id { get; init; }
    public required string Url { get; init; }
    public required string Instruction { get; init; }
    public Viewport Viewport { get; init; } = Viewport.Default;
    public TestRunStatus Status { get; set; } = TestRunStatus.Queued;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<TestStep> Steps { get; } = new();
    public BugReport? Report { get; set; }

    public TestRunDto ToDto() => new(
        Id, Url, Instruction, Status, CreatedAt, StartedAt, FinishedAt,
        Steps.ToArray(), Report);
}

public sealed class InMemoryTestRunStore : ITestRunStore
{
    private readonly ConcurrentDictionary<Guid, TestRunRecord> _records = new();

    public Task<TestRunRecord> CreateAsync(TestRunRequest request, CancellationToken ct)
    {
        var record = new TestRunRecord
        {
            Id = Guid.NewGuid(),
            Url = request.Url,
            Instruction = request.Instruction,
            Viewport = request.Viewport ?? Viewport.Default
        };
        _records[record.Id] = record;
        return Task.FromResult(record);
    }

    public Task<TestRunRecord?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_records.GetValueOrDefault(id));

    public Task UpdateAsync(TestRunRecord record, CancellationToken ct)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TestRunRecord>> ListRecentAsync(int take, CancellationToken ct)
    {
        IReadOnlyList<TestRunRecord> recent = _records.Values
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToArray();
        return Task.FromResult(recent);
    }
}
