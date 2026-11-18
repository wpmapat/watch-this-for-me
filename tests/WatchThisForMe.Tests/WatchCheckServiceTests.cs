using Microsoft.Extensions.Logging.Abstractions;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Monitoring;
using WatchThisForMe.Tests.Fakes;
using Xunit;

namespace WatchThisForMe.Tests;

// Exercises the full check pipeline (fetch -> detect change -> classify -> save -> prune,
// plus the retry/dead-letter bookkeeping) against in-memory fakes - no Cosmos, OpenAI or network.
public class WatchCheckServiceTests
{
    private const string WatchId = "watch-1";

    private readonly InMemoryWatchRepository _watches = new();
    private readonly InMemoryObservationRepository _observations = new();
    private readonly InMemoryWatchExecutionRepository _executions = new();
    private readonly ScriptedSourceFetcher _site = new();
    private readonly FakeChangeClassifier _classifier = new();
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly WatchCheckService _service;

    public WatchCheckServiceTests()
    {
        _service = new WatchCheckService(
            _watches, _observations, _site, _classifier, _embeddings,
            new FakeInterestProfileRepository(), _executions, NullLogger<WatchCheckService>.Instance);

        _watches.CreateAsync(new Watch
        {
            Id = WatchId,
            Name = "Test watch",
            UserRequest = "watch this",
            Source = "https://example.com/releases",
            SourceType = SourceType.WebPage,
            CheckFrequency = TimeSpan.FromHours(1),
            MeaningfulChangeCriteria = "New releases",
            NotificationPolicy = NotificationPolicy.OnlyMeaningfulChanges,
            NotificationChannel = NotificationChannel.Email,
            CreatedAt = DateTimeOffset.UtcNow
        }).Wait();
    }

    private async Task<Watch> CurrentWatch() => (await _watches.GetAsync(WatchId))!;

    // Runs a check that's expected to fail; the service records the failure, then rethrows.
    private async Task FailingCheck() =>
        await Assert.ThrowsAsync<HttpRequestException>(() => _service.CheckAsync(WatchId));

    // ---- Change detection ----

    [Fact]
    public async Task FirstCheck_SavesABaseline_WithoutCallingTheAI()
    {
        _site.ThenReturns("v1.0 released");

        var result = await _service.CheckAsync(WatchId);

        Assert.NotNull(result);
        Assert.Null(result.DiffSummary);
        Assert.Single(_observations.All);
        Assert.Empty(_classifier.Calls);
        Assert.Equal(0, _embeddings.Calls);
    }

    [Fact]
    public async Task UnchangedPage_SavesNothingNew_AndDoesNotCallTheAI()
    {
        _site.ThenReturns("v1.0 released").ThenReturns("v1.0 released");

        await _service.CheckAsync(WatchId);
        await _service.CheckAsync(WatchId);

        Assert.Single(_observations.All);
        Assert.Empty(_classifier.Calls);
    }

    // An observation saved by the old normalizer: no format version, whole page on one line.
    private Task SeedSnapshotFromBeforeTheUpgrade(string content) =>
        _observations.CreateAsync(new Observation
        {
            Id = "legacy-1",
            WatchId = WatchId,
            CheckedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ContentHash = content,
            NormalizedContent = content,
            Changed = true
        });

    [Fact]
    public async Task FirstCheckAfterNormalizerUpgrade_SavesANewBaseline_WithoutCallingTheAI()
    {
        // Old snapshot: whole page on one line (old normalizer). New snapshot: one line per block.
        await SeedSnapshotFromBeforeTheUpgrade("Releases v1.0 Footer");
        _site.ThenReturns("Releases\nv1.0\nFooter");

        var result = await _service.CheckAsync(WatchId);

        Assert.Equal(2, _observations.All.Count);
        Assert.Null(result!.DiffSummary);
        Assert.Empty(_classifier.Calls);
    }

