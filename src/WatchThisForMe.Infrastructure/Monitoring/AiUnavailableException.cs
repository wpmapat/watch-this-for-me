using System.Net;

namespace WatchThisForMe.Infrastructure.Monitoring;

// "The page was fetched fine, but the AI couldn't analyse the change." Kept apart from website
// failures because it says nothing about the watch: an OpenAI outage, an expired key or used-up
// credit would otherwise count toward stopping every watch whose page changed meanwhile.
// Nothing is lost - the change isn't saved until it's analysed, so the next check sees the same
// difference and tries again.
public class AiUnavailableException(string message, Exception innerException) : Exception(message, innerException)
{
    private const string WillRetry = " The page change isn't lost - it will be analysed on the next check.";

    public static AiUnavailableException From(Exception ex) => new(Describe(ex) + WillRetry, ex);

    // Shown on the dashboard, so say what it means rather than echo the raw error.
    private static string Describe(Exception ex) => (ex as HttpRequestException)?.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "AI analysis failed: OpenAI rejected our API key (expired or revoked?).",
        HttpStatusCode.TooManyRequests =>
            "AI analysis failed: OpenAI is limiting our requests, or the account is out of credit.",
        _ => "AI analysis is temporarily unavailable (OpenAI didn't answer)."
    };
}
