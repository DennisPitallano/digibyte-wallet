using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigiByte.Crypto.DigiId;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace DigiByte.Api.Tests;

/// <summary>T016 + T017 (DGB-02): the Digi-ID proxy only contacts the site named in a signature-verified digiid:// URI (contracts/digiid-callback.md).</summary>
public class DigiIdCallbackTests
{
    private static readonly Key SiteKey = new();

    // ── helpers ──

    private static (string Uri, string Address, string Signature) Signed(string uri)
    {
        var request = DigiIdService.ParseUri(uri) ?? throw new InvalidOperationException("test uri did not parse");
        var response = DigiIdService.Sign(request, SiteKey);
        return (response.Uri, response.Address, response.Signature);
    }

    private static object Payload(string uri, string? callbackUrl = null, string? signatureOverride = null)
    {
        var s = Signed(uri);
        return new { uri = s.Uri, address = s.Address, signature = signatureOverride ?? s.Signature, callbackUrl };
    }

    private sealed class FakeSite : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("welcome") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return Respond(request);
        }
    }

    private sealed class Factory(FakeSite site) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
                services.AddHttpClient("DigiIdProxy").ConfigurePrimaryHttpMessageHandler(() => site));
        }
    }

    private static async Task<(HttpStatusCode Status, JsonElement Json)> Post(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/digiid/callback", payload);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static void AssertProxyRefusal(JsonElement json, string reason)
    {
        Assert.Equal("proxy", json.GetProperty("source").GetString());
        Assert.Equal(reason, json.GetProperty("reason").GetString());
    }

    private static (FakeSite Site, HttpClient Client) NewHost()
    {
        var site = new FakeSite();
        return (site, new Factory(site).CreateClient());
    }

    // ── derivation and the happy path ──

    [Fact]
    public async Task Target_IsDerivedFromTheSignedUri_AndTheSiteReplyIsRelayed()
    {
        var (site, client) = NewHost();
        site.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("welcome back") };

        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=nonce123"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(200, json.GetProperty("status").GetInt32());
        Assert.Equal("welcome back", json.GetProperty("body").GetString());
        var sent = Assert.Single(site.Requests);
        Assert.Equal("https://example.com/auth?x=nonce123", sent.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, sent.Method);
        var body = JsonDocument.Parse(site.Bodies[0]).RootElement;
        Assert.Equal("digiid://example.com/auth?x=nonce123", body.GetProperty("uri").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("signature").GetString()));
    }

    [Fact]
    public async Task MatchingCallbackUrl_IsAccepted()
    {
        var (site, client) = NewHost();
        var (status, _) = await Post(client, Payload("digiid://example.com/auth?x=n1", callbackUrl: "https://example.com/auth?x=n1"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Single(site.Requests);
    }

    [Fact]
    public async Task UpstreamRejection_IsRelayedWithItsOwnStatus_AndNoProxySource()
    {
        var (site, client) = NewHost();
        site.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("nope") };

        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n2"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(401, json.GetProperty("status").GetInt32());
        Assert.False(json.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task RelayedBody_IsTruncatedPlainText()
    {
        var (site, client) = NewHost();
        site.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 10_000)) };

        var (_, json) = await Post(client, Payload("digiid://example.com/auth?x=n3"));

        Assert.True(json.GetProperty("body").GetString()!.Length <= 2048);
    }

    // ── refusals: nothing is sent ──

    [Fact]
    public async Task UnsecureUri_IsRefused_MustUseHttps()
    {
        var (site, client) = NewHost();
        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n4&u=1"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "must use https");
        Assert.Empty(site.Requests);
    }

    [Fact]
    public async Task CallbackUrlThatDiffersFromTheSignedUri_IsRefused()
    {
        var (site, client) = NewHost();
        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n5", callbackUrl: "https://evil.example/steal"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "invalid request");
        Assert.Empty(site.Requests);
    }

    [Fact]
    public async Task BadSignature_IsRefused()
    {
        var (site, client) = NewHost();
        var other = DigiIdService.Sign(DigiIdService.ParseUri("digiid://other.example/x?x=zzz")!, SiteKey).Signature; // valid shape, wrong message
        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n6", signatureOverride: other));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "bad signature");
        Assert.Empty(site.Requests);
    }

    [Fact]
    public async Task SignatureForADifferentAddress_IsRefused()
    {
        var (site, client) = NewHost();
        var s = Signed("digiid://example.com/auth?x=n7");
        var (status, json) = await Post(client, new { uri = s.Uri, address = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main).ToString(), signature = s.Signature });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "bad signature");
        Assert.Empty(site.Requests);
    }

    [Theory]
    [InlineData("digiid://127.0.0.1/auth?x=a")]
    [InlineData("digiid://localhost/auth?x=a")]
    [InlineData("digiid://10.0.0.5/auth?x=a")]
    [InlineData("digiid://192.168.1.1/auth?x=a")]
    [InlineData("digiid://169.254.169.254/latest/meta-data?x=a")]
    [InlineData("digiid://[::1]/auth?x=a")]
    [InlineData("digiid://[::ffff:10.0.0.1]/auth?x=a")]
    public async Task BlockedDestination_IsRefused_AndNothingIsSent(string uri)
    {
        var (site, client) = NewHost();
        var (status, json) = await Post(client, Payload(uri));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "address not allowed");
        Assert.Empty(site.Requests);
    }

    [Theory]
    [InlineData("digiid://example.com:8443/auth?x=a")]
    [InlineData("digiid://example.com:22/auth?x=a")]
    [InlineData("digiid://example.com:80/auth?x=a")]
    public async Task NonStandardPort_IsRefused(string uri)
    {
        var (site, client) = NewHost();
        var (status, json) = await Post(client, Payload(uri));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "address not allowed");
        Assert.Empty(site.Requests);
    }

    [Fact]
    public async Task HostnameThatResolvesToAPrivateAddress_IsRefusedAtConnectTime()
    {
        var (site, client) = NewHost();
        site.Respond = _ => throw new HttpRequestException("blocked", new DigiByte.Server.Shared.OutboundBlockedException("evil.example resolved to 10.0.0.5"));

        var (status, json) = await Post(client, Payload("digiid://evil.example/auth?x=n8"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "address not allowed");
        Assert.DoesNotContain("10.0.0.5", json.GetRawText());
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Redirect_IsNotFollowed_AndIsReported(int code)
    {
        var (site, client) = NewHost();
        site.Respond = _ =>
        {
            var r = new HttpResponseMessage((HttpStatusCode)code);
            r.Headers.Location = new Uri("https://169.254.169.254/latest/meta-data");
            return r;
        };

        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n9"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        AssertProxyRefusal(json, "redirects not allowed");
        Assert.Single(site.Requests); // the redirect target was never contacted
    }

    // ── upstream failures: 502, never an unhandled 500 ──

    [Theory]
    [InlineData("timeout")]
    [InlineData("dns")]
    [InlineData("reset")]
    public async Task UpstreamFailure_Returns502SiteUnreachable(string kind)
    {
        var (site, client) = NewHost();
        site.Respond = _ => kind switch
        {
            "timeout" => throw new TaskCanceledException("timed out", new TimeoutException()),
            "dns" => throw new HttpRequestException("Name or service not known", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound)),
            _ => throw new HttpRequestException("connection reset", new IOException("reset")),
        };

        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n10"));

        Assert.Equal(HttpStatusCode.BadGateway, status);
        AssertProxyRefusal(json, "site unreachable");
        Assert.DoesNotContain("Name or service", json.GetRawText());
    }

    [Fact]
    public async Task OversizedBody_Returns502SiteUnreachable()
    {
        var (site, client) = NewHost();
        site.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('a', 70_000), Encoding.UTF8) };

        var (status, json) = await Post(client, Payload("digiid://example.com/auth?x=n11"));

        Assert.Equal(HttpStatusCode.BadGateway, status);
        AssertProxyRefusal(json, "site unreachable");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"uri\":\"https://example.com/\",\"address\":\"D\",\"signature\":\"s\"}")]
    [InlineData("{\"uri\":\"digiid://example.com/auth\",\"address\":\"D\",\"signature\":\"s\"}")]
    [InlineData("{\"uri\":5,\"address\":\"D\",\"signature\":\"s\"}")]
    public async Task MalformedRequest_Returns400InvalidRequest(string body)
    {
        var (site, client) = NewHost();
        var response = await client.PostAsync("/api/digiid/callback", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertProxyRefusal(json.RootElement, "invalid request");
        Assert.Empty(site.Requests);
    }

    // ── T017: rate limit (SC-007) ──

    [Fact]
    public async Task EleventhCallInAMinute_Returns429_AndASpoofedForwardedForDoesNotResetTheCount()
    {
        var (_, client) = NewHost();

        for (var i = 1; i <= 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/digiid/callback") { Content = JsonContent.Create(new { }) };
            request.Headers.Add("X-Forwarded-For", $"10.9.8.{i}, 203.0.113.50"); // leftmost is whatever the caller claims
            var ok = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, ok.StatusCode);
        }

        using var eleventh = new HttpRequestMessage(HttpMethod.Post, "/api/digiid/callback") { Content = JsonContent.Create(new { }) };
        eleventh.Headers.Add("X-Forwarded-For", "198.51.100.77, 203.0.113.50");
        var limited = await client.SendAsync(eleventh);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var json = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        AssertProxyRefusal(json.RootElement, "rate limited");
    }

    [Fact]
    public async Task DifferentClientAddresses_AreCountedSeparately()
    {
        var (_, client) = NewHost();
        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/digiid/callback") { Content = JsonContent.Create(new { }) };
            request.Headers.Add("X-Forwarded-For", "203.0.113.50");
            await client.SendAsync(request);
        }

        using var other = new HttpRequestMessage(HttpMethod.Post, "/api/digiid/callback") { Content = JsonContent.Create(new { }) };
        other.Headers.Add("X-Forwarded-For", "203.0.113.51");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(other)).StatusCode);
    }
}
