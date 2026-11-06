using System.Net;
using System.Text.Json;

namespace DigiByte.Pay.Api.Tests;

/// <summary>T022 (DGB-03): merchant registration validates the webhook URL (contracts/webhook-url-policy.md).</summary>
public class WebhookRegistrationTests : IClassFixture<PayFactory>
{
    private readonly PayFactory _factory;
    private readonly HttpClient _client;

    public WebhookRegistrationTests(PayFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string Error(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    [Theory]
    [InlineData("http://example.com/hook", "must use https")]
    [InlineData("http://127.0.0.1/hook", "must use https")]
    [InlineData("https://127.0.0.1/hook", "address not allowed")]
    [InlineData("https://localhost/hook", "address not allowed")]
    [InlineData("https://10.0.0.5/hook", "address not allowed")]
    [InlineData("https://192.168.1.10/hook", "address not allowed")]
    [InlineData("https://169.254.169.254/latest/meta-data", "address not allowed")]
    [InlineData("https://[::1]/hook", "address not allowed")]
    [InlineData("https://example.com:22/hook", "address not allowed")]
    [InlineData("https://example.com:8444/hook", "address not allowed")]
    [InlineData("https://user:pw@example.com/hook", "address not allowed")]
    [InlineData("https://internal.example/hook", "address not allowed")] // resolves to 10.0.0.5 via the fake resolver
    public async Task HostileWebhookUrl_Returns400WithAShortCategoryOnly(string url, string category)
    {
        var response = await _factory.Register(_client, url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(category, Error(body));
        Assert.DoesNotContain("10.0.0.5", body);
        Assert.DoesNotContain("169.254", body);
    }

    [Fact]
    public async Task OverlongWebhookUrl_Returns400()
    {
        var response = await _factory.Register(_client, "https://example.com/" + new string('a', 2100));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://hooks.example.com/digipay")]
    [InlineData("https://hooks.example.com:443/digipay")]
    [InlineData("https://hooks.example.com:8443/digipay")]
    [InlineData("https://hooks.example.com:8080/digipay")]
    [InlineData("https://hooks.example.com:8000/digipay")]
    public async Task PublicHttpsWebhookUrl_IsAccepted_AndStored(string url)
    {
        var response = await _factory.Register(_client, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var store = await _factory.LoadStore(doc.RootElement.GetProperty("storeId").GetString()!);
        Assert.Equal(url, store!.WebhookUrl);
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("webhookSecret").GetString()));
    }

    [Fact]
    public async Task RegistrationWithoutAWebhook_StillWorks()
    {
        var response = await _factory.Register(_client, webhookUrl: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectedRegistration_CreatesNothing()
    {
        var before = await response_count();
        await _factory.Register(_client, "https://10.0.0.5/hook");
        Assert.Equal(before, await response_count());

        async Task<int> response_count()
        {
            using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_factory.Services);
            var db = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DigiByte.Pay.Api.Data.DigiPayDbContext>(scope.ServiceProvider);
            return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Merchants);
        }
    }
}
