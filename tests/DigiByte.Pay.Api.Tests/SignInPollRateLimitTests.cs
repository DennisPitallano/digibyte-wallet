using System.Net;
using System.Net.Http.Json;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// The sign-in page polls /v1/pay/auth/poll/{nonce} every second while the merchant approves in the wallet. Those polls
/// used to count against the per-address "auth" limit (10 a minute), so an approved sign-in waited for the minute to
/// reset before the page saw it. Each challenge now has its own poll allowance; creating challenges is still limited.
/// </summary>
public class SignInPollRateLimitTests
{
    private record Challenge(string Nonce, string Uri);

    [Fact]
    public async Task A_page_polling_every_second_for_a_minute_is_never_throttled()
    {
        using var factory = PayFactory.Create();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.40");

        var challenge = await client.GetFromJsonAsync<Challenge>("/v1/pay/auth/challenge");
        for (var i = 0; i < 60; i++)
        {
            var poll = await client.GetAsync($"/v1/pay/auth/poll/{challenge!.Nonce}");
            Assert.Equal(HttpStatusCode.Accepted, poll.StatusCode);
        }
    }

    [Fact]
    public async Task Polling_does_not_use_up_the_sign_in_limit_for_new_challenges()
    {
        using var factory = PayFactory.Create();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.41");

        var first = await client.GetFromJsonAsync<Challenge>("/v1/pay/auth/challenge");
        for (var i = 0; i < 30; i++) await client.GetAsync($"/v1/pay/auth/poll/{first!.Nonce}");

        // Another 9 challenges still fit in the address's 10 a minute; the 11th is refused.
        for (var i = 0; i < 9; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/pay/auth/challenge")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/v1/pay/auth/challenge")).StatusCode);
    }

    [Fact]
    public async Task Each_challenge_has_its_own_poll_allowance()
    {
        using var factory = PayFactory.Create();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.42");

        var a = await client.GetFromJsonAsync<Challenge>("/v1/pay/auth/challenge");
        var b = await client.GetFromJsonAsync<Challenge>("/v1/pay/auth/challenge");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 121; i++) statuses.Add((await client.GetAsync($"/v1/pay/auth/poll/{a!.Nonce}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]); // a runaway poller is still capped...
        Assert.Equal(HttpStatusCode.Accepted, (await client.GetAsync($"/v1/pay/auth/poll/{b!.Nonce}")).StatusCode); // ...without touching others
    }
}
