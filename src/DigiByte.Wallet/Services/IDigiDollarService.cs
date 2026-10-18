using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>What the DigiDollar pages should render (contracts/wallet-ui-contract.md, "UI states per page").</summary>
public enum DdViewState
{
    /// <summary>Not a seed-based wallet, no wallet unlocked, or an unsupported network (FR-029, FR-030).</summary>
    NotSupported,

    /// <summary>Backend unreachable and nothing cached.</summary>
    Unavailable,

    /// <summary>Backend unreachable: last cached balance and history, labeled with their age (FR-003).</summary>
    UnavailableCached,

    /// <summary>DigiDollar is not yet activated on the connected network (FR-003).</summary>
    NotActive,

    /// <summary>Scan complete and no DigiDollar yet: the page shows a call to action to receive.</summary>
    Empty,

    Ready,
}

/// <summary>Restore-scan progress (research R12): how many derived addresses were checked and how many had activity.</summary>
public readonly record struct DdScanProgress(int AddressesChecked, int ActiveFound);

public sealed record DdDashboard(
    DdViewState State,
    NetworkHealth Health,
    DigiDollarBalance? Balance,
    IReadOnlyList<DigiDollarTransaction> History,
    /// <summary>The user's receive addresses (change addresses are hidden).</summary>
    IReadOnlyList<DigiDollarAddress> Addresses,
    /// <summary>Confirmed DD that no pending transaction spends or reserves: what a send may use.</summary>
    long AvailableCents,
    /// <summary>True when Balance and History are the last cached values rather than live ones.</summary>
    bool Cached,
    DateTimeOffset? CachedAsOf,
    bool ScanComplete,
    /// <summary>Sends that were marked Failed during this refresh (absent 30 minutes): tell the user once (FR-017).</summary>
    IReadOnlyList<PendingDdSend> NewlyFailedSends);

/// <summary>Why a send cannot go ahead (or null/None when it can). Each maps to an inline message (wallet-ui-contract).</summary>
public enum DdSendIssue
{
    None,
    NotSupported,
    NotActive,
    Offline,
    NotDdAddress,
    WrongNetwork,
    BelowMinimum,
    AboveMaximum,
    InsufficientDd,
    ChangeBelowMinimum,
    ChangeAboveMaximum,
    InsufficientDgbFee,
    RateLimited,
    /// <summary>The node refused the transaction; <see cref="DdSendException.Detail"/> carries its reason.</summary>
    Rejected,
}

/// <summary>An unusual network condition the confirmation dialog must name before the user signs (FR-027).</summary>
public enum DdDegradedCondition
{
    OracleStale,
    Err,
    VolatilityFreeze,
    PriceUnavailable,
}

/// <summary>The outcome of validating a send and quoting its fee: everything the confirmation dialog shows (FR-014, FR-015).</summary>
public sealed record DdSendCheck(
    DdSendIssue Issue,
    /// <summary>What a send may use right now (also the "Send all" amount).</summary>
    long AvailableCents,
    long FeeSats,
    long ChangeCents,
    /// <summary>The recipient is one of the user's own addresses (the dialog notes it).</summary>
    bool IsOwnAddress,
    IReadOnlyList<DdDegradedCondition> Degraded)
{
    public bool IsOk => Issue == DdSendIssue.None;
}

public sealed record DdSendResult(string Txid, long AmountCents, long FeeSats, long ChangeCents);

/// <summary>A send that did not happen. Nothing was broadcast unless the node accepted it.</summary>
public sealed class DdSendException : Exception
{
    public DdSendException(DdSendIssue issue, string? detail = null) : base($"{issue}{(detail is null ? "" : ": " + detail)}")
    {
        Issue = issue;
        Detail = detail;
    }

    public DdSendIssue Issue { get; }

    public string? Detail { get; }
}

/// <summary>
/// Non-custodial DigiDollar hold, receive, and (later) send. Keys are derived from the unlocked wallet's seed in
/// memory; the backend only ever sees public addresses (FR-026, Constitution I and II).
/// </summary>
public interface IDigiDollarService
{
    /// <summary>
    /// Load or refresh everything: runs the restore scan when it has not completed (FR-007), then balance,
    /// history, and network health. Unreachable backend and inactive network are states, not exceptions.
    /// </summary>
    Task<DdDashboard> RefreshAsync(CancellationToken ct = default, IProgress<DdScanProgress>? progress = null);

    /// <summary>Derive and remember the next receive address. Purely local: works while the backend is down (FR-005).</summary>
    Task<DigiDollarAddress> NewReceiveAddressAsync(CancellationToken ct = default);

    /// <summary>The user's receive addresses from local state, oldest first (FR-007).</summary>
    Task<IReadOnlyList<DigiDollarAddress>> GetReceiveAddressesAsync(CancellationToken ct = default);

    /// <summary>
    /// Validate a send exactly as the final send will (address, limits, balance, change minimum, DGB fee, rate limit,
    /// reachability) and quote its fee and change. Builds and discards an unsent transaction; nothing leaves the device.
    /// </summary>
    Task<DdSendCheck> CheckSendAsync(string recipient, long amountCents, CancellationToken ct = default);

    /// <summary>
    /// Revalidate, then build, sign on this device, and broadcast. Records a Pending entry and reserves its inputs.
    /// Throws <see cref="DdSendException"/> when the send cannot go ahead.
    /// </summary>
    Task<DdSendResult> SendAsync(string recipient, long amountCents, CancellationToken ct = default);

    /// <summary>The entire spendable balance, for the "Send all" action (no change output).</summary>
    Task<long> GetSendAllAmountAsync(CancellationToken ct = default);

    /// <summary>Remove a Failed send once the user has been told about it.</summary>
    Task AcknowledgeFailedSendAsync(string txid, CancellationToken ct = default);

    /// <summary>Forget session-scoped state (the send rate limit) when the wallet locks or changes.</summary>
    void ResetSession();
}
