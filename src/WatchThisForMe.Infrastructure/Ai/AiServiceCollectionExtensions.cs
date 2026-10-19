using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton<IWatchRequestInterpreter, OpenAiWatchRequestInterpreter>();
        services.AddSingleton<IChangeClassifier, OpenAiChangeClassifier>();

        return services;
    }
}
