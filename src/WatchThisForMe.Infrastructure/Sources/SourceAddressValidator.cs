using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using WatchThisForMe.Core.Watches;

namespace WatchThisForMe.Infrastructure.Sources;

// Checks a watch's address when it is created or edited, so the user gets a clear answer
// straight away instead of a failed check later. This is for friendliness only: the real
// protection is at connect time (SourceServiceCollectionExtensions), because a site's DNS can
// change after the watch was saved.
public class SourceAddressValidator(IConfiguration configuration)
{
    public async Task<string?> ValidateAsync(SourceType sourceType, string source, CancellationToken cancellationToken = default)
    {
        if (sourceType != SourceType.WebPage)
        {
            return null;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "The address must be a full web address starting with http:// or https://.";
        }

        if (configuration.GetValue<bool>(SourceServiceCollectionExtensions.AllowPrivateAddressesSetting))
        {
            return null;
        }

        IPAddress[] addresses;
        try
        {
            var host = uri.Host.Trim('[', ']');
            addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return null; // can't look it up right now; the check will report it in plain words
        }

        return addresses.All(PublicAddressPolicy.IsPublic) ? null : BlockedAddressException.FriendlyMessage;
    }
}
