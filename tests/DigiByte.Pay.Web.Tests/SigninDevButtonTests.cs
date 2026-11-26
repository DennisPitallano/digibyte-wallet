using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Pages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace DigiByte.Pay.Web.Tests;

// The "Sign in as dev merchant" button: only in Development, only when the API advertises it,
// and it signs in through the same token-storing path as Digi-ID.
public class SigninDevButtonTests : BunitContext
{
    private readonly FakeApi api = new();

    private void Use(string environment)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddSingleton<IWebHostEnvironment>(new FakeEnvironment(environment));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Is_hidden_outside_development_even_when_the_api_offers_it(string environment)
    {
        Use(environment);
        var cut = Render<Signin>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qr-frame")));
        Assert.Empty(cut.FindAll("[data-testid=dev-signin]"));
        Assert.DoesNotContain("dev merchant", cut.Markup);
        Assert.DoesNotContain("/v1/pay/dev/status", api.Requests); // never even asked
    }

    [Fact]
    public void Is_hidden_in_development_when_the_api_has_it_switched_off()
    {
        api.DevEnabled = false;
        Use("Development");
        var cut = Render<Signin>();

        cut.WaitForAssertion(() => Assert.Contains("/v1/pay/dev/status", api.Requests));
        Assert.Empty(cut.FindAll("[data-testid=dev-signin]"));
    }

    [Fact]
    public void In_development_it_signs_in_and_stores_the_token_like_digi_id()
    {
        Use("Development");
        var cut = Render<Signin>();

        var button = cut.WaitForElement("[data-testid=dev-signin]");
        Assert.Contains("Sign in as dev merchant (local testing)", button.TextContent);
        button.Click();

        cut.WaitForAssertion(() => Assert.Contains("Signed in", cut.Markup));
        Assert.Contains("Dev merchant", cut.Markup);
        var stored = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem").Select(i => (string)i.Arguments[0]! + "=" + (string)i.Arguments[1]!).ToList();
        Assert.Contains("digipay-token=dps_dev_secret", stored);
        Assert.Contains("digipay-merchant-id=mer_devmerchant000001", stored);
    }

    private sealed class FakeEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DigiByte.Pay.Web";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeApi : IHttpClientFactory
    {
        public bool DevEnabled { get; set; } = true;
        public List<string> Requests { get; } = new();

        public HttpClient CreateClient(string name) => new(new Handler(this)) { BaseAddress = new Uri("http://pay-api.test") };

        private sealed class Handler(FakeApi api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                lock (api.Requests) api.Requests.Add(path);
                var (status, json) = path switch
                {
                    "/v1/pay/auth/challenge" => (HttpStatusCode.OK, """{"nonce":"n1","uri":"digiid://localhost/v1/pay/auth/callback?x=n1&u=1","expiresAt":"2030-01-01T00:00:00Z"}"""),
                    "/v1/pay/dev/status" when api.DevEnabled => (HttpStatusCode.OK, """{"enabled":true}"""),
                    "/v1/pay/dev/signin" when api.DevEnabled => (HttpStatusCode.OK, """{"status":"signed","merchantId":"mer_devmerchant000001","displayName":"Dev merchant","token":"dps_dev_secret"}"""),
                    _ when path.StartsWith("/v1/pay/auth/poll/") => (HttpStatusCode.Accepted, """{"status":"pending"}"""),
                    _ => (HttpStatusCode.NotFound, """{"error":"not found"}"""),
                };
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
        }
    }
}
