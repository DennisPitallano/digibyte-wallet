using System.Net;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// FR-010 / SC-007: shopper pages never show technical error text.
public class CheckoutErrorTests : BunitContext
{
    private const string Secret = "Npgsql.PostgresException: 28P01 password authentication failed for user digipay";

    private void UseApi(HttpMessageHandler handler)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton(new PayApiUrl("http://pay-api.test"));
        Services.AddSingleton<IHttpClientFactory>(new StubFactory(handler));
        Services.AddScoped<ToastService>();
        Services.AddScoped<LiveAnnouncer>();
        AddBunitPersistentComponentState();
    }

    [Fact]
    public void An_unreachable_server_shows_a_plain_message_and_retry()
    {
        UseApi(new ThrowingHandler(new HttpRequestException(Secret)));
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_abc"));

        cut.WaitForAssertion(() => Assert.Contains("This payment page could not be loaded", cut.Markup));
        Assert.DoesNotContain(Secret, cut.Markup);
        Assert.DoesNotContain("Exception", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Try again"));
    }

    [Fact]
    public void A_wrong_link_shows_the_same_plain_message()
    {
        UseApi(new StatusHandler(HttpStatusCode.NotFound));
        var cut = Render<Checkout>(p => p.Add(x => x.SessionId, "ses_does_not_exist"));

        cut.WaitForAssertion(() => Assert.Contains("This payment page could not be loaded", cut.Markup));
        Assert.DoesNotContain("404", cut.Markup);
        Assert.DoesNotContain("Response status code", cut.Markup);
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw ex;
    }

    private sealed class StatusHandler(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{\"error\":\"not found\"}") });
    }
}
