using Synth.Api.BackgroundServices;
using Synth.Api.Models;
using Synth.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Synth.Api.Controllers;

[ApiController]
[Route("api/test-runs")]
[Produces("application/json")]
public sealed class TestRunsController : ControllerBase
{
    private readonly ITestRunStore _store;
    private readonly TestRunQueue _queue;
    private readonly ILogger<TestRunsController> _logger;

    public TestRunsController(
        ITestRunStore store,
        TestRunQueue queue,
        ILogger<TestRunsController> logger)
    {
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>Queue a new test run. Returns 202 with the new run's id immediately.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TestRunDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] TestRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url) ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return BadRequest(new { error = "url must be a fully-qualified http(s) URL." });
        }
        if (string.IsNullOrWhiteSpace(request.Instruction))
        {
            return BadRequest(new { error = "instruction is required." });
        }

        var record = await _store.CreateAsync(request, ct);
        await _queue.EnqueueAsync(record.Id, ct);
        _logger.LogInformation("Queued run {RunId} for {Url}", record.Id, record.Url);

        return AcceptedAtAction(nameof(Get), new { id = record.Id }, record.ToDto());
    }

    /// <summary>Get the current snapshot of a run.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TestRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var record = await _store.GetAsync(id, ct);
        return record is null ? NotFound() : Ok(record.ToDto());
    }

    /// <summary>List the 50 most recent runs.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TestRunDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var records = await _store.ListRecentAsync(50, ct);
        return Ok(records.Select(r => r.ToDto()));
    }
}
