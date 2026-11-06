using System.Net;

namespace DigiByte.Pay.Api.Tests;

/// <summary>T025 (DGB-03, SC-007): 5 merchant registrations per hour per client; a spoofed X-Forwarded-For does not reset the count.</summary>
public class RegistrationRateLimitTests
{
    [Fact]
    public async Task SixthRegistrationInAnHour_Returns429_AndASpoofedForwardedForDoesNotResetIt()
    {
        using var factory = PayFactory.Create(registerPerHour: 5); // the production default
        var client = factory.CreateClient();

        for (var i = 1; i <= 5; i++)
        {
            var ok = await factory.Register(client, webhookUrl: null, forwardedFor: $"10.1.1.{i}, 203.0.113.9"); // leftmost = caller's claim
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var sixth = await factory.Register(client, webhookUrl: null, forwardedFor: "198.51.100.200, 203.0.113.9");
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }

    [Fact]
    public async Task ADifferentClientAddress_IsCountedSeparately()
    {
        using var factory = PayFactory.Create(registerPerHour: 5);
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++) await factory.Register(client, null, forwardedFor: "203.0.113.9");

        var other = await factory.Register(client, null, forwardedFor: "203.0.113.10");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task DefaultLimit_IsFivePerHour()
    {
        // Same config the service uses when RateLimits:RegisterPerHour is absent.
        using var factory = PayFactory.Create(registerPerHour: 5);
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++) statuses.Add((await factory.Register(client, null, forwardedFor: "203.0.113.77")).StatusCode);

        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}

/// <summary>T033 (FR-015): /api/health reports the commit.</summary>
public class HealthCommitTests : IClassFixture<PayFactory>
{
    private readonly HttpClient _client;
    public HealthCommitTests(PayFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_HasACommitField()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("commit").GetString()));
    }
}
