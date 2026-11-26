using System.Net;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// Pay.Web calls the API from its own server, so without help every merchant signing in through it shares one
/// "auth" allowance (10 a minute). With DigiPay:TrustedClientKey set, Pay.Web names the visitor and each visitor is
/// counted on their own; without the right key the header is ignored.
/// </summary>
public class TrustedClientRateLimitTests
{
    private const string Key = "test-trusted-key";

    private static PayFactory Factory(string? key = Key) =>
        PayFactory.Create(settings: new Dictionary<string, string?> { ["DigiPay:TrustedClientKey"] = key ?? "" });

    private static async Task<HttpStatusCode> Challenge(HttpClient client, string? visitor, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/pay/auth/challenge");
        request.Headers.Add("X-Forwarded-For", "198.51.100.7"); // the Pay.Web server, as the API sees it
        if (visitor is not null) request.Headers.Add("X-DigiPay-Client-Address", visitor);
        if (key is not null) request.Headers.Add("X-DigiPay-Client-Key", key);
        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Visitors_named_by_pay_web_each_get_their_own_allowance()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        for (var visitor = 1; visitor <= 15; visitor++)
            Assert.Equal(HttpStatusCode.OK, await Challenge(client, $"203.0.113.{visitor}", Key));
    }

    [Fact]
    public async Task One_visitor_is_still_limited()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++) statuses.Add(await Challenge(client, "203.0.113.50", Key));
        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task A_wrong_key_cannot_choose_the_bucket()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var visitor = 1; visitor <= 11; visitor++) statuses.Add(await Challenge(client, $"203.0.113.{visitor}", "guess"));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]); // all counted against the caller's own address
    }

    [Fact]
    public async Task Without_a_configured_key_the_header_is_ignored()
    {
        using var factory = Factory(key: null);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var visitor = 1; visitor <= 11; visitor++) statuses.Add(await Challenge(client, $"203.0.113.{visitor}", ""));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task A_visitor_address_that_is_not_an_ip_is_ignored()
    {
        using var factory = Factory();
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 1; i <= 11; i++) statuses.Add(await Challenge(client, $"not-an-ip-{i}", Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
