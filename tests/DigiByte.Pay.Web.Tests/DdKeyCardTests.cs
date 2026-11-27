using System.Net;
using System.Text;
using DigiByte.Pay.Web.Components.Shared;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

/// <summary>specs/035 T023: the Dashboard's DigiDollar key card (paste, check the first address, save, replace, remove).</summary>
public class DdKeyCardTests : BunitContext
{
    private const string FirstAddress = "DDfirst111111111111111111111111111111111111";
    private readonly FakeApi api = new();
    private int changed;

    public DdKeyCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
    }

    private IRenderedComponent<DdKeyCard> RenderCard(DdKeyCard.DdKeyView? key = null) =>
        Render<DdKeyCard>(p => p.Add(x => x.Token, "dps_test").Add(x => x.StoreId, "sto_1").Add(x => x.Network, "mainnet")
            .Add(x => x.DdKey, key).Add(x => x.OnChanged, () => changed++));

    private static DdKeyCard.DdKeyView Saved(int unpaidRun = 0) =>
        new(true, "mainnet", "a1b2c3d4", FirstAddress, DateTime.UtcNow, unpaidRun, unpaidRun > 500);

    [Fact]
    public void Paste_check_shows_the_first_address_and_saving_sends_it_back()
    {
        var cut = RenderCard();

        cut.Find("#dd-key-input").Input("xpub6Example");
        cut.Find("[data-dd-key-check]").Click();

        cut.WaitForAssertion(() => Assert.Equal(FirstAddress, cut.Find("[data-dd-key-preview-address]").TextContent));
        Assert.Contains("same first address", cut.Markup);
        cut.Find("[data-dd-key-save]").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, changed));
        Assert.Contains($"\"confirmFirstAddress\":\"{FirstAddress}\"", api.PutBody);
        Assert.Contains("\"key\":\"xpub6Example\"", api.PutBody);
    }

    [Fact]
    public void Saying_it_doesnt_match_saves_nothing()
    {
        var cut = RenderCard();
        cut.Find("#dd-key-input").Input("xpub6Example");
        cut.Find("[data-dd-key-check]").Click();
        cut.WaitForAssertion(() => cut.Find("[data-dd-key-preview]"));

        cut.FindAll("button").Single(b => b.TextContent.Contains("doesn't match")).Click();

        Assert.Contains("Don't use this key", cut.Find("[data-dd-key-error]").TextContent);
        Assert.Null(api.PutBody);
        Assert.Equal(0, changed);
    }

    [Theory]
    [InlineData("private_key", "That's a private key. Never share it.")]
    [InlineData("wrong_network", "other network")]
    [InlineData("not_account_key", "isn't a DigiDollar account key")]
    [InlineData("malformed", "isn't a DigiDollar key")]
    public void Each_refusal_reads_as_plain_text(string reason, string expected)
    {
        api.PreviewReason = reason;
        var cut = RenderCard();

        cut.Find("#dd-key-input").Input("whatever");
        cut.Find("[data-dd-key-check]").Click();

        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Find("[data-dd-key-error]").TextContent));
        Assert.DoesNotContain("whatever", cut.Find("[data-dd-key-error]").TextContent);
    }

    [Fact]
    public void A_saved_key_shows_its_fingerprint_and_first_address_only()
    {
        var cut = RenderCard(Saved());

        Assert.Contains("a1b2c3d4", cut.Find("[data-dd-key-fingerprint]").TextContent);
        Assert.Equal(FirstAddress, cut.Find("[data-dd-key-first]").TextContent);
        Assert.Empty(cut.FindAll("#dd-key-input"));
        Assert.Empty(cut.FindAll("[data-dd-unpaid-run]"));
    }

    [Fact]
    public void Removing_asks_first_then_removes()
    {
        var cut = RenderCard(Saved());

        cut.Find("[data-dd-key-remove]").Click();
        Assert.False(api.Deleted);
        cut.Find("[data-dd-key-remove-confirm]").Click();

        cut.WaitForAssertion(() => Assert.True(api.Deleted));
        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_long_unpaid_run_warns_to_look_further()
    {
        var cut = RenderCard(Saved(unpaidRun: 501));

        Assert.Contains("Look further for DigiDollar", cut.Find("[data-dd-unpaid-run]").TextContent);
    }

    private sealed class FakeApi : HttpMessageHandler, IHttpClientFactory
    {
        public string? PreviewReason { get; set; }
        public string? PutBody { get; private set; }
        public bool Deleted { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/v1/pay/stores/sto_1/dd-key/preview")
                return PreviewReason is { } r
                    ? Json($"{{\"error\":\"dd_key_invalid\",\"reason\":\"{r}\"}}", HttpStatusCode.BadRequest)
                    : Json($"{{\"network\":\"mainnet\",\"firstAddress\":\"{FirstAddress}\",\"keyFingerprint\":\"a1b2c3d4\"}}");
            if (request.Method == HttpMethod.Put && path == "/v1/pay/stores/sto_1/dd-key")
            {
                PutBody = await request.Content!.ReadAsStringAsync(ct);
                return Json("{\"id\":\"sto_1\"}");
            }
            if (request.Method == HttpMethod.Delete && path == "/v1/pay/stores/sto_1/dd-key")
            {
                Deleted = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
