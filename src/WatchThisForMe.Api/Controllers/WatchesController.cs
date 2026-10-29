using Microsoft.AspNetCore.Mvc;
using WatchThisForMe.Api.Contracts;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Preferences;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Embeddings;
using WatchThisForMe.Infrastructure.Monitoring;

namespace WatchThisForMe.Api.Controllers;

[ApiController]
[Route("api/watches")]
public class WatchesController(
    IWatchRepository repository,
    IObservationRepository observationRepository,
    WatchCheckService checkService,
    IWatchRequestInterpreter requestInterpreter,
    IInvestigationAgent investigationAgent,
    IInterestProfileRepository interestProfileRepository,
    IWatchExecutionRepository executionRepository) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Watch>> Create(CreateWatchRequest request, CancellationToken cancellationToken)
    {
        var watch = new Watch
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            UserRequest = request.UserRequest,
            Source = request.Source,
            SourceType = request.SourceType,
            CheckFrequency = request.CheckFrequency,
            MeaningfulChangeCriteria = request.MeaningfulChangeCriteria,
            InvestigationInstructions = request.InvestigationInstructions,
            NotificationPolicy = request.NotificationPolicy,
            NotificationChannel = request.NotificationChannel,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await repository.CreateAsync(watch, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Watch>>> List(CancellationToken cancellationToken)
    {
        var watches = await repository.ListAsync(cancellationToken);
        return Ok(watches);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Watch>> Get(string id, CancellationToken cancellationToken)
    {
        var watch = await repository.GetAsync(id, cancellationToken);
        return watch is null ? NotFound() : Ok(watch);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Watch>> Update(string id, UpdateWatchRequest request, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        existing.Name = request.Name;
        existing.Source = request.Source;
        existing.SourceType = request.SourceType;
        existing.CheckFrequency = request.CheckFrequency;
        existing.MeaningfulChangeCriteria = request.MeaningfulChangeCriteria;
        existing.InvestigationInstructions = request.InvestigationInstructions;
        existing.NotificationPolicy = request.NotificationPolicy;
        existing.NotificationChannel = request.NotificationChannel;
        existing.Status = request.Status;

        var updated = await repository.UpdateAsync(existing, cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        await repository.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/check")]
    public async Task<ActionResult<Observation>> Check(string id, CancellationToken cancellationToken)
    {
        var observation = await checkService.CheckAsync(id, cancellationToken);
        return observation is null ? NotFound() : Ok(observation);
    }

    [HttpGet("{id}/observations")]
    public async Task<ActionResult<IReadOnlyList<Observation>>> ListObservations(string id, [FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        var observations = await observationRepository.ListAsync(id, count, cancellationToken);
        return Ok(observations);
    }

    [HttpGet("{id}/executions")]
    public async Task<ActionResult<IReadOnlyList<WatchExecution>>> ListExecutions(string id, [FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        var executions = await executionRepository.ListAsync(id, count, cancellationToken);
        return Ok(executions);
    }

    [HttpPost("{id}/investigate")]
    public async Task<ActionResult<InvestigationResult>> Investigate(string id, CancellationToken cancellationToken)
    {
        var watch = await repository.GetAsync(id, cancellationToken);
        if (watch is null)
        {
            return NotFound();
        }

        var latest = await observationRepository.GetLatestAsync(id, cancellationToken);
        if (latest?.DiffSummary is null)
        {
            return BadRequest("No diff available to investigate yet - check this watch at least twice first.");
        }

        // Reuse latest.Embedding (already computed from its own classification summary during
        // the check) as the query - no need to call the embedding API again here.
        var history = await observationRepository.ListAsync(id, maxCount: 10, cancellationToken);
        var similarPast = latest.Embedding is { } embedding
            ? EmbeddingSimilarity.FindTopMatches(embedding, history.Where(o => o.Id != latest.Id), topN: 3)
            : [];

        var interestProfile = await interestProfileRepository.GetAsync(cancellationToken);

        var result = await investigationAgent.InvestigateAsync(
            id, latest.DiffSummary, watch.MeaningfulChangeCriteria, similarPast, interestProfile.Interests, cancellationToken);
        return Ok(result);
    }

    [HttpPost("from-natural-language")]
    public async Task<ActionResult<Watch>> CreateFromNaturalLanguage(InterpretWatchRequest request, CancellationToken cancellationToken)
    {
        var draft = await requestInterpreter.InterpretAsync(request.Request, cancellationToken);

        var watch = new Watch
        {
            Id = Guid.NewGuid().ToString(),
            Name = draft.Name,
            UserRequest = request.Request,
            Source = draft.Source,
            SourceType = draft.SourceType,
            CheckFrequency = draft.CheckFrequency,
            MeaningfulChangeCriteria = draft.MeaningfulChangeCriteria,
            InvestigationInstructions = draft.InvestigationInstructions,
            NotificationPolicy = draft.NotificationPolicy,
            NotificationChannel = draft.NotificationChannel,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await repository.CreateAsync(watch, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }
}
