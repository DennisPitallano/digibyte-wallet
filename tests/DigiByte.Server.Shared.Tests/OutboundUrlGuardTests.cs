using System.Net;

namespace DigiByte.Server.Shared.Tests;

/// <summary>T006: the destination rules for the Digi-ID callback and DigiPay webhooks (specs/008-security-hardening, contracts/webhook-url-policy.md).</summary>
public class OutboundUrlGuardTests
{
    private static readonly OutboundUrlPolicy Webhook = OutboundUrlPolicy.Webhook();
    private static readonly OutboundUrlPolicy DigiId = OutboundUrlPolicy.DigiId();

    // ── URL shape ──

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://example.com:443/hook")]
    [InlineData("https://example.com:8443/hook")]
    [InlineData("https://example.com:8080/hook")]
    [InlineData("https://example.com:8000/hook")]
    [InlineData("https://hooks.my-shop.example/digipay?x=1")]
    public void Webhook_AcceptsPublicHttpsOnAllowedPorts(string url) =>
        Assert.Null(OutboundUrlGuard.Validate(url, Webhook));

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("HTTP://example.com/hook")]
    [InlineData("ftp://example.com/hook")]
    public void NonHttps_IsRefused(string url) =>
        Assert.Equal(OutboundReason.MustUseHttps, OutboundUrlGuard.Validate(url, Webhook));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void NotAnAbsoluteUrl_IsRefused(string url) =>
        Assert.Equal(OutboundReason.InvalidUrl, OutboundUrlGuard.Validate(url, Webhook));

    [Fact]
    public void NullUrl_IsRefused() =>
        Assert.Equal(OutboundReason.InvalidUrl, OutboundUrlGuard.Validate(null, Webhook));

    [Fact]
    public void UrlLongerThan2048_IsRefused()
    {
        var url = "https://example.com/" + new string('a', 2040);
        Assert.True(url.Length > 2048);
        Assert.Equal(OutboundReason.InvalidUrl, OutboundUrlGuard.Validate(url, Webhook));
    }

    [Fact]
    public void UrlOfExactly2048_IsAccepted()
    {
        var url = "https://example.com/" + new string('a', 2048 - "https://example.com/".Length);
        Assert.Equal(2048, url.Length);
        Assert.Null(OutboundUrlGuard.Validate(url, Webhook));
    }

    [Theory]
    [InlineData("https://user@example.com/")]
    [InlineData("https://user:pw@example.com/hook")]
    public void UserInfo_IsRefused(string url) =>
        Assert.Equal(OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate(url, Webhook));

    // ── ports ──

    [Theory]
    [InlineData("https://example.com:22/")]
    [InlineData("https://example.com:80/")]
    [InlineData("https://example.com:3306/")]
    [InlineData("https://example.com:8444/")]
    [InlineData("https://example.com:65535/")]
    public void Webhook_RefusesPortsOutsideTheAllowList(string url) =>
        Assert.Equal(OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate(url, Webhook));

    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("https://example.com:443/", true)]
    [InlineData("https://example.com:8443/", false)]
    [InlineData("https://example.com:8080/", false)]
    [InlineData("https://example.com:8000/", false)]
    public void DigiId_AllowsPort443Only(string url, bool allowed) =>
        Assert.Equal(allowed ? null : OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate(url, DigiId));

    // ── blocked address ranges (IP literals in the URL) ──

    public static TheoryData<string> BlockedLiterals => new()
    {
        "127.0.0.1", "127.255.255.254", "[::1]",
        "10.0.0.1", "10.255.255.255", "172.16.0.1", "172.31.255.255", "192.168.0.1", "192.168.255.255",
        "169.254.169.254", "169.254.0.1", "[fe80::1]",
        "100.64.0.1", "100.127.255.255",
        "0.0.0.0", "0.1.2.3", "[::]",
        "224.0.0.1", "239.255.255.255", "[ff02::1]",
        "240.0.0.1", "255.255.255.255",
        "[fc00::1]", "[fd12:3456::1]",
        "[::ffff:10.0.0.1]", "[::ffff:127.0.0.1]", "[::ffff:169.254.169.254]", "[::ffff:192.168.1.1]",
    };

    [Theory]
    [MemberData(nameof(BlockedLiterals))]
    public void BlockedIpLiteralInUrl_IsRefused(string host) =>
        Assert.Equal(OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate($"https://{host}/hook", Webhook));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]   // just below 172.16/12
    [InlineData("172.32.0.1")]       // just above 172.16/12
    [InlineData("100.63.255.255")]   // just below CGNAT
    [InlineData("100.128.0.1")]      // just above CGNAT
    [InlineData("[2606:4700:4700::1111]")]
    [InlineData("[::ffff:8.8.8.8]")] // IPv4-mapped public address
    public void PublicIpLiteral_IsAccepted(string host) =>
        Assert.Null(OutboundUrlGuard.Validate($"https://{host}/hook", Webhook));

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("app.localhost")]
    [InlineData("localhost.")]
    public void LocalhostNames_AreRefused(string host) =>
        Assert.Equal(OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate($"https://{host}/hook", Webhook));

    // ── IsBlockedAddress (what the connect-time check uses) ──

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.5")]
    [InlineData("192.168.50.5")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::abcd")]
    [InlineData("fc00::1")]
    [InlineData("100.64.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("224.1.1.1")]
    [InlineData("ff05::1")]
    [InlineData("240.0.0.9")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:127.0.0.1")]
    public void ResolvedBlockedAddress_IsBlocked(string ip) =>
        Assert.True(OutboundUrlGuard.IsBlockedAddress(IPAddress.Parse(ip), allowLoopback: false));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void ResolvedPublicAddress_IsNotBlocked(string ip) =>
        Assert.False(OutboundUrlGuard.IsBlockedAddress(IPAddress.Parse(ip), allowLoopback: false));

    // ── development loopback flag ──

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.9.9.9")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void AllowLoopback_PermitsLoopback(string ip) =>
        Assert.False(OutboundUrlGuard.IsBlockedAddress(IPAddress.Parse(ip), allowLoopback: true));

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    public void AllowLoopback_DoesNotPermitAnythingElse(string ip) =>
        Assert.True(OutboundUrlGuard.IsBlockedAddress(IPAddress.Parse(ip), allowLoopback: true));

    [Fact]
    public void AllowLoopback_PermitsLocalhostAndLoopbackLiteralInUrl_ButStillNeedsAnAllowedPort()
    {
        var dev = OutboundUrlPolicy.Webhook(allowLoopback: true);
        Assert.Null(OutboundUrlGuard.Validate("https://localhost:8443/hook", dev));
        Assert.Null(OutboundUrlGuard.Validate("https://127.0.0.1/hook", dev));
        Assert.Equal(OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate("https://127.0.0.1:5000/hook", dev));
        Assert.Equal(OutboundReason.AddressNotAllowed, OutboundUrlGuard.Validate("https://10.0.0.1/hook", dev));
    }

    [Fact]
    public void DefaultPolicies_AreStrict()
    {
        Assert.False(Webhook.AllowLoopback);
        Assert.False(DigiId.AllowLoopback);
        Assert.Equal(new[] { 443, 8443, 8080, 8000 }, Webhook.AllowedPorts);
        Assert.Equal(new[] { 443 }, DigiId.AllowedPorts);
        Assert.Equal(10, Webhook.TimeoutSeconds);
        Assert.Equal(65536, Webhook.MaxResponseBytes);
    }
}
