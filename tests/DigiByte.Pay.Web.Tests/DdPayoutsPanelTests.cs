using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Pay.Web.Components.Pages.Merchant;
using DigiByte.Pay.Web.Components.Shared;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace DigiByte.Pay.Web.Tests;

/// <summary>specs/034 tasks T041, T048, T054: the Dashboard's DigiDollar payouts section and the change banner.</summary>
public class DdPayoutsPanelTests : BunitContext
{
    private readonly FakeApi api = new();

    public DdPayoutsPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
    }

    private static string DdAddress(string network = "mainnet") =>
        DigiByte.Crypto.Addresses.DigiDollarAddressValidator.Encode(new NBitcoin.Key().PubKey.TaprootInternalKey.ToBytes(), network);

    private IRenderedComponent<DdPayoutsPanel> RenderPanel() =>
        Render<DdPayoutsPanel>(p => p.Add(x => x.Token, "dps_test").Add(x => x.StoreId, "sto_1").Add(x => x.StoreName, "Corner Café").Add(x => x.Network, "mainnet"));

    [Theory]
    [InlineData("not an address", "isn't a DigiDollar address")]
    [InlineData("WRONGNET", "testnet DigiDollar address")]
    [InlineData("DGB", "DGB address")]
    public void Bad_addresses_show_a_message_and_never_reach_the_API(string kind, string expected)
    {
        var input = kind switch
        {
            "WRONGNET" => DdAddress("testnet"),
            "DGB" => new NBitcoin.Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet).ToString(),
            _ => kind,
        };
        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Find("#dd-address-input"));

        cut.Find("#dd-address-input").Input(input);
        cut.FindAll("button").First(b => b.TextContent.Contains("Review")).Click();

        Assert.Contains(expected, cut.Find("#dd-address-error").TextContent);
        Assert.DoesNotContain(api.Requests, r => r.StartsWith("PUT", StringComparison.Ordinal));
        Assert.DoesNotContain(api.Requests, r => r.Contains("step-up", StringComparison.Ordinal));
    }

    [Fact]
    public void A_valid_address_is_shown_back_with_start_and_end_highlighted_then_starts_the_digi_id_step_up()
    {
        var address = DdAddress();
        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Find("#dd-address-input"));

        cut.Find("#dd-address-input").Input(address);
        cut.FindAll("button").First(b => b.TextContent.Contains("Review")).Click();

        var marks = cut.FindAll("#dd-confirm mark").Select(m => m.TextContent).ToList();
        Assert.Equal([address[..6], address[^6..]], marks);

        cut.FindAll("button").First(b => b.TextContent.Contains("Confirm with Digi-ID")).Click();
        cut.WaitForAssertion(() => cut.Find("#dd-stepup img"));
        Assert.Contains(api.Requests, r => r == "GET /v1/pay/payouts/step-up/challenge");
    }

    [Fact]
    public void Held_DD_shows_why_and_where_it_came_from()
    {
        api.HoldReason = "waiting_confirmations";
        api.Extra = 250;
        var cut = RenderPanel();

        cut.WaitForAssertion(() => Assert.Contains("6 network confirmations", cut.Find("#dd-hold-reason").TextContent));
        Assert.Contains("refund to buyers", cut.Find("#dd-by-source").TextContent);
        Assert.Contains("12.50 DD", cut.Find("#dd-confirmed").TextContent);
    }

    [Fact]
    public void A_pending_change_shows_when_it_takes_effect_and_can_be_cancelled()
    {
        api.Pending = true;
        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Find("#dd-pending-change"));

        cut.FindAll("button").First(b => b.TextContent.Contains("Cancel change")).Click();

        cut.WaitForAssertion(() => Assert.Contains(api.Requests, r => r == "DELETE /v1/pay/stores/sto_1/payout-address/pending"));
    }

    [Fact]
    public void A_failed_payout_says_the_DD_is_still_held()
    {
        api.Failed = true;
        var cut = RenderPanel();
        cut.WaitForAssertion(() => Assert.Contains("still held for you", cut.Find("#dd-payouts").TextContent));
    }

    [Fact]
    public void The_banner_lists_unacknowledged_changes_until_dismissed()
    {
        api.Alert = true;
        var cut = Render<PayoutAlertsBanner>(p => p.Add(x => x.Token, "dps_test"));
        cut.WaitForAssertion(() => Assert.Contains("change requested", cut.Find("#payout-alerts").TextContent));

        cut.FindAll("button").First(b => b.TextContent.Contains("Got it")).Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#payout-alerts")));
        Assert.Contains(api.Requests, r => r.EndsWith("/payout-address/acknowledge", StringComparison.Ordinal));
    }

    [Fact]
    public void Rules_match_the_server_reasons()
    {
        Assert.Null(PayoutAddressRules.Problem(DdAddress(), "mainnet"));
        Assert.NotNull(PayoutAddressRules.Problem(DdAddress("regtest"), "mainnet"));
        Assert.Contains("DigiPay's own", PayoutAddressRules.Reason("digipay_address"));
    }

    // ----- DD from deleted stores (specs/034 T060) -----

    private IRenderedComponent<DdPayoutsPanel> RenderMerchantPanel() =>
        Render<DdPayoutsPanel>(p => p.Add(x => x.Token, "dps_test").Add(x => x.MerchantLevel, true).Add(x => x.Network, "mainnet"));

    [Fact]
    public void The_merchant_level_panel_stays_hidden_while_no_DD_from_deleted_stores_is_held()
    {
        var cut = RenderMerchantPanel();
        cut.WaitForAssertion(() => Assert.Contains(api.Requests, r => r == "GET /v1/pay/me/dd-balance"));

        Assert.Empty(cut.FindAll("#dd-merchant-level"));
        Assert.DoesNotContain(api.Requests, r => r.Contains("/stores/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_merchant_level_panel_shows_DD_from_deleted_stores_with_its_own_payout_address_and_no_schedule()
    {
        api.MerchantCents = 4250;
        var cut = RenderMerchantPanel();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#dd-merchant-level")));
        Assert.Contains("42.50 DD", cut.Find("#dd-confirmed").TextContent);
        Assert.NotNull(cut.Find("#dd-address-input"));
        Assert.Empty(cut.FindAll("#dd-schedule"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Pay out now"));
        Assert.Contains(api.Requests, r => r.StartsWith("GET /v1/pay/me/dd-balance/payouts", StringComparison.Ordinal));
    }

    private sealed class FakeApi : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];
        public string? HoldReason { get; set; }
        public long Extra { get; set; }
        public bool Pending { get; set; }
        public bool Failed { get; set; }
        public bool Alert { get; set; }
        /// <summary>DD from deleted stores on GET /v1/pay/me/dd-balance (specs/034 T060).</summary>
        public long MerchantCents { get; set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            var method = request.Method.Method;
            lock (Requests) Requests.Add($"{method} {path}");

            if (method == "GET" && path == "/v1/pay/stores/sto_1/dd-balance")
                return Json(new
                {
                    confirmedCents = 1250L, confirmingCents = 0L,
                    bySource = new { payment = 1000L, overpayment = 0L, late = 0L, extraRefund = Extra, donation = 0L },
                    lastCheckedAt = DateTime.UtcNow, stale = false,
                    payoutAddress = Pending ? "DDcurrent" : null,
                    pendingChange = Pending ? new { id = "pac_1", newAddress = "DDnew", effectiveAt = DateTime.UtcNow.AddHours(48) } : null,
                    schedule = new { mode = "confirmed" }, autoPaused = false, holdReason = HoldReason, nextPayoutAt = (DateTime?)null,
                });
            if (method == "GET" && path.StartsWith("/v1/pay/stores/sto_1/payouts", StringComparison.Ordinal))
                return Json(new
                {
                    items = Failed
                        ? new object[] { new { id = "dpo_1", amountCents = 500L, destination = "DDdest000000000000", status = "failed", failureReason = "The DigiDollar network refused the payout.", willRetry = true, createdAt = DateTime.UtcNow, items = Array.Empty<object>() } }
                        : [],
                });
            if (method == "GET" && path == "/v1/pay/me/dd-balance")
                return Json(new
                {
                    confirmedCents = MerchantCents, confirmingCents = 0L,
                    bySource = new { payment = MerchantCents, overpayment = 0L, late = 0L, extraRefund = 0L, donation = 0L },
                    lastCheckedAt = DateTime.UtcNow, stale = false, payoutAddress = (string?)null, pendingChange = (object?)null,
                    schedule = (object?)null, autoPaused = false, holdReason = MerchantCents > 0 ? "no_address" : null, nextPayoutAt = (DateTime?)null,
                });
            if (method == "GET" && path.StartsWith("/v1/pay/me/dd-balance/payouts", StringComparison.Ordinal))
                return Json(new { items = Array.Empty<object>() });
            if (method == "GET" && path == "/v1/pay/payouts/step-up/challenge")
                return Json(new { nonce = "n1", uri = "digiid://pay.test/v1/pay/auth/callback?x=n1", expiresAt = DateTime.UtcNow.AddMinutes(5) });
            if (method == "GET" && path.StartsWith("/v1/pay/auth/poll/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"status\":\"pending\"}") });
            if (method == "DELETE" && path == "/v1/pay/stores/sto_1/payout-address/pending")
            {
                Pending = false;
                return Json(new { change = new { } });
            }
            if (method == "GET" && path == "/v1/pay/me/payout-alerts")
                return Json(new
                {
                    items = Alert
                        ? new object[] { new { changeId = "pac_1", storeId = "sto_1", storeName = "Corner Café", @event = "change_requested", oldAddress = "DDold", newAddress = "DDnew", requestedAt = DateTime.UtcNow, effectiveAt = DateTime.UtcNow.AddHours(48), status = "pending" } }
                        : [],
                });
            if (method == "POST" && path.EndsWith("/payout-address/acknowledge", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        });
    }
}
