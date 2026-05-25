using Synth.Api.Models;
using Synth.Api.Services;
using FluentAssertions;
using Xunit;

namespace Synth.Tests;

public class InMemoryTestRunStoreTests
{
    private static TestRunRequest SampleRequest() => new(
        Url: "https://example.com",
        Instruction: "smoke test the landing page");

    [Fact]
    public async Task Create_AssignsIdAndDefaultStatusQueued()
    {
        var store = new InMemoryTestRunStore();

        var record = await store.CreateAsync(SampleRequest(), CancellationToken.None);

        record.Id.Should().NotBeEmpty();
        record.Status.Should().Be(TestRunStatus.Queued);
        record.Viewport.Should().Be(Viewport.Default);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenIdUnknown()
    {
        var store = new InMemoryTestRunStore();

        (await store.GetAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task Update_PersistsLatestState()
    {
        var store = new InMemoryTestRunStore();
        var record = await store.CreateAsync(SampleRequest(), CancellationToken.None);

        record.Status = TestRunStatus.Passed;
        record.Steps.Add(new TestStep(0, "navigate", "first step", StepStatus.Succeeded, DateTimeOffset.UtcNow));
        await store.UpdateAsync(record, CancellationToken.None);

        var roundTrip = await store.GetAsync(record.Id, CancellationToken.None);
        roundTrip!.Status.Should().Be(TestRunStatus.Passed);
        roundTrip.Steps.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListRecent_ReturnsNewestFirstAndRespectsTake()
    {
        var store = new InMemoryTestRunStore();
        for (var i = 0; i < 5; i++)
        {
            await store.CreateAsync(SampleRequest(), CancellationToken.None);
            await Task.Delay(2); // ensure distinct CreatedAt ordering on fast machines
        }

        var recent = await store.ListRecentAsync(3, CancellationToken.None);

        recent.Should().HaveCount(3);
        recent.Select(r => r.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public void ToDto_ReturnsImmutableSnapshot()
    {
        var record = new TestRunRecord { Id = Guid.NewGuid(), Url = "https://x", Instruction = "i" };
        record.Steps.Add(new TestStep(0, "navigate", "a", StepStatus.Succeeded, DateTimeOffset.UtcNow));

        var dto = record.ToDto();
        record.Steps.Add(new TestStep(1, "click", "b", StepStatus.Succeeded, DateTimeOffset.UtcNow));

        dto.Steps.Should().HaveCount(1, "DTO must snapshot the steps at the time of conversion");
    }
}
