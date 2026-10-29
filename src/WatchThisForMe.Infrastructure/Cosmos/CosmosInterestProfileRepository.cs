using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using WatchThisForMe.Core.Preferences;

namespace WatchThisForMe.Infrastructure.Cosmos;

public class CosmosInterestProfileRepository : IInterestProfileRepository
{
    private readonly Container _container;

    public CosmosInterestProfileRepository(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var settings = options.Value;
        _container = client.GetContainer(settings.DatabaseName, settings.InterestProfileContainerName);
    }

    public async Task<InterestProfile> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<InterestProfile>(
                InterestProfile.DefaultId, new PartitionKey(InterestProfile.DefaultId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new InterestProfile();
        }
    }

    public async Task<InterestProfile> UpdateAsync(InterestProfile profile, CancellationToken cancellationToken = default)
    {
        profile.Id = InterestProfile.DefaultId;
        var response = await _container.UpsertItemAsync(profile, new PartitionKey(InterestProfile.DefaultId), cancellationToken: cancellationToken);
        return response.Resource;
    }
}
