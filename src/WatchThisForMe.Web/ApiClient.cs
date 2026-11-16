using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;

namespace WatchThisForMe.Web;

// Hands components an HttpClient for the Api that carries the signed-in user's access token.
// Scoped (one per Blazor circuit) because that's where the user is known: a DelegatingHandler on
// the named client would run in IHttpClientFactory's own DI scope and can't see who's signed in.
public class ApiClient(
    IHttpClientFactory httpClientFactory,
    AuthenticationStateProvider authenticationStateProvider,
    IConfiguration configuration,
    ITokenAcquisition? tokenAcquisition = null,
    BlazorAuthenticationChallengeHandler? challengeHandler = null)
{
    public async Task<HttpClient> CreateAsync()
    {
        var client = httpClientFactory.CreateClient("Api");

        // Not registered when Entra isn't configured (local Development) - the Api is anonymous then too.
        if (tokenAcquisition is null)
        {
            return client;
        }

        var scopes = configuration.GetSection("Api:Scopes").Get<string[]>();
        if (scopes is not { Length: > 0 })
        {
            throw new InvalidOperationException("Api:Scopes must be configured when AzureAd is configured.");
        }

        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        string token;
        try
        {
            token = await tokenAcquisition.GetAccessTokenForUserAsync(scopes, user: user);
        }
        catch (Exception ex) when (challengeHandler is not null)
        {
            // If this needs the user to sign in again (e.g. the token cache was lost on restart while
            // the cookie survived), this starts that redirect; either way this call can't complete.
            await challengeHandler.HandleExceptionAsync(ex);
            throw;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
