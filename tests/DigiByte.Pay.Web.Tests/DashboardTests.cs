using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The merchant dashboard against a fake Pay API: fiat links are priced at the live rate, lists
// and tiles use the API's totals, the getting-started checklist, and the webhook signing secret.
public class DashboardTests : BunitContext
{
    private readonly FakePayApi api = new();

    public DashboardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string?>("localStorage.getItem", "digipay-token").SetResult("dps_test");
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(api);
        Services.AddScoped<ToastService>();
    }

    private IRenderedComponent<Dashboard> RenderSection(string? section, string? query = null)
    {
        if (query is not null)
        {
            var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            nav.NavigateTo($"/merchant/{section}{query}");
        }
        return Render<Dashboard>(p => p.Add(x => x.Section, section));
    }

    private static IEnumerable<AngleSharp.Dom.IElement> Buttons(IRenderedComponent<Dashboard> cut, string text) =>
        cut.FindAll("button").Where(b => b.TextContent.Trim() == text);

    // ----- 1. Fiat payment links -----

    [Fact]
    public void A_fiat_link_is_charged_in_dgb_at_the_live_price()
    {
        api.Price = 0.0123m;
        var cut = RenderSection("links");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Your currency")));

        Buttons(cut, "Your currency").First().Click();
        cut.WaitForAssertion(() => Assert.Contains("1 DGB = $0.0123 USD", cut.Markup));
        Assert.Contains(api.Requests, r => r.StartsWith("GET /v1/pay/public/price?currency=usd"));

        cut.Find("#link-amount-fiat").Input("12.50");
        cut.WaitForAssertion(() => Assert.False(Buttons(cut, "Create payment link").Single().HasAttribute("disabled")));
        Buttons(cut, "Create payment link").Single().Click();

        cut.WaitForAssertion(() => Assert.NotNull(api.CreatedSession));
        var body = api.CreatedSession!;
        // 12.50 / 0.0123 = 1016.260162601626… rounded to 8 decimals (1 satoshi).
        Assert.Equal(1016.26016260m, body["amount"]!.GetValue<decimal>());
        Assert.Equal(0.0123m, body["dgbPriceAtCreation"]!.GetValue<decimal>());
        Assert.Equal(12.50m, body["fiatAmount"]!.GetValue<decimal>());
        Assert.Equal("USD", body["fiatCurrency"]!.GetValue<string>());
    }

    [Fact]
    public void A_link_can_be_priced_in_any_wallet_currency_from_the_picker()
    {
        api.Price = 1.52m;
        var cut = RenderSection("links");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Your currency")));
        Buttons(cut, "Your currency").First().Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#link-currency")));

        cut.Find("#link-currency").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#link-currency-picker")));
        cut.Find("#link-currency-picker input[type=search]").Input("kenya");
        cut.WaitForAssertion(() => Assert.Contains("Kenyan Shilling", cut.Find("#link-currency-picker").TextContent));
        cut.FindAll("#link-currency-picker .ui-picker-row").Single(b => b.TextContent.Contains("KES")).Click();

        cut.WaitForAssertion(() => Assert.Contains(api.Requests, r => r.StartsWith("GET /v1/pay/public/price?currency=kes")));
        Assert.Contains("KES", cut.Find("#link-currency").TextContent);
    }

    [Fact]
    public void Without_a_live_price_a_fiat_link_cant_be_created_and_says_why()
    {
        api.Price = null; // price feed down
        var cut = RenderSection("links");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Your currency")));

        Buttons(cut, "Your currency").First().Click();
        cut.WaitForAssertion(() => Assert.Contains("live DGB price isn't available", cut.Markup));
        cut.Find("#link-amount-fiat").Input("10");

        Assert.True(Buttons(cut, "Create payment link").Single().HasAttribute("disabled"));
        Assert.Null(api.CreatedSession);
    }

    // ----- 2. Totals and Load more -----

    [Fact]
    public void The_payments_list_shows_the_api_total_and_loads_more()
    {
        api.Total = 30;
        var cut = RenderSection("sessions");
        cut.WaitForAssertion(() => Assert.Contains("Showing 25 of 30", cut.Markup));
        Assert.Equal(25, cut.FindAll("a.dash-row-link").Count);

        Buttons(cut, "Load more").Single().Click();
        cut.WaitForAssertion(() => Assert.Equal(30, cut.FindAll("a.dash-row-link").Count));
        Assert.Contains(api.Requests, r => r.Contains("skip=25"));
        Assert.Empty(Buttons(cut, "Load more"));
        Assert.Contains("Showing 30 of 30", cut.Markup);
    }

    [Fact]
    public void Overview_tiles_count_every_payment_not_just_the_first_page()
    {
        api.Total = 130;
        api.StatusTotals["underpaid"] = 2;
        api.StatusTotals["expired"] = 7;
        var cut = RenderSection(null);
        cut.WaitForAssertion(() => Assert.Contains("130", cut.Markup));

        var underpaid = cut.FindAll("a.dash-stat").Single(a => a.TextContent.Contains("Underpaid"));
        Assert.Contains("2", underpaid.TextContent);
        Assert.Contains("Needs your action", underpaid.TextContent);
        Assert.EndsWith("/merchant/sessions?status=underpaid", underpaid.GetAttribute("href"));
        var expired = cut.FindAll("a.dash-stat").Single(a => a.TextContent.Contains("Expired"));
        Assert.Contains("7", expired.TextContent);
    }

    [Fact]
    public void The_status_filter_in_the_address_reaches_the_api()
    {
        api.Total = 3;
        var cut = RenderSection("sessions", "?status=underpaid");
        cut.WaitForAssertion(() => Assert.Contains(api.Requests, r => r.StartsWith("GET /v1/pay/sessions?take=25") && r.Contains("status=underpaid")));
        var pressed = cut.FindAll(".dash-filter .segmented-item").Single(b => b.GetAttribute("aria-pressed") == "true");
        Assert.Equal("Underpaid", pressed.TextContent.Trim());
    }

    // ----- 13. Getting started -----

    [Fact]
    public void A_new_merchant_sees_the_getting_started_checklist()
    {
        api.HasReceive = false;
        api.Total = 0;
        var cut = RenderSection(null);
        cut.WaitForAssertion(() => Assert.Contains("0 of 3 done", cut.Markup));

        Assert.NotNull(cut.Find("a[href='/merchant/settings#receive']"));
        Assert.Contains("Create your first payment", cut.Markup);
        Assert.Contains("Connect your shop or pair a POS tablet", cut.Markup);
        Assert.DoesNotContain("No stores", cut.Markup);
    }

    [Fact]
    public void The_checklist_counts_progress()
    {
        api.HasReceive = true;
        api.Total = 0;
        var cut = RenderSection(null);
        cut.WaitForAssertion(() => Assert.Contains("1 of 3 done", cut.Markup));
    }

    [Fact]
    public void The_checklist_hides_once_every_step_is_done()
    {
        api.HasReceive = true;
        api.Total = 4;
        api.ApiKeys = 1;
        var cut = RenderSection(null);
        cut.WaitForAssertion(() => Assert.Contains("All payments", cut.Markup));
        Assert.DoesNotContain("Get started", cut.Markup);
    }

    // ----- 14. Grouped menu -----

    [Fact]
    public void The_rail_lists_sections_under_their_groups()
    {
        var cut = RenderSection(null);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dp-rail .dash-nav-heading")));

        var headings = cut.FindAll(".dp-rail .dash-nav-heading").Select(h => h.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Payments", "Insights", "Developers", "Account" }, headings);
        var payments = cut.Find("#nav-rail-payments + ul");
        Assert.Contains("Payments", payments.TextContent);
        Assert.Contains("POS tablets", payments.TextContent);
        Assert.NotNull(cut.Find(".dp-rail a[href='/merchant/sessions']"));
    }

    // ----- 3. Webhook signing secret -----

    [Fact]
    public void The_first_webhook_save_shows_the_signing_secret_once()
    {
        api.WebhookUrl = null;
        api.SecretOnPatch = "s3cret-first";
        var cut = RenderSection("webhook");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#webhook-url")));
        Assert.Contains("Save a webhook address above", cut.Markup);

        cut.Find("#webhook-url").Change("https://shop.example/hook");
        Buttons(cut, "Save").Single().Click();

        cut.WaitForAssertion(() => Assert.Contains("s3cret-first", cut.Find("#webhook-secret").TextContent));
        Assert.Contains("copy it now", cut.Markup);
        Assert.NotNull(cut.Find("button[aria-label='Copy signing secret']"));

        Buttons(cut, "Dismiss").Single().Click();
        Assert.DoesNotContain("s3cret-first", cut.Markup);
    }

    [Fact]
    public void Rotating_asks_first_then_shows_the_new_secret()
    {
        api.WebhookUrl = "https://shop.example/hook";
        api.HasSecret = true;
        var cut = RenderSection("webhook");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Rotate secret")));

        Buttons(cut, "Rotate secret").First().Click();
        cut.WaitForAssertion(() => Assert.Contains("old secret stops working", cut.Markup));
        Assert.DoesNotContain(api.Requests, r => r.Contains("webhook-secret/rotate"));

        // The dialog's confirm button.
        cut.Find(".ui-dialog .ui-dialog-actions .btn-danger").Click();
        cut.WaitForAssertion(() => Assert.Contains("rotated-secret", cut.Markup));
        Assert.Contains(api.Requests, r => r == "POST /v1/pay/stores/sto_1/webhook-secret/rotate");
    }

    // ----- 4. Unpairing a tablet refreshes the tablets -----

    [Fact]
    public void Unpairing_a_tablet_reloads_the_paired_devices()
    {
        api.Devices = 1;
        var cut = RenderSection("pos");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Unpair")));
        var before = api.Requests.Count(r => r == "GET /v1/pay/pos/devices");

        api.Devices = 0;
        Buttons(cut, "Unpair").Single().Click();
        cut.Find(".ui-dialog .ui-dialog-actions .btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains("No paired tablets", cut.Markup));
        Assert.True(api.Requests.Count(r => r == "GET /v1/pay/pos/devices") > before);
    }

    // ----- 4b. Pairing codes -----

    [Fact]
    public void A_new_pairing_code_shows_with_copy_and_countdown_then_turns_into_paired()
    {
        var cut = RenderSection("pos");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Generate code")));

        Buttons(cut, "Generate code").Single().Click();

        cut.WaitForAssertion(() => Assert.Equal("ABC234", cut.Find(".dash-pair-code").TextContent.Trim()));
        Assert.NotEmpty(Buttons(cut, "Copy code"));
        Assert.Matches(@"Expires in \d+:\d\d", cut.Find(".dash-pair").TextContent);
        Assert.Contains("Waiting for the tablet", cut.Markup);

        api.Claimed = true; // the tablet types the code in
        cut.WaitForAssertion(() => Assert.Contains("Tablet paired", cut.Markup), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Re_pairing_a_tablet_reuses_its_name_and_revokes_the_old_key_once_paired()
    {
        api.Devices = 1;
        var cut = RenderSection("pos");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Re-pair")));

        Buttons(cut, "Re-pair").Single().Click();

        cut.WaitForAssertion(() => Assert.Contains("Re-pair code for POS — Front", cut.Markup));
        Assert.Equal("Front", api.PairingBody!["deviceLabel"]!.GetValue<string>());
        Assert.DoesNotContain(api.Requests, r => r == "DELETE /v1/pay/api-keys/key_pos0");

        api.Claimed = true;
        cut.WaitForAssertion(() => Assert.Contains(api.Requests, r => r == "DELETE /v1/pay/api-keys/key_pos0"), TimeSpan.FromSeconds(15));
        cut.WaitForAssertion(() => Assert.Contains("old key has been revoked", cut.Markup), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_tablet_paired_before_store_binding_is_flagged_for_re_pairing()
    {
        api.Devices = 1;
        api.DeviceStoreId = null; // paired before tablets were tied to a store
        var cut = RenderSection("pos");
        cut.WaitForAssertion(() => Assert.Contains("Re-pair it to limit it to", cut.Markup));
    }

    [Fact]
    public void A_store_bound_tablet_is_not_flagged()
    {
        api.Devices = 1;
        var cut = RenderSection("pos");
        cut.WaitForAssertion(() => Assert.NotEmpty(Buttons(cut, "Unpair")));
        Assert.DoesNotContain("Re-pair it to limit it to", cut.Markup);
    }

    // ----- 5. Donation without a receive address -----

    [Fact]
    public void The_donation_page_explains_a_missing_receive_address_instead_of_loading()
    {
        api.HasReceive = false;
        var cut = RenderSection("donation");
        cut.WaitForAssertion(() => Assert.Contains("Configure where to receive payments", cut.Markup));
        Assert.DoesNotContain("Loading the donation address", cut.Markup);
    }

    // ----- 12. Plain errors -----

    [Fact]
    public void Api_errors_are_shown_in_plain_language_not_raw()
    {
        api.CreateError = (HttpStatusCode.BadRequest, """{"error":"store has no receive address or xpub configured — PATCH /v1/pay/stores/{id} first"}""");
        var cut = RenderSection("new");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#np-amount")));
        cut.Find("#np-amount").Change("5");
        Buttons(cut, "Create payment").Single().Click();

        cut.WaitForAssertion(() => Assert.Contains("nowhere to receive payments", cut.Markup));
        Assert.DoesNotContain("PATCH", cut.Markup);
    }

    /// <summary>A small in-memory Pay API: one merchant, one store, generated sessions.</summary>
    private sealed class FakePayApi : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];
        public bool HasReceive { get; set; } = true;
        public string? WebhookUrl { get; set; }
        public bool HasSecret { get; set; }
        public string? SecretOnPatch { get; set; }
        public int Total { get; set; } = 3;
        public Dictionary<string, int> StatusTotals { get; } = new();
        public decimal? Price { get; set; } = 0.01m;
        public int ApiKeys { get; set; }
        public int Devices { get; set; }
        public bool Claimed;
        public string? DeviceStoreId = "sto_1";
        public JsonObject? PairingBody;
        public JsonObject? CreatedSession { get; private set; }
        public (HttpStatusCode Status, string Body)? CreateError { get; set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            var method = request.Method.Method;
            lock (Requests) Requests.Add($"{method} {path}");
            var q = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);

            if (method == "GET" && path == "/v1/pay/me/")
                return Json(new { id = "mer_1", displayName = "Test Shop", digiIdAddress = (string?)null, createdAt = DateTime.UtcNow, stores = new[] { Store() } });

            if (method == "GET" && request.RequestUri.AbsolutePath == "/v1/pay/sessions")
            {
                var status = q["status"];
                var take = int.Parse(q["take"] ?? "25", CultureInfo.InvariantCulture);
                var skip = int.Parse(q["skip"] ?? "0", CultureInfo.InvariantCulture);
                var total = status is null ? Total : StatusTotals.GetValueOrDefault(status);
                var rows = Enumerable.Range(skip, Math.Max(0, Math.Min(take, total - skip))).Select(i => Session($"ses_{i:D4}", status ?? "confirmed"));
                return Json(new { total, take, skip, sessions = rows });
            }

            if (method == "POST" && path == "/v1/pay/sessions")
            {
                if (CreateError is { } err) return new HttpResponseMessage(err.Status) { Content = new StringContent(err.Body, Encoding.UTF8, "application/json") };
                CreatedSession = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                return Json(Session("ses_new", "pending"));
            }

            if (method == "GET" && path.StartsWith("/v1/pay/public/price"))
                return Price is decimal p
                    ? Json(new { currency = q["currency"], dgbPrice = p, fetchedAt = DateTime.UtcNow })
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"error":"price unavailable"}""") };

            if (method == "PATCH" && path == "/v1/pay/stores/sto_1")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                string? secret = null;
                if (body["webhookUrl"] is JsonNode url)
                {
                    WebhookUrl = url.GetValue<string>();
                    if (!HasSecret) { secret = SecretOnPatch; HasSecret = true; }
                }
                var dto = JsonSerializer.SerializeToNode(Store(), JsonSerializerOptions.Web)!.AsObject();
                dto["webhookSecret"] = secret;
                return Json(dto);
            }

            if (method == "POST" && path == "/v1/pay/stores/sto_1/webhook-secret/rotate")
                return Json(new { webhookSecret = "rotated-secret" });

            if (method == "GET" && path.StartsWith("/v1/pay/stores/sto_1/webhook-deliveries"))
                return Json(Array.Empty<object>());

            if (method == "GET" && path == "/v1/pay/stores/sto_1/donation-address")
                return HasReceive ? Json(new { address = "dgb1qdonation", mode = "address" }) : new HttpResponseMessage(HttpStatusCode.BadRequest);

            if (method == "GET" && path == "/v1/pay/api-keys")
                return Json(Enumerable.Range(0, ApiKeys).Select(i => new { id = $"key_{i}", label = "production", prefix = "dgp_x", createdAt = DateTime.UtcNow, lastUsedAt = (DateTime?)null, revokedAt = (DateTime?)null, isRevoked = false }));

            if (method == "POST" && path == "/v1/pay/pos/pairings")
            {
                PairingBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                var name = PairingBody["deviceLabel"]?.GetValue<string>();
                return Json(new { code = "ABC234", expiresAt = DateTime.UtcNow.AddMinutes(10), storeId = "sto_1", storeName = "Corner Café",
                    deviceLabel = name is null ? "POS device — today" : $"POS — {name}", prefix = "dgp_new" });
            }

            if (method == "GET" && path == "/v1/pay/pos/devices")
            {
                var devices = Enumerable.Range(0, Devices)
                    .Select(i => new { id = $"key_pos{i}", label = "POS — Front", prefix = "dgp_p", createdAt = DateTime.UtcNow, lastUsedAt = (DateTime?)null, storeId = DeviceStoreId })
                    .ToList();
                if (Claimed) devices.Add(new { id = "key_new", label = "POS — Front", prefix = "dgp_new", createdAt = DateTime.UtcNow, lastUsedAt = (DateTime?)null, storeId = (string?)"sto_1" });
                return Json(devices);
            }

            if (method == "DELETE" && path.StartsWith("/v1/pay/api-keys/"))
                return Json(new { ok = true });

            if (method == "GET" && path.StartsWith("/v1/pay/stores/sto_1/analytics"))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private object Store() => new
        {
            id = "sto_1", merchantId = "mer_1", name = "Corner Café", network = "mainnet",
            receiveMode = HasReceive ? "address" : "unconfigured", hasReceive = HasReceive,
            receivePreview = HasReceive ? "dgb1qreceive" : null,
            webhookUrl = WebhookUrl, hasWebhookSecret = HasSecret,
            defaultSessionExpiryMinutes = (int?)null, createdAt = DateTime.UtcNow, digiDollarEnabled = false,
        };

        private static object Session(string id, string status) => new
        {
            id, storeId = "sto_1", address = "dgb1qexample", amountSatoshis = 100_000_000L, amount = 1m,
            fiatCurrency = (string?)null, fiatAmount = (decimal?)null, label = (string?)null, memo = (string?)null,
            status, createdAt = DateTime.UtcNow, expiresAt = DateTime.UtcNow.AddMinutes(30),
            seenAt = (DateTime?)null, paidAt = (DateTime?)null, receivedSatoshis = 0L, confirmations = 0, paidTxid = (string?)null,
            uri = "digibyte:dgb1qexample?amount=1", checkoutUrl = $"http://pay.test/pay/{id}",
        };

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
    }
}
