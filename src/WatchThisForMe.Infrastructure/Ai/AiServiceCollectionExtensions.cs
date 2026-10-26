using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        });

        // Named (not typed) client: the investigation agent is a long-lived Singleton that keeps
        // its own MCP connection open across calls, so it's constructed manually below rather
        // than via the typed-client pattern (which would default to a new instance per resolution).
        services.AddHttpClient("OpenAiInvestigation", client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        });

        services.AddSingleton<IWatchRequestInterpreter, OpenAiWatchRequestInterpreter>();
        services.AddSingleton<IChangeClassifier, OpenAiChangeClassifier>();
        services.AddSingleton<IInvestigationAgent>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAiInvestigation");
            var logger = sp.GetRequiredService<ILogger<OpenAiInvestigationAgent>>();
            return new OpenAiInvestigationAgent(httpClient, options, logger);
        });

        return services;
    }
}
