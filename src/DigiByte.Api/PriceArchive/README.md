# Price archive

Daily DGB prices and daily USD exchange rates for the wallet's CSV export (spec 031, research R3). The price service
answers `/api/price/range` from these files first and asks the live sources only for days after them, so an export
of any age gets a value for every day the sources ever covered.

- `dgb-daily-{currency}.csv`: the DGB price on each UTC day (closing price), one file per currency CoinGecko prices DGB in.
- `usd-fx-{currency}.csv`: units of the currency per 1 USD on each published day. It is used for currencies converted from
  USD, and for DigiDollar (1 DD = 1 USD) outside USD.
- Lines are `yyyy-MM-dd,value`, ascending. A malformed line fails the price service's tests (`PriceArchiveTests`).

The files hold public market data only, nothing about any wallet. They change only through reviewed pull requests.

## Seeding (one time, maintainer)

This folder ships empty until it is seeded. Until then, `/api/price/range` serves the last 365 days from the live sources
only, and older export rows have an empty value. The wallet counts those rows and tells the person.

1. **DGB prices.** For each CoinGecko-native currency in `FiatCurrencies`, download CoinGecko's DGB "Historical data" CSV
   (coingecko.com → DigiByte → Historical Data → Export), then run:

   ```
   dotnet run --project tools/PriceArchiveImport -- import-coingecko <export.csv> src/DigiByte.Api/PriceArchive <currency>
   ```

2. **USD rates.** For each converted currency, and each native one when DigiDollar exports should have values in it, run:

   ```
   dotnet run --project tools/PriceArchiveImport -- seed-rates src/DigiByte.Api/PriceArchive <currency> 2014-01-01
   ```

   European Central Bank currencies (via Frankfurter) go back to 1999. Other currencies use the free `exchange-api` daily
   snapshots, which start in 2024. Days before a source begins stay absent.

3. Record below which files were seeded, from which source, when, and their first date. Then open a pull request.

## Refresh

`.github/workflows/price-archive-refresh.yml` runs monthly. It appends new days and opens one pull request for review.

## Seed log

| File | Source | Seeded on | First date |
|---|---|---|---|
| _(not seeded yet)_ | | | |
