using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace DigiByte.Api.Endpoints;

public static class PriceEndpoints
{
    public static RouteGroupBuilder MapPriceEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/price/simple?currency=usd
        group.MapGet("/simple", async (
            string? currency,
            IHttpClientFactory httpFactory,
            IMemoryCache cache) =>
        {
            currency = (currency ?? "usd").ToLower();
            var cacheKey = $"price:simple:{currency}";

            if (cache.TryGetValue(cacheKey, out JsonElement cached))
                return Results.Json(cached);

            var http = httpFactory.CreateClient("CoinGeckoProxy");
            var url = $"https://api.coingecko.com/api/v3/simple/price?ids=digibyte&vs_currencies={currency}";
            var response = await http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            cache.Set(cacheKey, json, TimeSpan.FromSeconds(60));
            return Results.Json(json);
        });

        // GET /api/price/coin — full coin data with sparkline
        group.MapGet("/coin", async (
            IHttpClientFactory httpFactory,
            IMemoryCache cache) =>
        {
            const string cacheKey = "price:coin";

            if (cache.TryGetValue(cacheKey, out JsonElement cached))
                return Results.Json(cached);

            var http = httpFactory.CreateClient("CoinGeckoProxy");
            var url = "https://api.coingecko.com/api/v3/coins/digibyte?localization=false&tickers=false&community_data=false&developer_data=false&sparkline=true";
            var response = await http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            cache.Set(cacheKey, json, TimeSpan.FromSeconds(60));
            return Results.Json(json);
        });

        // GET /api/price/fx — USD exchange rates, for display currencies CoinGecko does not price DGB in
        group.MapGet("/fx", async (
            IHttpClientFactory httpFactory,
            IMemoryCache cache) =>
        {
            const string cacheKey = "price:fx";

            if (cache.TryGetValue(cacheKey, out JsonElement cached))
                return Results.Json(new { @base = "USD", rates = cached });

            var http = httpFactory.CreateClient("CoinGeckoProxy");
            var response = await http.GetAsync("https://open.er-api.com/v6/latest/USD");

            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Object)
                return Results.StatusCode(StatusCodes.Status502BadGateway);

            var copy = rates.Clone();
            cache.Set(cacheKey, copy, TimeSpan.FromHours(6));
            return Results.Json(new { @base = "USD", rates = copy });
        });

        return group;
    }
}
