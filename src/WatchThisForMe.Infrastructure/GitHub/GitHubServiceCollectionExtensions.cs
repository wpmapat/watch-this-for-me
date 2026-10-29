using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace WatchThisForMe.Infrastructure.GitHub;

public static class GitHubServiceCollectionExtensions
{
    public static IServiceCollection AddGitHub(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(GitHubOptions.SectionName).Get<GitHubOptions>() ?? new GitHubOptions();

        services.AddSingleton(options);

        services.AddHttpClient<GitHubClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            // GitHub's API rejects requests with no User-Agent header.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WatchThisForMe");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            if (!string.IsNullOrEmpty(options.Token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
            }
        }).AddStandardResilienceHandler();

        return services;
    }
}
