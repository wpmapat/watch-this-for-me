namespace WatchThisForMe.Core.Watches;

public interface IWatchRequestInterpreter
{
    Task<WatchDraft> InterpretAsync(string request, CancellationToken cancellationToken = default);
}
