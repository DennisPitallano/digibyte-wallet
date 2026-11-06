using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DigiByte.Server.Shared.Tests;

/// <summary>T007: connect-time enforcement, no redirects, capped reads, timeout (contracts/webhook-url-policy.md).</summary>
public class OutboundHandlerTests
{
    private sealed class FakeResolver(Func<string, IPAddress[]> map) : IOutboundResolver
    {
        public int Calls;
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(map(host));
        }
    }

    /// <summary>Minimal HTTP/1.1 server on loopback that answers every request with a canned response.</summary>
    private sealed class TinyServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int Requests;

        public TinyServer(Func<byte[]> response)
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                    catch { return; }
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                var stream = client.GetStream();
                                var buf = new byte[4096];
                                var read = await stream.ReadAsync(buf, _cts.Token);
                                if (read == 0) return;
                                Interlocked.Increment(ref Requests);
                                await stream.WriteAsync(response(), _cts.Token);
                                await stream.FlushAsync(_cts.Token);
                                await Task.Delay(200, _cts.Token);
                            }
                            catch { }
                        }
                    });
                }
            });
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private static byte[] Http(string status, string? extraHeaders, string body) =>
        Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n{extraHeaders}\r\n{body}");

    private static HttpClient Client(OutboundUrlPolicy policy, IOutboundResolver resolver, int timeoutSeconds = 5) =>
        new(GuardedConnector.CreateHandler(policy, resolver)) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

    // ── DNS rebinding / resolved-address enforcement ──

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.1")]
    public async Task HostnameResolvingToBlockedAddress_IsRefusedAtConnectTime(string resolved)
    {
        var resolver = new FakeResolver(_ => [IPAddress.Parse(resolved)]);
        using var http = Client(new OutboundUrlPolicy { AllowedPorts = [443] }, resolver);

        // The URL passes validation (it is just a public-looking hostname) - the address is only known at connect time.
        Assert.Null(OutboundUrlGuard.Validate("https://rebind.example/hook", OutboundUrlPolicy.Webhook()));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://rebind.example/hook"));
        Assert.IsType<OutboundBlockedException>(ex.InnerException);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task HostnameResolvingToPublicAndPrivate_IsRefused()
    {
        var resolver = new FakeResolver(_ => [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5")]);
        using var http = Client(OutboundUrlPolicy.Webhook(), resolver);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://mixed.example/hook"));
        Assert.IsType<OutboundBlockedException>(ex.InnerException);
    }

    [Fact]
    public async Task ResolutionAtConnectTimeWins_WhenTheHostChangesAfterValidation()
    {
        var calls = 0;
        // First lookup (validation time) is public; the second (connect time) is private - the connect-time answer is the one enforced.
        var resolver = new FakeResolver(_ => Interlocked.Increment(ref calls) == 1 ? [IPAddress.Parse("8.8.8.8")] : [IPAddress.Parse("10.0.0.5")]);

        Assert.Null(await OutboundUrlGuard.ValidateAsync("https://flip.example/hook", OutboundUrlPolicy.Webhook(), resolver));

        using var http = Client(OutboundUrlPolicy.Webhook(), resolver);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://flip.example/hook"));
        Assert.IsType<OutboundBlockedException>(ex.InnerException);
    }

    [Fact]
    public async Task ValidateAsync_RefusesAHostnameThatResolvesToAPrivateAddress()
    {
        var resolver = new FakeResolver(_ => [IPAddress.Parse("10.1.1.1")]);
        Assert.Equal(OutboundReason.AddressNotAllowed, await OutboundUrlGuard.ValidateAsync("https://corp.example/hook", OutboundUrlPolicy.Webhook(), resolver));
    }

    [Fact]
    public async Task ValidateAsync_LetsAnUnresolvableNamePass_ConnectTimeStillGuardsIt()
    {
        var resolver = new FakeResolver(_ => throw new SocketException((int)SocketError.HostNotFound));
        Assert.Null(await OutboundUrlGuard.ValidateAsync("https://nowhere.example/hook", OutboundUrlPolicy.Webhook(), resolver));
    }

    [Fact]
    public async Task AllowLoopback_ConnectsToLoopback_ButNotWithoutTheFlag()
    {
        await using var server = new TinyServer(() => Http("200 OK", null, "hello"));
        var resolver = new FakeResolver(_ => [IPAddress.Loopback]);
        var url = $"http://dev.example:{server.Port}/hook";

        using (var dev = Client(new OutboundUrlPolicy { AllowLoopback = true }, resolver))
        {
            var response = await dev.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        }

        using var prod = Client(new OutboundUrlPolicy(), resolver);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => prod.GetAsync(url));
        Assert.IsType<OutboundBlockedException>(ex.InnerException);
        Assert.Equal(1, server.Requests); // the refused attempt never reached the server
    }

    // ── redirects ──

    [Fact]
    public async Task Redirect_IsReturnedAsIs_AndNotFollowed()
    {
        await using var target = new TinyServer(() => Http("200 OK", null, "secret"));
        await using var server = new TinyServer(() => Http("302 Found", $"Location: http://127.0.0.1:{target.Port}/internal\r\n", ""));
        var resolver = new FakeResolver(_ => [IPAddress.Loopback]);
        using var http = Client(new OutboundUrlPolicy { AllowLoopback = true }, resolver);

        var response = await http.GetAsync($"http://r.example:{server.Port}/hook");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, server.Requests);
        Assert.Equal(0, target.Requests);
    }

    // ── response cap ──

    [Fact]
    public async Task ReadCapped_StopsAtMaxResponseBytes()
    {
        var big = new string('a', 200_000);
        await using var server = new TinyServer(() => Http("200 OK", null, big));
        var resolver = new FakeResolver(_ => [IPAddress.Loopback]);
        var policy = new OutboundUrlPolicy { AllowLoopback = true };
        using var http = Client(policy, resolver);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://big.example:{server.Port}/");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var (text, truncated) = await response.Content.ReadCappedAsync(policy.MaxResponseBytes);

        Assert.True(truncated);
        Assert.Equal(65536, text.Length);
    }

    [Fact]
    public async Task ReadCapped_ReturnsTheWholeBodyWhenItFits()
    {
        await using var server = new TinyServer(() => Http("200 OK", null, "small"));
        var resolver = new FakeResolver(_ => [IPAddress.Loopback]);
        using var http = Client(new OutboundUrlPolicy { AllowLoopback = true }, resolver);

        using var response = await http.GetAsync($"http://small.example:{server.Port}/", HttpCompletionOption.ResponseHeadersRead);
        var (text, truncated) = await response.Content.ReadCappedAsync(65536);

        Assert.False(truncated);
        Assert.Equal("small", text);
    }

    // ── timeout ──

    [Fact]
    public async Task RequestTimeout_Applies()
    {
        // A listener that accepts but never answers.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var resolver = new FakeResolver(_ => [IPAddress.Loopback]);
            using var http = Client(new OutboundUrlPolicy { AllowLoopback = true }, resolver, timeoutSeconds: 1);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.GetAsync($"http://slow.example:{port}/"));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void DefaultTimeoutAndCap_MatchThePolicy()
    {
        var policy = OutboundUrlPolicy.Webhook();
        Assert.Equal(10, policy.TimeoutSeconds);
        Assert.Equal(65536, policy.MaxResponseBytes);
    }
}
