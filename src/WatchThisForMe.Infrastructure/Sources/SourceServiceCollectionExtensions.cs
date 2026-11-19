using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using WatchThisForMe.Core.Sources;

namespace WatchThisForMe.Infrastructure.Sources;

public static class SourceServiceCollectionExtensions
{
    // Off everywhere by default. Only the local scenario scripts turn it on, because their fake
    // website runs on localhost.
    public const string AllowPrivateAddressesSetting = "Sources:AllowPrivateAddresses";

    public static IServiceCollection AddSourceFetchers(this IServiceCollection services)
    {
        services.AddHttpClient<ISourceFetcher, WebPageSourceFetcher>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var allowPrivate = sp.GetRequiredService<IConfiguration>().GetValue<bool>(AllowPrivateAddressesSetting);
                return CreateGuardedHandler(allowPrivate ? _ => true : PublicAddressPolicy.IsPublic);
            })
            .AddStandardResilienceHandler(options =>
            {
                // A blocked address will never start working, so don't retry it.
                options.Retry.ShouldHandle = args => ValueTask.FromResult(
                    !IsBlocked(args.Outcome.Exception) && HttpClientResiliencePredicates.IsTransient(args.Outcome));
            });

        services.AddSingleton<SourceAddressValidator>();

        return services;
    }

    public static bool IsBlocked(Exception? ex) =>
        ex is BlockedAddressException || ex?.InnerException is BlockedAddressException;

    // The check happens at CONNECT time, for every connection - so it also covers each redirect
    // hop (a public page that redirects to http://127.0.0.1 is caught on the second hop).
    // We look the name up ourselves, check every address, and then connect to exactly those
    // addresses. Checking the name and letting the system look it up again would allow
    // "DNS rebinding": a hostile DNS server answers "public" for the check and "127.0.0.1" for
    // the real connection.
    public static SocketsHttpHandler CreateGuardedHandler(Func<IPAddress, bool> isAllowed) => new()
    {
        UseProxy = false, // a proxy would make the connection, bypassing the check below
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host.Trim('[', ']');
            var addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);

            // If ANY answer is internal, refuse: a mixed answer is a classic rebinding setup.
            if (addresses.Length == 0 || addresses.Any(a => !isAllowed(a)))
            {
                throw new BlockedAddressException(BlockedAddressException.FriendlyMessage);
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
