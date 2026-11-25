# Contract: Daily price range (price proxy)

New endpoint on the existing price proxy, `DigiByte.Api` → `MapGroup("/api/price")`.

## Request

```http
GET /api/price/range?currency=usd&from=2026-10-31&to=2026-12-01
```

| Parameter | Rules | On violation |
|---|---|---|
| `currency` | Lower-case code from the wallet's `FiatCurrencies` list (native or converted) | `400` |
| `asset` | `dgb` (default) or `dd`; `dd` returns the price of 1 USD in `currency` (the DD peg) per day | `400` |
| `from`, `to` | `yyyy-MM-dd`, `from ≤ to`, `to` not after tomorrow (UTC), `from` not before 2014-01-01 (no span limit: older days come from the archive) | `400` |

Nothing else is accepted. The request carries no address, amount, wallet id or transaction date.

## Response `200`

```json
{
  "currency": "usd",
  "asset": "dgb",
  "basis": "native",
  "from": "2026-10-31",
  "to": "2026-12-01",
  "prices": [
    { "date": "2026-10-31", "price": 0.01102 },
    { "date": "2026-11-01", "price": 0.01117 }
  ]
}
```

- One entry per UTC day that has data, in ascending order. The price is the day's closing price: the archive value, or for days after the archive the last live data point of that day.
- `basis` is `native` when CoinGecko prices DGB in the currency, `convertedFromUsd` when the server converted the USD price at each day's rate, and `peg` for `asset=dd`.
- For converted currencies, `price = USD price that day × rate that day`, where the rate is the most recent published rate up to 7 days old (weekends and holidays).
- Days before DGB price data begins, days before a converted currency's rate history begins, and any day missing from both archive and live source are **absent**. The client treats them as unknown.

## Errors

| Status | When | Client behaviour |
|---|---|---|
| `400` | Bad parameters | Treat as no prices (should not happen from the wallet) |
| `429` / `5xx` / timeout | Upstream limit or failure and no archived day in range | Export continues with empty Price/Value cells; the sheet says how many rows have no value (FR-007) |

## Server behaviour

- **Rates for converted currencies and DD.** `PriceArchive/usd-fx-{currency}.csv` (lines `yyyy-MM-dd,rate`, units per 1 USD) is used the same way: archived days first, then one live call (Frankfurter, or `exchange-api` for currencies the ECB doesn't publish) for days after the archive.
- **Archive first.** `PriceArchive/dgb-daily-{currency}.csv` (lines `yyyy-MM-dd,price`, ascending, one per UTC day) is loaded once per process. Days in `[from, to]` that the archive has are taken from it. Archive values win over live values for the same day.
- **Live only for newer days.** Only when `to` is after the archive's last date does the server make the upstream call below, for those days only. When `to` is not after the archive's last date, no upstream call is made at all.
- If the upstream call fails, the archived days are still returned with `200`; the error statuses below apply only when nothing at all could be returned.
- The live upstream call: `GET https://api.coingecko.com/api/v3/coins/digibyte/market_chart/range?vs_currency={currency}&from={unix}&to={unix}` through the existing `CoinGeckoProxy` HTTP client.
- Cache key `price:range:{currency}:{from}:{to}`, lifetime 1 hour.
- Logs carry no query values beyond status codes (Principle II, secret and privacy hygiene).
- Rate-limited by the API's existing limiter.

## Client (`DigiByte.Wallet`)

```csharp
Task<DailyPriceSeries> GetDailyPricesAsync(string fiatCurrency, DateOnly from, DateOnly to, CancellationToken ct);
```

- With a price proxy configured, calls the endpoint above. Without one (developer direct mode only), calls the CoinGecko URL directly and reduces to daily with the same shared helper. Direct mode has no archive, so days older than 365 days stay empty there.

## Archive refresh (scheduled)

`.github/workflows/price-archive-refresh.yml`, monthly and on manual dispatch:

1. For each `PriceArchive/dgb-daily-{currency}.csv`, call `coins/digibyte/market_chart?vs_currency={currency}&days=365&interval=daily`. For each `PriceArchive/usd-fx-{currency}.csv`, read the missing days from that file's recorded source.
2. Append only days strictly after the file's last date, excluding today (an unfinished day).
3. Open one pull request with all changed files. It is never merged automatically.
4. Fail the run if any file's last date is more than 300 days old after the update.
- Requests the person's currency as is; the server does any conversion. `Basis` comes from the response's `basis`.
- In direct mode only native currencies get values; converted currencies get an empty series there.
- Never throws for upstream failure; returns an empty series with `Failed = true`.
