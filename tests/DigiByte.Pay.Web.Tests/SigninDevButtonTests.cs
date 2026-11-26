using Microsoft.Extensions.Configuration;
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
    private readonly Microsoft.AspNetCore.Http.HttpContextAccessor accessor = new();

    private void Use(string environment, string? trustedClientKey = null)
    {
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DigiPay:TrustedClientKey"] = trustedClientKey }).Build());
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor>(accessor);
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

    [Fact]
    public void The_challenge_can_be_approved_in_the_digibyte_wallet_in_the_browser()
    {
        Use("Production");
        var cut = Render<Signin>();

        var link = cut.WaitForElement("a.dp-signin-wallet");
        // &return= is this sign-in page, so the wallet can offer "Return to localhost" after approving.
        Assert.Equal("https://dgbwallet.app/identity?uri=" + Uri.EscapeDataString("digiid://localhost/v1/pay/auth/callback?x=n1&u=1")
            + "&return=" + Uri.EscapeDataString("http://localhost/"),
            link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target")); // this tab keeps polling and signs in
        Assert.Contains("Sign in with DigiByte Wallet", link.TextContent);
    }

    [Fact]
    public void The_return_address_is_the_full_sign_in_address_with_its_own_return()
    {
        Use("Production");
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/merchant/signin?return=%2Fdashboard%2Fsessions");
        var cut = Render<Signin>();

        var href = cut.WaitForElement("a.dp-signin-wallet").GetAttribute("href")!;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(href).Query);
        Assert.Equal("digiid://localhost/v1/pay/auth/callback?x=n1&u=1", query["uri"]);
        Assert.Equal("http://localhost/merchant/signin?return=%2Fdashboard%2Fsessions", query["return"]);
    }

    // Pay.Web asks the API from its own server, so it names the visitor (with the shared key) for the sign-in limit.
    [Fact]
    public void With_a_trusted_client_key_sign_in_requests_name_the_visitor()
    {
        Use("Production", trustedClientKey: "k1");
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 203.0.113.9"; // the proxy appended the real visitor
        accessor.HttpContext = http;
        var cut = Render<Signin>();

        cut.WaitForAssertion(() => Assert.Contains("/v1/pay/auth/challenge", api.Requests));
        var sent = api.Headers["/v1/pay/auth/challenge"];
        Assert.Equal("203.0.113.9", sent["X-DigiPay-Client-Address"]);
        Assert.Equal("k1", sent["X-DigiPay-Client-Key"]);
    }

    [Fact]
    public void Without_a_key_the_visitor_is_not_sent()
    {
        Use("Production");
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        accessor.HttpContext = http;
        var cut = Render<Signin>();

        cut.WaitForAssertion(() => Assert.Contains("/v1/pay/auth/challenge", api.Requests));
        Assert.Empty(api.Headers["/v1/pay/auth/challenge"]);
    }

    [Fact]
    public void A_refused_sign_in_says_to_wait_rather_than_blaming_the_connection()
    {
        api.ChallengeLimited = true;
        Use("Production");
        var cut = Render<Signin>();

        cut.WaitForAssertion(() => Assert.Contains("Too many sign-in attempts. Wait a minute and try again.", cut.Markup));
        Assert.DoesNotContain("Check your connection", cut.Markup);
    }

    [Theory]
    [InlineData("198.51.100.1, 203.0.113.9", "203.0.113.9")] // rightmost entry: what the proxy appended
    [InlineData("not-an-ip", "192.0.2.5")]                    // unusable header: the socket address
    [InlineData(null, "192.0.2.5")]
    public void The_visitor_address_is_read_like_the_api_reads_it(string? forwarded, string expected)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.5");
        if (forwarded is not null) http.Request.Headers["X-Forwarded-For"] = forwarded;
        Assert.Equal(expected, Signin.VisitorAddressOf(http));
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
        public bool ChallengeLimited { get; set; }
        public List<string> Requests { get; } = new();
        public Dictionary<string, Dictionary<string, string>> Headers { get; } = new();

        public HttpClient CreateClient(string name) => new(new Handler(this)) { BaseAddress = new Uri("http://pay-api.test") };

        private sealed class Handler(FakeApi api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var path = request.RequestUri!.AbsolutePath;
                lock (api.Requests)
                {
                    api.Requests.Add(path);
                    api.Headers[path] = request.Headers.Where(h => h.Key.StartsWith("X-DigiPay-")).ToDictionary(h => h.Key, h => string.Join(",", h.Value));
                }
                var (status, json) = path switch
                {
                    "/v1/pay/auth/challenge" when api.ChallengeLimited => ((HttpStatusCode)429, """{}"""),
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
