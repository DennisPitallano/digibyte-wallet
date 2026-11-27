using System.Net;
using System.Text;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// Stand-in for the price feeds behind <c>DgbPriceService</c> ("DigiPayChain" HttpClient): the exchange-rate table
/// (units per 1 USD) and CoinGecko's DGB price. No test reaches the real internet.
/// </summary>
public sealed class FakeRates : HttpMessageHandler
{
    public Dictionary<string, decimal> PerUsd { get; } = new(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m, ["PHP"] = 58.2m, ["KES"] = 129.5m };
    public decimal DgbUsd { get; set; } = 0.01m;
    public bool Down { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Down) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var url = request.RequestUri!.ToString();
        string body;
        if (url.Contains("open.er-api.com"))
        {
            var rates = string.Join(",", PerUsd.Select(kv => $"\"{kv.Key}\":{kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
            body = "{\"result\":\"success\",\"base_code\":\"USD\",\"rates\":{" + rates + "}}";
        }
        else
        {
            var cur = url[(url.LastIndexOf('=') + 1)..];
            var price = cur == "usd" ? DgbUsd : PerUsd.TryGetValue(cur, out var r) ? DgbUsd * r : 0m;
            body = "{\"digibyte\":{\"" + cur + "\":" + price.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
