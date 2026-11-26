using Microsoft.Extensions.Logging.Abstractions;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Monitoring;
using WatchThisForMe.Tests.Fakes;
using Xunit;

namespace WatchThisForMe.Tests;

// Each run tells its own story for the watch's history page: which steps happened, in order,
// how each ended - and, when a run fails, exactly which step it stopped at.
public class ExecutionHistoryTests
{
    private const string WatchId = "watch-1";

    private readonly InMemoryWatchRepository _watches = new();
    private readonly InMemoryObservationRepository _observations = new();
    private readonly InMemoryWatchExecutionRepository _executions = new();
    private readonly ScriptedSourceFetcher _site = new();
    private readonly FakeChangeClassifier _classifier = new();
    private readonly FakeInvestigationAgent _agent = new();
    private readonly WatchCheckService _service;

    public ExecutionHistoryTests()
    {
        _service = new WatchCheckService(
            _watches, _observations, _site, _classifier, new FakeEmbeddingGenerator(),
            new FakeInterestProfileRepository(), _executions,
            TestNotifier.Create(_observations, agent: _agent), NullLogger<WatchCheckService>.Instance);

        _watches.CreateAsync(new Watch
        {
            Id = WatchId,
            Name = "Releases",
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

    private WatchExecution LatestRun => _executions.All[^1];

    private static void AssertSteps(WatchExecution run, params (string Name, StepOutcome Outcome)[] expected) =>
        Assert.Equal(expected, run.Steps.Select(s => (s.Name, s.Outcome)).ToArray());

    private static ExecutionStep Step(WatchExecution run, string name) => Assert.Single(run.Steps, s => s.Name == name);

    [Fact]
    public async Task FirstCheck_FetchesAndSavesAStartingPoint()
    {
        _site.ThenReturns("v1.0");

        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun, ("Page fetch", StepOutcome.Ok), ("Comparison", StepOutcome.Ok));
        Assert.Contains("4 characters", Step(LatestRun, "Page fetch").Detail);
        Assert.Contains("First look", Step(LatestRun, "Comparison").Detail);
    }

    [Fact]
    public async Task UnchangedPage_StopsAfterTheComparison()
    {
        _site.ThenReturns("v1.0").ThenReturns("v1.0");

        await _service.CheckAsync(WatchId);
        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun, ("Page fetch", StepOutcome.Ok), ("Comparison", StepOutcome.Ok));
        Assert.Equal("No change since the last check.", Step(LatestRun, "Comparison").Detail);
    }

    [Fact]
    public async Task MeaningfulChange_ShowsTheWholeJourney_ToTheNotice()
    {
        _site.ThenReturns("v1.0").ThenReturns("v1.0\nv2.0\nv2.1");

        await _service.CheckAsync(WatchId);
        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun,
            ("Page fetch", StepOutcome.Ok),
            ("Comparison", StepOutcome.Ok),
            ("AI analysis", StepOutcome.Highlight),
            ("Investigation", StepOutcome.Highlight),
            ("Notice", StepOutcome.Highlight));
        Assert.Equal("The page changed: 2 lines added, 0 lines removed.", Step(LatestRun, "Comparison").Detail);
        Assert.Equal("Meaningful - new release, 90% sure. A new release was published.", Step(LatestRun, "AI analysis").Detail);
        Assert.StartsWith("Sent you a notice: ", Step(LatestRun, "Notice").Detail);
        Assert.All(LatestRun.Steps, s => Assert.True(s.DurationMs >= 0));
        var startTimes = LatestRun.Steps.Select(s => s.StartedAt).ToList();
        Assert.Equal(startTimes.Order(), startTimes);
    }

