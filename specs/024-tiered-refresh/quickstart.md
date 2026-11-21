# Quickstart: Fewer Requests per Refresh and per Send

How to check the feature works. Details are in [contracts/tiered-refresh.md](contracts/tiered-refresh.md) and [data-model.md](data-model.md).

## 1. Automated tests

```powershell
dotnet test tests/DigiByte.Wallet.Tests --filter "FullyQualifiedName~HotAddressSelector|FullyQualifiedName~SendCoinPlanner|FullyQualifiedName~TieredRefresh|FullyQualifiedName~HistorySync"
```

| Scenario | Expected |
|----------|----------|
| Quiet refresh, 60 addresses, 2 funded, after a full check, 31 s later | hot addresses + the cold share for 31 s + at most 1 tip (≤ 15), no history pages |
| 10 simulated minutes at 15 s and at 60 s polls | requests ≥ 60% fewer than with tiering off |
| Payment to the address on show | shown on the next refresh |
| Payment to a cold address | shown within 5 simulated minutes |
| Manual refresh (`RequestFullCheck`) | every address read |
| A hot summary read fails | refresh fails, saved summaries unchanged |
| Send with 2 funded of 60 | ≤ 4 coin reads; same transaction as before |
| Summary under-reports funds at send | the rest are read before "Insufficient funds" |
| Fewer than 10 watched addresses, or either switch off | every address read, as in 0.7.0 |

## 2. Live check on `/dev/node` (mainnet, unlocked seed wallet)

1. Leave **Change-only history sync** and **Tiered refresh** on.
2. Run the wallet refresh benchmark. Expect "Tiered on, quiet" ≈ hot count + about 6 cold + at most 1 tip read (≤ 15), and "Tiered off, quiet" ≈ the watched address count.
3. Read "Coins read by a send": expect the number of funded addresses.
4. Record the numbers in the PR as proof of SC-001 and SC-004.

## 3. In the app

1. On Home, open the wallet info panel: it shows "Fully checked N min ago", at most 5 minutes after any refresh.
2. Pull to refresh on Home or tap refresh: the note resets to "just now".
3. Send a small amount: the balance refreshes at once, as before.