    [Fact]
    public async Task AfterTheNewBaseline_RealChangesAreDiffedLineByLine()
    {
        await SeedSnapshotFromBeforeTheUpgrade("Releases v1.0 Footer");
        _site.ThenReturns("Releases\nv1.0\nFooter")
             .ThenReturns("Releases\nv1.1\nv1.0\nFooter");

        await _service.CheckAsync(WatchId);
        var result = await _service.CheckAsync(WatchId);

        Assert.Equal("+ v1.1", result!.DiffSummary);
        Assert.Equal("+ v1.1", Assert.Single(_classifier.Calls).Diff);
    }

    [Fact]
    public async Task ChangedPage_IsClassifiedAgainstTheWatchCriteria_AndSaved()
    {
        _site.ThenReturns("v1.0 released").ThenReturns("v1.0 released\nv2.0 released");

        await _service.CheckAsync(WatchId);
        var result = await _service.CheckAsync(WatchId);

        var call = Assert.Single(_classifier.Calls);
        Assert.Contains("v2.0", call.Diff);
        Assert.Equal("New releases", call.Criteria);

        Assert.NotNull(result);
        Assert.True(result.Meaningful);
        Assert.Equal("new_release", result.Category);
        Assert.Equal("A new release was published.", result.ClassificationSummary);
        Assert.NotNull(result.Embedding);
        Assert.Equal(2, _observations.All.Count);
    }

    [Fact]
    public async Task KeepsOnlyTheTenMostRecentChanges()
    {
        for (var i = 1; i <= 12; i++)
        {
            _site.ThenReturns($"version {i}");
        }

        for (var i = 1; i <= 12; i++)
        {
            await _service.CheckAsync(WatchId);
        }

        var kept = _observations.All;
        Assert.Equal(10, kept.Count);
        Assert.Equal("version 12", kept[0].NormalizedContent);
        Assert.DoesNotContain(kept, o => o.NormalizedContent is "version 1" or "version 2");
    }

    [Fact]
    public async Task UnknownWatch_ReturnsNull_AndRecordsNoRun()
    {
        var result = await _service.CheckAsync("no-such-watch");

        Assert.Null(result);
        Assert.Empty(_executions.All);
    }

    // ---- Failure handling ----

    [Fact]
    public async Task FailedFetch_RecordsTheError_AndTheAttempt_ButNotASuccessfulCheck()
    {
        _site.ThenFails("No such host is known.");

        await FailingCheck();

        var run = Assert.Single(_executions.All);
        Assert.Equal(WatchExecutionStatus.Failed, run.Status);
        Assert.Equal("No such host is known.", run.ErrorMessage);
        Assert.NotNull(run.CompletedAt);

        // The Nov 17 scheduler fix: the attempt is persisted even though the run failed,
        // so the scheduler waits a full interval before retrying.
        var watch = await CurrentWatch();
        Assert.NotNull(watch.LastAttemptAt);
        Assert.Null(watch.LastCheckedAt);
        Assert.Equal(WatchStatus.Active, watch.Status);
    }

    [Fact]
    public async Task ThreeFailuresInARow_StopTheWatch()
    {
        _site.ThenFails().ThenFails().ThenFails();

        await FailingCheck();
        await FailingCheck();
        Assert.Equal(WatchStatus.Active, (await CurrentWatch()).Status);

        await FailingCheck();

        Assert.Equal(WatchStatus.DeadLettered, (await CurrentWatch()).Status);
        Assert.Equal([1, 2, 3], _executions.All.Select(e => e.AttemptNumber));
        Assert.All(_executions.All, e => Assert.Equal(WatchExecutionStatus.Failed, e.Status));
    }

    [Fact]
    public async Task ASuccessResetsTheFailureCount()
    {
        _site.ThenFails().ThenFails().ThenReturns("v1.0 released").ThenFails().ThenFails();

        await FailingCheck();
        await FailingCheck();
        await _service.CheckAsync(WatchId);
        await FailingCheck();
        await FailingCheck();

        // Four failures in total, but never three in a row - the watch must still be running.
        Assert.Equal(WatchStatus.Active, (await CurrentWatch()).Status);
        // The success is itself attempt 3 (third try since the last success); the count restarts
        // at 1 on the run after it.
        Assert.Equal([1, 2, 3, 1, 2], _executions.All.Select(e => e.AttemptNumber));
    }

