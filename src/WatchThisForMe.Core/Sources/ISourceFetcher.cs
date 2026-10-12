using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Core.Sources;

public interface ISourceFetcher
{
    SourceType SourceType { get; }

    Task<SourceSnapshot> FetchAsync(string source, CancellationToken cancellationToken = default);
}
