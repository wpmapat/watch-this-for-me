using System.Text.Json;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Preferences;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Tests.Fakes;

// In-memory stand-ins for the real dependencies, so pipeline tests run in milliseconds with no
// Cosmos, OpenAI or network. Stores copy objects on every write and read, like a real database:
// if code mutates an object but forgets to save it, the change is lost here too - so tests
// catch missing saves instead of hiding them behind shared references.

internal static class Copy
{
    public static T Of<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}

public class InMemoryWatchRepository : IWatchRepository
{
    private readonly Dictionary<string, Watch> _watches = [];

    public Task<Watch> CreateAsync(Watch watch, CancellationToken cancellationToken = default)
    {
        _watches[watch.Id] = Copy.Of(watch);
        return Task.FromResult(Copy.Of(watch));
    }

    public Task<Watch?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_watches.TryGetValue(id, out var w) ? Copy.Of(w) : null);

    public Task<IReadOnlyList<Watch>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Watch>>(_watches.Values.Select(Copy.Of).ToList());

    public Task<Watch> UpdateAsync(Watch watch, CancellationToken cancellationToken = default)
    {
        _watches[watch.Id] = Copy.Of(watch);
        return Task.FromResult(Copy.Of(watch));
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _watches.Remove(id);
        return Task.CompletedTask;
    }
}

// Insertion order stands in for CheckedAt order (most recent first on reads, as Cosmos returns
// them), so ordering doesn't depend on two checks getting distinct clock readings.
public class InMemoryObservationRepository : IObservationRepository
{
    private readonly List<Observation> _observations = [];

    public IReadOnlyList<Observation> All => _observations.AsEnumerable().Reverse().Select(Copy.Of).ToList();

    public Task<Observation> CreateAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        _observations.Add(Copy.Of(observation));
        return Task.FromResult(Copy.Of(observation));
    }

    public Task<Observation?> GetLatestAsync(string watchId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_observations.LastOrDefault(o => o.WatchId == watchId) is { } o ? Copy.Of(o) : null);

    public Task<IReadOnlyList<Observation>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default)
    {
        var mostRecentFirst = _observations.Where(o => o.WatchId == watchId).Reverse();
        if (maxCount is { } max)
        {
            mostRecentFirst = mostRecentFirst.Take(max);
        }

        return Task.FromResult<IReadOnlyList<Observation>>(mostRecentFirst.Select(Copy.Of).ToList());
    }

    public Task DeleteAsync(string id, string watchId, CancellationToken cancellationToken = default)
    {
        _observations.RemoveAll(o => o.Id == id && o.WatchId == watchId);
        return Task.CompletedTask;
    }
}

public class InMemoryWatchExecutionRepository : IWatchExecutionRepository
{
    private readonly List<WatchExecution> _executions = [];

    // Oldest first - reads naturally as "what happened, in order" in test assertions.
    public IReadOnlyList<WatchExecution> All => _executions.Select(Copy.Of).ToList();

    public Task<WatchExecution> CreateAsync(WatchExecution execution, CancellationToken cancellationToken = default)
    {
        _executions.Add(Copy.Of(execution));
        return Task.FromResult(Copy.Of(execution));
    }

    public Task<WatchExecution> UpdateAsync(WatchExecution execution, CancellationToken cancellationToken = default)
    {
        var index = _executions.FindIndex(e => e.Id == execution.Id);
        _executions[index] = Copy.Of(execution);
        return Task.FromResult(Copy.Of(execution));
    }

    public Task<IReadOnlyList<WatchExecution>> ListAsync(string watchId, int? maxCount = null, CancellationToken cancellationToken = default)
    {
        var mostRecentFirst = _executions.Where(e => e.WatchId == watchId).Reverse();
        if (maxCount is { } max)
        {
            mostRecentFirst = mostRecentFirst.Take(max);
        }

        return Task.FromResult<IReadOnlyList<WatchExecution>>(mostRecentFirst.Select(Copy.Of).ToList());
    }
}

// A website whose responses are scripted in advance: each fetch takes the next queued
// result - page text, or an exception to simulate the site being unreachable.
public class ScriptedSourceFetcher : ISourceFetcher
{
    private readonly Queue<Func<SourceSnapshot>> _responses = new();

    public SourceType SourceType => SourceType.WebPage;

    public ScriptedSourceFetcher ThenReturns(string content)
    {
        // The content itself serves as its hash - identical text, identical hash.
        _responses.Enqueue(() => new SourceSnapshot(content, content, DateTimeOffset.UtcNow));
        return this;
    }

    public ScriptedSourceFetcher ThenFails(string message = "No such host is known.")
    {
        _responses.Enqueue(() => throw new HttpRequestException(message));
        return this;
    }

    public Task<SourceSnapshot> FetchAsync(string source, CancellationToken cancellationToken = default) =>
        _responses.Count == 0
            ? throw new InvalidOperationException("Test fetched more times than it scripted responses for.")
            : Task.FromResult(_responses.Dequeue()());
}

public class FakeChangeClassifier : IChangeClassifier
{
    public List<(string Diff, string Criteria)> Calls { get; } = [];

    public ChangeClassification Result { get; set; } = new(true, "new_release", 0.9, "A new release was published.");

    // When set, every call throws this - simulates the AI service being down.
    public Exception? FailWith { get; set; }

    public Task<ChangeClassification> ClassifyAsync(
        string diffSummary,
        string meaningfulChangeCriteria,
        IReadOnlyList<SimilarObservation> similarPastObservations,
        string userInterests,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((diffSummary, meaningfulChangeCriteria));
        return FailWith is not null ? throw FailWith : Task.FromResult(Result);
    }
}

public class FakeEmbeddingGenerator : IEmbeddingGenerator
{
    public int Calls { get; private set; }

    public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new[] { 1f, 0f, 0f });
    }
}

public class FakeInterestProfileRepository : IInterestProfileRepository
{
    public Task<InterestProfile> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new InterestProfile { Interests = "security fixes" });

    public Task<InterestProfile> UpdateAsync(InterestProfile profile, CancellationToken cancellationToken = default) =>
        Task.FromResult(profile);
}
