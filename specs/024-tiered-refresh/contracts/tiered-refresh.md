# Contract: Tiered Refresh and Send Coin Planning

Internal contracts in `DigiByte.Wallet`, as C# outlines. Bodies belong to implementation.

## 1. `HotAddressSelector` (new, pure)

```csharp
public static class HotAddressSelector
{
    public const int MinAddressesToTier = 10;
    public const int HandedOutKept = 3;
    public static readonly TimeSpan RecentActivityWindow = TimeSpan.FromDays(7);

    /// Hot = funded or pending (from summaries) ∪ on-show ∪ handed-out ∪ next change ∪ pending-send inputs ∪ recent activity.
    /// Fewer than MinAddressesToTier watched → every address is hot.
    public static HotSet Select(HotAddressInputs inputs);
}
```

## 2. `WalletHistorySync` (changed)

```csharp
/// Unchanged signature. New behaviour when the caller passes hints and tiering applies:
/// hot addresses reuse snapshot entries younger than 30 s; cold ones younger than 5 min,
/// except the spread share (oldest first) and any 5 min or older, which are read.
Task<SyncResult> RefreshAsync(string walletId, string network, IReadOnlyList<string> addresses,
    HotAddressHints? hints = null, CancellationToken ct = default);

public static readonly TimeSpan ColdCheckInterval = TimeSpan.FromMinutes(5);

/// Oldest snapshot stamp across the addresses, or null if any is missing (FR-008).
DateTimeOffset? GetFullyCheckedAt(string walletId, string network, IReadOnlyList<string> addresses);

/// Summaries for the addresses if every one was read live this session within ColdCheckInterval (5 min), whether hot or cold;
/// otherwise null. For send planning only: coins are still read fresh, and an under-reporting summary is caught by the fallback.
IReadOnlyDictionary<string, AddressSummary>? TryGetSendSummaries(string walletId, string network, IReadOnlyList<string> addresses);

/// Diagnostic for the /dev/node benchmark: moves every snapshot stamp of the wallet back by `by`.
void AgeSnapshot(string walletId, TimeSpan by);
```

`HotAddressHints` carries `OnShow`, `HandedOut`, `NextChange` and `PendingSendInputs` from `WalletService`; the sync adds the summaries and recent activity it already holds and calls `HotAddressSelector.Select`.

Rules:
- Tiering applies only when `HistorySyncOptions.Tiered` and `ChangeOnly` are both on and `hints` is not null; otherwise the 0.7.0 rule (30 s for every address).
- Any failed summary read fails the refresh, as today.
- The balance is the sum of the summaries used for every watched address.

## 3. `SendCoinPlanner` (new, pure)

```csharp
public static class SendCoinPlanner
{
    public const long FeeAllowanceSatoshis = 1_000_000; // 0.01 DGB

    /// Funded = current summary shows a confirmed balance or pending activity; Rest = the others.
    /// summaries == null → Planned = false, Funded = every address.
    public static SendCoinPlan Plan(IReadOnlyList<string> addresses, IReadOnlyDictionary<string, AddressSummary>? summaries);

    /// True when the coins found are not enough and Rest must be read before continuing.
    public static bool NeedsRest(SendCoinPlan plan, long foundSatoshis, long amountSatoshis);

    /// Consolidate spends every coin: true when the coins found total less than the summaries' confirmed sum, so Rest must be read.
    public static bool NeedsRestForConsolidate(SendCoinPlan plan, long foundSatoshis, long summariesConfirmedSatoshis);
}
```

## 4. `WalletService` (changed)

| Member | Change |
|--------|--------|
| `GetBalanceAsync()` / `GetTransactionHistoryAsync()` | pass `HotAddressHints` to `RefreshAsync` |
| `RequestFullCheck()` (new) | drops the active wallet's snapshot so the next refresh reads every address (FR-006) |
| `GetFullyCheckedAt()` (new) | forwards to the sync for the active wallet; null when unknown |
| `SendAsync`, `SendBatchAsync`, `ConsolidateUtxosAsync` | read coins for `plan.Funded`, then `plan.Rest` when `NeedsRest`, then continue as today |
| `PlanSendCoinReads()` (new, diagnostic) | the number of coin reads a send would make now, without reading |

## 5. Web

- Home `ManualRefreshAsync` and Activity's pull to refresh call `WalletService.RequestFullCheck()` first.
- Home's wallet info panel shows "Fully checked N min ago" from `GetFullyCheckedAt()`.
- `/dev/node`: a "Tiered refresh" switch (`dev:tiered-refresh`), and benchmark rows "Tiered on, quiet" and "Tiered off, quiet" (full check, `AgeSnapshot(31 s)`, one refresh) plus "Coins read by a send".
