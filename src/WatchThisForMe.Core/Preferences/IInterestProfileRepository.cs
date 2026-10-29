namespace WatchThisForMe.Core.Preferences;

public interface IInterestProfileRepository
{
    // Returns an empty profile (never null) if one hasn't been set yet, so callers
    // always have something to feed into a prompt without null-checking everywhere.
    Task<InterestProfile> GetAsync(CancellationToken cancellationToken = default);

    Task<InterestProfile> UpdateAsync(InterestProfile profile, CancellationToken cancellationToken = default);
}
