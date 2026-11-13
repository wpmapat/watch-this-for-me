using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using WatchThisForMe.Web;
using WatchThisForMe.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5003");
});
builder.Services.AddScoped<ApiClient>();
builder.Services.AddCascadingAuthenticationState();

// Users sign in with Entra ID, and the app then requests a token for the Api on their behalf. Same
// rule as the Api: no Entra config is only tolerated in local Development (where the Api is open
// too); anywhere else it stops startup instead of serving the UI to anyone.
var entraConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();

    // Lets ApiClient send the user back through sign-in when no token can be obtained silently - e.g.
    // after an app restart, the sign-in cookie survives but the in-memory token cache doesn't.
    builder.Services.AddScoped<BlazorAuthenticationChallengeHandler>();

    // Provides the /MicrosoftIdentity/Account/SignIn and SignOut endpoints.
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
}
else
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "AzureAd:ClientId is not configured. Refusing to start outside Development without authentication.");
    }

    // The auth middleware below still runs, so its services must exist - with no schemes registered,
    // every request is simply anonymous.
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
}

var app = builder.Build();

if (!entraConfigured)
{
    app.Logger.LogWarning("AzureAd is not configured - the UI is open to ANONYMOUS users (Development only).");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
if (entraConfigured)
{
    app.MapControllers();
}
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