    [Fact]
    public async Task ResumedWatch_GetsAFreshSetOfAttempts()
    {
        _site.ThenFails().ThenFails().ThenFails()   // stopped
             .ThenFails().ThenFails()               // after resume: 2 failures - must survive
             .ThenFails();                          // 3rd failure since resume - stopped again

        await FailingCheck();
        await FailingCheck();
        await FailingCheck();
        Assert.Equal(WatchStatus.DeadLettered, (await CurrentWatch()).Status);

        // What POST api/watches/{id}/resume does.
        var watch = await CurrentWatch();
        watch.Status = WatchStatus.Active;
        watch.ResumedAt = DateTimeOffset.UtcNow;
        await _watches.UpdateAsync(watch);

        await FailingCheck();
        await FailingCheck();
        Assert.Equal(WatchStatus.Active, (await CurrentWatch()).Status);

        await FailingCheck();
        Assert.Equal(WatchStatus.DeadLettered, (await CurrentWatch()).Status);
        Assert.Equal([1, 2, 3, 1, 2, 3], _executions.All.Select(e => e.AttemptNumber));
    }

    [Fact]
    public async Task ARetryAfterAFailure_IsRecordedAsRetrying_UntilItFinishes()
    {
        _site.ThenFails().ThenReturns("v1.0 released");

        await FailingCheck();
        await _service.CheckAsync(WatchId);

        var retry = _executions.All[1];
        Assert.Equal(2, retry.AttemptNumber);
        Assert.Equal(WatchExecutionStatus.Succeeded, retry.Status);
        Assert.NotNull((await CurrentWatch()).LastCheckedAt);
    }

    [Fact]
    public async Task SiteOutage_ThenRecovery_ComparesAgainstTheLastSnapshotBeforeTheOutage()
    {
        // Site works, goes down for two checks, comes back with two releases published meanwhile.
        _site.ThenReturns("v1.0 released")
             .ThenFails().ThenFails()
             .ThenReturns("v1.0 released\nv2.0 released\nv3.0 released");

        await _service.CheckAsync(WatchId);
        await FailingCheck();
        await FailingCheck();
        var result = await _service.CheckAsync(WatchId);

        // Picks up from where it left off: everything since the pre-outage snapshot arrives as
        // ONE combined change, classified once - not one change per release.
        Assert.NotNull(result);
        Assert.Contains("v2.0", result.DiffSummary);
        Assert.Contains("v3.0", result.DiffSummary);
        Assert.Single(_classifier.Calls);
        Assert.Equal(2, _observations.All.Count);

        // Recovery clears the trouble: still running, and the success is recorded.
        var watch = await CurrentWatch();
        Assert.Equal(WatchStatus.Active, watch.Status);
        Assert.Equal(WatchExecutionStatus.Succeeded, _executions.All[^1].Status);
    }

    [Fact]
    public async Task AIOutage_DoesNotLoseTheChange_ItIsClassifiedOnTheNextCheck()
    {
        _site.ThenReturns("v1.0 released")
             .ThenReturns("v1.0 released\nv2.0 released")
             .ThenReturns("v1.0 released\nv2.0 released");

        await _service.CheckAsync(WatchId);

        _classifier.FailWith = new HttpRequestException("OpenAI unavailable");
        await Assert.ThrowsAsync<HttpRequestException>(() => _service.CheckAsync(WatchId));
        Assert.Single(_observations.All); // nothing half-saved

        _classifier.FailWith = null;
        var result = await _service.CheckAsync(WatchId);

        // The same page content is still compared against the old baseline, so the change
        // that arrived during the outage gets classified now rather than silently skipped.
        Assert.NotNull(result);
        Assert.True(result.Meaningful);
        Assert.Contains("v2.0", result.DiffSummary);
        Assert.Equal(2, _observations.All.Count);
    }
}