    [Fact]
    public async Task UnimportantChange_SaysWhyNoNoticeWasSent()
    {
        _classifier.Result = new ChangeClassification(false, "cosmetic", 0.8, "Only a typo was fixed.");
        _site.ThenReturns("v1.0").ThenReturns("v1.0 relased").ThenReturns("v1.0 released");

        await _service.CheckAsync(WatchId);
        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun,
            ("Page fetch", StepOutcome.Ok),
            ("Comparison", StepOutcome.Ok),
            ("AI analysis", StepOutcome.Ok),
            ("Notice", StepOutcome.Ok));
        Assert.StartsWith("Not important - cosmetic", Step(LatestRun, "AI analysis").Detail);
        Assert.StartsWith("No notice", Step(LatestRun, "Notice").Detail);
        Assert.Equal(0, _agent.Calls);
    }

    [Fact]
    public async Task InvestigationDisagrees_ShowsTheFalseAlarmAvoided()
    {
        _agent.AgreesItMatters = false;
        _site.ThenReturns("v1.0").ThenReturns("v1.0\nv2.0");

        await _service.CheckAsync(WatchId);
        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun,
            ("Page fetch", StepOutcome.Ok),
            ("Comparison", StepOutcome.Ok),
            ("AI analysis", StepOutcome.Highlight),
            ("Investigation", StepOutcome.Ok));
        Assert.Contains("didn't seem to matter", Step(LatestRun, "Investigation").Detail);
    }

    [Fact]
    public async Task ErrorPage_IsAWarning_AndNeedsNoNotice()
    {
        _classifier.Result = new ChangeClassification(true, ChangeCategories.PageUnavailable, 0.95, "The page shows a 404 error.");
        _site.ThenReturns("v1.0").ThenReturns("404 Not Found");

        await _service.CheckAsync(WatchId);
        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun,
            ("Page fetch", StepOutcome.Ok),
            ("Comparison", StepOutcome.Ok),
            ("AI analysis", StepOutcome.Warning));
    }

    [Fact]
    public async Task FailedFetch_ShowsWhereTheRunStopped()
    {
        _site.ThenFails("No such host is known.");

        await Assert.ThrowsAnyAsync<Exception>(() => _service.CheckAsync(WatchId));

        Assert.Equal(WatchExecutionStatus.Failed, LatestRun.Status);
        AssertSteps(LatestRun, ("Page fetch", StepOutcome.Failed));
        Assert.Equal("No such host is known.", Step(LatestRun, "Page fetch").Detail);
    }

    [Fact]
    public async Task AIOutage_ShowsTheAnalysisFailed_InPlainLanguage()
    {
        _site.ThenReturns("v1.0").ThenReturns("v1.0\nv2.0");
        await _service.CheckAsync(WatchId);

        _classifier.FailWith = new HttpRequestException("boom", null, System.Net.HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<AiUnavailableException>(() => _service.CheckAsync(WatchId));

        AssertSteps(LatestRun,
            ("Page fetch", StepOutcome.Ok),
            ("Comparison", StepOutcome.Ok),
            ("AI analysis", StepOutcome.Failed));
        Assert.Contains("rejected our API key", Step(LatestRun, "AI analysis").Detail);
    }

    [Fact]
    public async Task FailedNotice_IsAWarning_AndItsRetryShowsUpOnTheNextRun()
    {
        _site.ThenReturns("v1.0").ThenReturns("v1.0\nv2.0").ThenReturns("v1.0\nv2.0");
        await _service.CheckAsync(WatchId);

        _agent.FailWith = new HttpRequestException("OpenAI unavailable");
        await _service.CheckAsync(WatchId);

        Assert.Equal(WatchExecutionStatus.Succeeded, LatestRun.Status); // a failed notice doesn't fail the check
        Assert.Equal(("Notice", StepOutcome.Warning), (LatestRun.Steps[^1].Name, LatestRun.Steps[^1].Outcome));
        Assert.Contains("tried again on the next check", LatestRun.Steps[^1].Detail);

        _agent.FailWith = null;
        await _service.CheckAsync(WatchId);

        AssertSteps(LatestRun,
            ("Page fetch", StepOutcome.Ok),
            ("Comparison", StepOutcome.Ok),
            ("Investigation", StepOutcome.Highlight),
            ("Notice", StepOutcome.Highlight));
    }
}
