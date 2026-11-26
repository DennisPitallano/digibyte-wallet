using System.Net;
using System.Text;
using System.Text.Json;
using DigiByte.Pay.Web.Components.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// The Overview's sales charts against a fake stats endpoint: rendering with data, the empty
// states, switching the period, the fiat equivalent, and the period-over-period change.
public class SalesOverviewTests : BunitContext
{
    private readonly FakeStatsApi api = new();

    public SalesOverviewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton<IHttpClientFactory>(api);
    }

    private IRenderedComponent<SalesOverview> RenderSales(string? currency = null, string? tz = "Europe/Amsterdam") =>
        Render<SalesOverview>(p => p
            .Add(x => x.Token, "dps_test")
            .Add(x => x.StoreId, "sto_1")
            .Add(x => x.TimeZoneId, tz)
            .Add(x => x.Currency, currency));

    [Fact]
    public void Renders_headline_numbers_charts_and_the_outcome_split()
    {
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dchart")));

        // Asked for 30 days by default, in the browser's zone.
        Assert.Contains(api.Requests, r => r == "GET /v1/pay/stores/sto_1/stats/daily?days=30&tz=Europe%2FAmsterdam");
        var kpis = cut.FindAll(".sales-kpi");
        Assert.Equal(3, kpis.Count);
        Assert.Contains("12.5 DGB", kpis[0].TextContent);
        Assert.Contains("10", kpis[1].TextContent);
        Assert.Contains("60% of payments", kpis[2].TextContent);
        Assert.Contains("Days follow your time zone (Europe/Amsterdam).", cut.Markup);

        // Two charts: an area for revenue, bars for payments; each SVG is an image with a summary.
        Assert.Single(cut.FindAll(".dchart-area"));
        Assert.Single(cut.FindAll(".dchart-bars"));
        var svgs = cut.FindAll("svg.dchart-svg[role=img]");
        Assert.Equal(2, svgs.Count);
        Assert.Contains("12.5 DGB in total", svgs[0].GetAttribute("aria-label"));
        Assert.Contains("10 payments, 6 paid", svgs[1].GetAttribute("aria-label"));
        // Every day is in the table alternative.
        Assert.Equal(30, cut.FindAll(".dchart-area .dchart-table tbody tr").Count);

        // Outcomes with counts and percentages; refunded shows because there is one, voided doesn't.
        var outcomes = cut.FindAll(".sales-outcomes li").Select(li => li.TextContent).ToList();
        Assert.Contains(outcomes, o => o.Contains("Paid") && o.Contains("6") && o.Contains("60%"));
        Assert.Contains(outcomes, o => o.Contains("Expired") && o.Contains("2") && o.Contains("20%"));
        Assert.Contains(outcomes, o => o.Contains("Underpaid") && o.Contains("1") && o.Contains("10%"));
        Assert.Contains(outcomes, o => o.Contains("Refunded") && o.Contains("10%"));
        Assert.DoesNotContain(outcomes, o => o.Contains("Cancelled"));
        Assert.Contains("paid 6 (60%)", cut.Find(".sales-split").GetAttribute("aria-label"));
    }

    [Fact]
    public void Shows_the_change_against_the_previous_period_only_when_there_is_one()
    {
        api.WithPrevious = true; // previous: 10 DGB revenue, 8 payments, 6 paid
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".sales-change").Count));
        var changes = cut.FindAll(".sales-change").Select(c => c.TextContent).ToList();
        Assert.Contains("+25% vs previous 30 days", changes[0]);
        Assert.Contains("+25% vs previous 30 days", changes[1]);
        Assert.Contains("0% vs previous 30 days", changes[2]);
        Assert.Contains("is-up", cut.FindAll(".sales-change")[0].ClassName);

        api.WithPrevious = false;
        cut.FindAll(".sales-period button").Single(b => b.TextContent.Trim() == "7 days").Click();
        cut.WaitForAssertion(() => Assert.Contains("Last 7 days", cut.Find(".sales-window").TextContent));
        Assert.Empty(cut.FindAll(".sales-change"));
    }

    [Fact]
    public void A_store_without_payments_gets_an_empty_state()
    {
        api.Empty = true;
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.Contains("No sales yet", cut.Markup));
        Assert.Empty(cut.FindAll(".dchart"));
        Assert.Empty(cut.FindAll(".sales-kpi"));
    }

    [Fact]
    public void A_quiet_period_suggests_a_longer_one()
    {
        api.QuietWindow = true;
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.Contains("No payments in the last 30 days", cut.Markup));
        Assert.Contains("Pick a longer period", cut.Markup);
    }

    [Fact]
    public void Shows_a_loading_skeleton_until_the_stats_arrive()
    {
        api.Gate = new TaskCompletionSource();
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".sales-loading")));
        Assert.Equal("true", cut.Find("section.sales").GetAttribute("aria-busy"));

        api.Gate.SetResult();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dchart")));
        Assert.Equal("false", cut.Find("section.sales").GetAttribute("aria-busy"));
    }

    [Fact]
    public void Toggling_the_period_reloads_with_that_many_days()
    {
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.Equal(30, cut.FindAll(".dchart-area .dchart-table tbody tr").Count));
        Assert.Equal("true", cut.FindAll(".sales-period button").Single(b => b.TextContent.Trim() == "30 days").GetAttribute("aria-pressed"));

        cut.FindAll(".sales-period button").Single(b => b.TextContent.Trim() == "90 days").Click();

        cut.WaitForAssertion(() => Assert.Equal(90, cut.FindAll(".dchart-area .dchart-table tbody tr").Count));
        Assert.Contains(api.Requests, r => r.StartsWith("GET /v1/pay/stores/sto_1/stats/daily?days=90"));
        Assert.Equal("true", cut.FindAll(".sales-period button").Single(b => b.TextContent.Trim() == "90 days").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.FindAll(".sales-period button").Single(b => b.TextContent.Trim() == "30 days").GetAttribute("aria-pressed"));
        Assert.Contains("Last 90 days", cut.Find(".sales-window").TextContent);
    }

    [Fact]
    public void A_fiat_default_currency_adds_the_equivalent_at_todays_rate()
    {
        api.Price = 2m; // 1 DGB = €2
        var cut = RenderSales(currency: "EUR");
        cut.WaitForAssertion(() => Assert.Contains("≈ €25.00 EUR at today's rate", cut.FindAll(".sales-kpi")[0].TextContent));

        Assert.Contains(api.Requests, r => r == "GET /v1/pay/public/price?currency=eur");
        // The table gets a fiat column and the tooltip the fiat value.
        Assert.Contains(cut.FindAll(".dchart-area .dchart-table thead th"), th => th.TextContent == "≈ EUR");
        var plot = cut.Find(".dchart-area .dchart-plot");
        plot.Focus();
        cut.WaitForAssertion(() => Assert.Contains("€", cut.Find(".dchart-area .dchart-tip").TextContent));
        Assert.Contains("2.5 DGB · ≈ €5.00 EUR", cut.Find(".dchart-area .dchart-tip").TextContent);
    }

    [Fact]
    public void Dgb_and_no_currency_show_no_fiat()
    {
        var cut = RenderSales(currency: "DGB");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".sales-kpi")));
        Assert.DoesNotContain("today's rate", cut.Markup);
        Assert.DoesNotContain(api.Requests, r => r.Contains("/public/price"));
    }

    [Fact]
    public void Arrow_keys_move_the_tooltip_across_the_days_and_announce_it()
    {
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dchart")));
        var plot = cut.Find(".dchart-bars .dchart-plot");
        Assert.Equal("0", plot.GetAttribute("tabindex"));

        plot.Focus(); // starts on today
        cut.WaitForAssertion(() => Assert.Contains("3 payments, 2 paid", cut.Find(".dchart-bars .dchart-tip").TextContent));
        Assert.Contains("3 payments, 2 paid", cut.Find(".dchart-bars [aria-live=polite]").TextContent);

        cut.Find(".dchart-bars .dchart-plot").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.WaitForAssertion(() => Assert.Contains("7 payments, 4 paid", cut.Find(".dchart-bars .dchart-tip").TextContent));
        cut.Find(".dchart-bars .dchart-plot").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.WaitForAssertion(() => Assert.Contains("0 payments, 0 paid", cut.Find(".dchart-bars .dchart-tip").TextContent));
        // Never past the last day.
        cut.Find(".dchart-bars .dchart-plot").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find(".dchart-bars .dchart-plot").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.WaitForAssertion(() => Assert.Contains("3 payments, 2 paid", cut.Find(".dchart-bars .dchart-tip").TextContent));

        cut.Find(".dchart-bars .dchart-plot").Blur();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dchart-bars .dchart-tip")));
    }

    [Fact]
    public void Hovering_a_day_shows_its_tooltip()
    {
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dchart")));

        cut.FindAll(".dchart-area .dchart-hit")[^2].MouseOver();

        cut.WaitForAssertion(() => Assert.Contains("10 DGB", cut.Find(".dchart-area .dchart-tip").TextContent));
    }

    [Fact]
    public void A_failed_load_offers_to_try_again()
    {
        api.Fail = true;
        var cut = RenderSales();
        cut.WaitForAssertion(() => Assert.Contains("Sales couldn't be loaded", cut.Markup));

        api.Fail = false;
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Try again").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".dchart")));
    }

    /// <summary>
    /// A stats endpoint answering any period: today (the last day) has 3 payments (2 paid, 2.5 DGB) and
    /// the day before 7 payments (4 paid, 10 DGB): 10 payments, 6 paid, 12.5 DGB, 2 expired, 1 underpaid, 1 refunded.
    /// </summary>
    private sealed class FakeStatsApi : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Requests { get; } = [];
        public bool Empty { get; set; }
        public bool QuietWindow { get; set; }
        public bool WithPrevious { get; set; }
        public bool Fail { get; set; }
        public decimal? Price { get; set; }
        public TaskCompletionSource? Gate { get; set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://pay-api.test") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (Requests) Requests.Add($"{request.Method.Method} {path}");
            if (path.StartsWith("/v1/pay/public/price"))
                return Price is decimal p ? Json(new { currency = "eur", dgbPrice = p, fetchedAt = DateTime.UtcNow }) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (!path.StartsWith("/v1/pay/stores/sto_1/stats/daily")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (Gate is not null) await Gate.Task;
            if (Fail) return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"boom"}""") };

            var q = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var days = int.Parse(q["days"] ?? "30");
            var to = new DateOnly(2026, 11, 26);
            var from = to.AddDays(-(days - 1));
            var busy = !Empty && !QuietWindow;
            var series = Enumerable.Range(0, days).Select(i =>
            {
                var date = from.AddDays(i);
                var today = busy && date == to;
                var yesterday = busy && date == to.AddDays(-1);
                return new
                {
                    date,
                    payments = today ? 3 : yesterday ? 7 : 0,
                    paid = today ? 2 : yesterday ? 4 : 0,
                    open = 0,
                    expired = yesterday ? 2 : 0,
                    underpaid = today ? 1 : 0,
                    refunded = yesterday ? 1 : 0,
                    voided = 0,
                    revenueDgb = today ? 2.5m : yesterday ? 10m : 0m,
                };
            }).ToList();
            object Totals(int payments, int paid, decimal revenue) => new { payments, paid, open = 0, expired = busy ? 2 : 0, underpaid = busy ? 1 : 0, refunded = busy ? 1 : 0, voided = 0, revenueDgb = revenue };
            return Json(new
            {
                storeId = "sto_1", days, timeZone = q["tz"] ?? "UTC", from, to,
                fromUtc = DateTime.UtcNow, toUtc = DateTime.UtcNow,
                storeHasPayments = !Empty,
                totals = busy ? Totals(10, 6, 12.5m) : Totals(0, 0, 0m),
                previous = WithPrevious ? new { from = from.AddDays(-days), to = from.AddDays(-1), totals = Totals(8, 6, 10m) } : null,
                series,
            });
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
    }
}
