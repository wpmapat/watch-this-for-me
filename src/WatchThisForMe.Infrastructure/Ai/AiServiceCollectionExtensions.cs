using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiInterpretation(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>()
            ?? throw new InvalidOperationException($"Missing '{OpenAiOptions.SectionName}' configuration section.");

        services.AddSingleton(options);

        services.AddHttpClient<OpenAiResponsesClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }).AddStandardResilienceHandler();

        // Named (not typed) client: the investigation agent is a long-lived Singleton that keeps
        // its own MCP connection open across calls, so it's constructed manually below rather
        // than via the typed-client pattern (which would default to a new instance per resolution).
        services.AddHttpClient("OpenAiInvestigation", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<IEmbeddingGenerator, OpenAiEmbeddingGenerator>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }).AddStandardResilienceHandler();

        services.AddSingleton<IWatchRequestInterpreter, OpenAiWatchRequestInterpreter>();
        services.AddSingleton<IChangeClassifier, OpenAiChangeClassifier>();
        services.AddSingleton<IInvestigationAgent>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAiInvestigation");
            var logger = sp.GetRequiredService<ILogger<OpenAiInvestigationAgent>>();
            // The MCP server subprocess runs in the same environment as this app (Development locally,
            // Production in Azure). Hosts without an IHostEnvironment (e.g. the eval console) never
            // investigate, so the fallback only needs to be safe, not useful.
            var environmentName = sp.GetService<IHostEnvironment>()?.EnvironmentName ?? Environments.Production;
            return new OpenAiInvestigationAgent(
                httpClient, options, logger, environmentName, configuration["Mcp:ServerDirectory"]);
        });

        return services;
    }
}
