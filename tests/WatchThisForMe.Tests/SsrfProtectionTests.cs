using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WatchThisForMe.Core.Sources;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Sources;
using Xunit;

namespace WatchThisForMe.Tests;

// SSRF: a watch's address must never make our server fetch its own or its network's private
// pages. Each attack below worked before the fix (Nov 19): the Api read its own watch list via
// http://localhost:5003/api/watches, and the disguised forms 2130706433 and [::1] got through too.
public class SsrfProtectionTests
{
    [Theory]
    [InlineData("127.0.0.1")]            // this machine
    [InlineData("10.1.2.3")]             // private
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]          // home/office network
    [InlineData("169.254.169.254")]      // cloud metadata service (credentials)
    [InlineData("168.63.129.16")]        // Azure's internal wire server
    [InlineData("100.64.0.1")]           // carrier-grade NAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]            // multicast
    [InlineData("::1")]                  // this machine, IPv6
    [InlineData("::")]
    [InlineData("fe80::1")]              // link-local
    [InlineData("fd12:3456::1")]         // private IPv6
    [InlineData("::ffff:127.0.0.1")]     // IPv4 hidden inside IPv6
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")]      // NAT64 form of 127.0.0.1
    [InlineData("2002:7f00:1::1")]       // 6to4 form of 127.0.0.1
    public void InternalAddresses_AreNotPublic(string address) =>
        Assert.False(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.215.14")]
    [InlineData("172.32.0.1")]           // just outside 172.16-31
    [InlineData("2606:4700::1111")]
    public void InternetAddresses_ArePublic(string address) =>
        Assert.True(PublicAddressPolicy.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://localhost:5003/api/watches")]
    [InlineData("http://127.0.0.1:5003/api/watches")]
    [InlineData("http://2130706433:5003/health")]     // 127.0.0.1 written as one number
    [InlineData("http://0x7f000001:5003/health")]     // ... and in hex
    [InlineData("http://[::1]:5003/health")]
    [InlineData("http://[::ffff:127.0.0.1]:5003/health")]
    [InlineData("http://169.254.169.254/metadata/instance")]
    public async Task Fetcher_RefusesInternalAddresses_BeforeConnecting(string address)
    {
        var fetcher = new WebPageSourceFetcher(new HttpClient(SourceServiceCollectionExtensions.CreateGuardedHandler(PublicAddressPolicy.IsPublic)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync(address));

        Assert.Equal(BlockedAddressException.FriendlyMessage, ex.Message);
    }

    [Fact]
    public async Task PublicPageThatRedirectsToAnInternalAddress_IsCaughtOnTheRedirect()
    {
        // The sneaky version: the first address is allowed, the page then redirects to [::1].
        // Here 127.0.0.1 plays the "public" site and [::1] the internal target.
        await using var site = TinyWebServer.Start(port => $"HTTP/1.1 302 Found\r\nLocation: http://[::1]:{port}/secret\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var fetcher = new WebPageSourceFetcher(new HttpClient(SourceServiceCollectionExtensions.CreateGuardedHandler(a => a.Equals(IPAddress.Loopback))));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync($"http://127.0.0.1:{site.Port}/start"));

        Assert.Equal(BlockedAddressException.FriendlyMessage, ex.Message);
        Assert.Equal(1, site.RequestCount); // the first hop really was fetched; the redirect was refused
    }

    [Fact]
    public async Task AllowedAddress_IsFetchedNormally()
    {
        // The guard must not break ordinary fetching (it makes the connections itself).
        await using var site = TinyWebServer.Start(_ => "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: 15\r\nConnection: close\r\n\r\n<p>v1.0 out</p>");
        var fetcher = new WebPageSourceFetcher(new HttpClient(SourceServiceCollectionExtensions.CreateGuardedHandler(a => a.Equals(IPAddress.Loopback))));

        var snapshot = await fetcher.FetchAsync($"http://127.0.0.1:{site.Port}/");

        Assert.Equal("v1.0 out", snapshot.NormalizedContent);
    }

    [Fact]
    public async Task TheAppsRealRegistration_IsProtected()
    {
        // Proves the guard is actually wired into the fetcher the app and the MCP server use.
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSourceFetchers();
        var fetcher = services.BuildServiceProvider().GetRequiredService<ISourceFetcher>();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.FetchAsync("http://localhost:5003/api/watches"));

        Assert.Equal(BlockedAddressException.FriendlyMessage, ex.Message);
    }

    [Theory]
    [InlineData("http://localhost:5003/api/watches")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://192.168.1.1/admin")]
    public async Task Validator_RejectsInternalAddresses_WhenAWatchIsSaved(string address)
    {
        var problem = await Validator().ValidateAsync(SourceType.WebPage, address);

        Assert.Equal(BlockedAddressException.FriendlyMessage, problem);
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://example.com/file")]
    [InlineData("example.com")]
    public async Task Validator_RejectsAddressesThatAreNotWebAddresses(string address)
    {
        var problem = await Validator().ValidateAsync(SourceType.WebPage, address);

        Assert.StartsWith("The address must be a full web address", problem);
    }

    [Fact]
    public async Task Validator_AcceptsPublicAddresses() =>
        Assert.Null(await Validator().ValidateAsync(SourceType.WebPage, "http://93.184.215.14/releases"));

    [Fact]
    public async Task Validator_AllowsLocalhost_OnlyWhenTheScenarioSettingIsOn()
    {
        var validator = Validator(new() { [SourceServiceCollectionExtensions.AllowPrivateAddressesSetting] = "true" });

        Assert.Null(await validator.ValidateAsync(SourceType.WebPage, "http://localhost:5098/releases"));
    }

    private static SourceAddressValidator Validator(Dictionary<string, string?>? settings = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());

    // A few lines of raw HTTP on 127.0.0.1, so the redirect test needs no internet and works on CI.
    private sealed class TinyWebServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private int requestCount;

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public int RequestCount => requestCount;

        public static TinyWebServer Start(Func<int, string> response)
        {
            var server = new TinyWebServer();
            server.listener.Start();
            _ = server.ServeAsync(response);
            return server;
        }

        private async Task ServeAsync(Func<int, string> response)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = client.GetStream();
                    await stream.ReadAtLeastAsync(new byte[4096], 1, throwOnEndOfStream: false, stop.Token); // the request; we don't care what it says
                    Interlocked.Increment(ref requestCount);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response(Port)), stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
