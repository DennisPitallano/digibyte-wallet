using DigiByte.Crypto.KeyGeneration;

namespace DigiByte.Wallet.Models;

// DigiDollar (DD) models, per specs/001-digidollar-wallet/data-model.md.
// All amounts are integers: DD in USD CENTS (long), DGB in satoshis (long). Display conversion happens in the UI only.
// None of these types may hold a private key, seed, mnemonic, or WIF (FR-008, Constitution I).

/// <summary>Network a DigiDollar account lives on. Regtest is not supported (FR-029).</summary>
public enum DdNetwork
{
    Mainnet,
    Testnet,
}

public enum DdDirection
{
    Received,
    Sent,

    /// <summary>Every DD output went to one of the user's own addresses.</summary>
    SelfTransfer,
}

public enum DdTxStatus
{
    Pending,
    Confirmed,
    Failed,
}

/// <summary>
/// Persisted, per seed-based wallet. No keys are stored: keys are re-derived from the decrypted seed in memory.
/// </summary>
public sealed record DigiDollarAccount(
    string WalletId,
    DdNetwork Network,
    /// <summary>&gt;= 0; incremented when the user asks for a new address (FR-005).</summary>
    int NextReceiveIndex,
    /// <summary>&gt;= 0; incremented when a send creates DD change.</summary>
    int NextChangeIndex,
    /// <summary>False until the first restore scan finishes (FR-007, research R12); a retry runs on next open.</summary>
    bool ScanComplete,
    DateTimeOffset? LastScanAt,
    DateTimeOffset CreatedAt)
{
    public static DigiDollarAccount Create(string walletId, DdNetwork network, DateTimeOffset now) =>
        new(walletId, network, 0, 0, false, null, now);
}

/// <summary>
/// Persisted. Uniqueness is on (WalletId, Chain, Index) and Address is unique per wallet.
/// Address is valid per <c>DigiDollarAddressValidator</c> for the account network (FR-006).
/// </summary>
public sealed record DigiDollarAddress
{
    public const int MaxLabelLength = 64;

    private readonly string? _label;

    public required string WalletId { get; init; }

    public required string Address { get; init; }

    /// <summary>Change addresses are hidden from the Receive list.</summary>
    public required DigiDollarChain Chain { get; init; }

    /// <summary>Derivation index on <c>m/86'/{coin}'/0'/{chain}/{index}</c>.</summary>
    public required int Index { get; init; }

    /// <summary>Optional, at most 64 characters.</summary>
    public string? Label
    {
        get => _label;
        init
        {
            if (value is { Length: > MaxLabelLength })
                throw new ArgumentException($"Label must be at most {MaxLabelLength} characters.", nameof(Label));
            _label = value;
        }
    }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Derived in memory from the NodeApi: one unspent DD output.</summary>
public sealed record DigiDollarUtxo(
    string Txid,
    int Vout,
    /// <summary>Owner DD address.</summary>
    string Address,
    /// <summary>&gt; 0.</summary>
    long AmountCents,
    /// <summary><c>5120</c> followed by the output key; required for the Taproot sighash (research R4).</summary>
    string ScriptPubKey,
    /// <summary>0 for DD outputs; required for the sighash.</summary>
    long ValueSats,
    int Confirmations,
    /// <summary>A confirmed output that an unconfirmed transaction already spends: not spendable, counted as pending outgoing.</summary>
    bool SpentByMempool = false)
{
    /// <summary>Only confirmed (at least 1 confirmation) DD UTXOs that no pending transaction spends can be spent (research R1).</summary>
    public bool IsSpendable => Confirmations >= 1 && !SpentByMempool;
}

/// <summary>Derived in memory. Pending may be negative while a send is outstanding.</summary>
public sealed record DigiDollarBalance(
    /// <summary>Sum of DD UTXOs with at least 1 confirmation across all the wallet's addresses.</summary>
    long ConfirmedCents,
    /// <summary>Unconfirmed incoming DD minus unconfirmed outgoing DD.</summary>
    long PendingCents,
    IReadOnlyDictionary<string, long> PerAddress,
    /// <summary>Node tip height at fetch time.</summary>
    int AsOfHeight);

/// <summary>Derived plus the local pending overlay.</summary>
public sealed record DigiDollarTransaction(
    string Txid,
    DdDirection Direction,
    /// <summary>First non-own DD output for Sent; the first sender DD input address for Received when known.</summary>
    string? CounterpartyAddress,
    /// <summary>Net DD to or from the user, in cents.</summary>
    long AmountCents,
    /// <summary>Only for Sent.</summary>
    long? FeeSats,
    int Confirmations,
    /// <summary>Block time, or local submit time while pending.</summary>
    DateTimeOffset Timestamp,
    DdTxStatus Status);

/// <summary>Persisted (encrypted): last-known balance and recent history, shown only while the backend is unreachable.</summary>
public sealed record DigiDollarCache(
    DigiDollarBalance? Balance,
    /// <summary>The last 50 entries.</summary>
    IReadOnlyList<DigiDollarTransaction> RecentHistory,
    /// <summary>Displayed as "cached, as of …".</summary>
    DateTimeOffset AsOf)
{
    public const int MaxHistory = 50;
}

/// <summary>A spent outpoint reserved by a pending send.</summary>
public sealed record DdOutpoint(string Txid, int Vout);

/// <summary>
/// Persisted (encrypted) so a pending send survives reloads (FR-017, research R13). Removed after 6
/// confirmations, or once marked Failed and the user has been notified.
/// </summary>
public sealed record PendingDdSend
{
    /// <summary>Returned by broadcast.</summary>
    public required string Txid { get; init; }

    /// <summary>Kept for rebroadcast; contains no secrets.</summary>
    public required string RawTxHex { get; init; }

    public required long AmountCents { get; init; }

    public required long FeeSats { get; init; }

    public required string Recipient { get; init; }

    /// <summary>DD and DGB inputs reserved by this send; excluded from available balance until Failed or confirmed.</summary>
    public required IReadOnlyList<DdOutpoint> SpentOutpoints { get; init; }

    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>Set when the tx status is unknown; cleared if seen again; not advanced while the backend is unreachable.</summary>
    public DateTimeOffset? FirstMissingAt { get; init; }

    /// <summary>One automatic rebroadcast on the first time the tx is seen as unknown.</summary>
    public bool Rebroadcasted { get; init; }

    /// <summary>When the backend last answered a status check for this send.</summary>
    public DateTimeOffset? LastCheckAt { get; init; }

    /// <summary>
    /// How long the send has been OBSERVED absent from both mempool and chain (research R13). Each successful "unknown"
    /// check adds the time since the previous check, capped at 2 minutes, so outages and a closed app never count as absence.
    /// Reaching 30 minutes marks the send Failed.
    /// </summary>
    public TimeSpan MissingObserved { get; init; }

    public DdTxStatus Status { get; init; } = DdTxStatus.Pending;
}

/// <summary>Everything the wallet persists about DigiDollar for one wallet, stored as one encrypted blob.</summary>
public sealed record DigiDollarState
{
    public required DigiDollarAccount Account { get; init; }

    public IReadOnlyList<DigiDollarAddress> Addresses { get; init; } = [];

    public DigiDollarCache? Cache { get; init; }

    public IReadOnlyList<PendingDdSend> PendingSends { get; init; } = [];
}

/// <summary>
/// Derived in memory from NodeApi <c>/health</c>. <see cref="IsDegraded"/> drives the FR-027 confirm-dialog warning.
/// </summary>
public sealed record NetworkHealth
{
    public const int OracleStaleBlocks = 20;
    public const int NodeLagBlocks = 6;

    /// <summary>False on NodeApi 503, timeout, or <c>enabled:false</c> (FR-003); disables Send and switches the UI to cached data.</summary>
    public bool Available { get; init; }

    /// <summary>From <c>/health.deploymentActive</c>; false means the NotActive UI state (FR-003).</summary>
    public bool DeploymentActive { get; init; } = true;

    /// <summary>USD per DGB times 10^6.</summary>
    public long? OraclePriceMicroUsd { get; init; }

    public int? OracleHeight { get; init; }

    public int NodeHeight { get; init; }

    /// <summary>Best-known height from peers.</summary>
    public int? NetworkHeight { get; init; }

    public bool ErrActive { get; init; }

    public bool VolatilityFreeze { get; init; }

    /// <summary>False when there is no usable price; hides the DGB USD equivalent (FR-019).</summary>
    public bool OraclePriceAvailable => OraclePriceMicroUsd is > 0;

    /// <summary><c>NodeHeight − OracleHeight &gt; 20</c> (research R6).</summary>
    public bool IsOracleStale => OracleHeight is { } h && NodeHeight - h > OracleStaleBlocks;

    /// <summary><c>NetworkHeight − NodeHeight &gt; 6</c> (research R6).</summary>
    public bool IsNodeLagging => NetworkHeight is { } n && n - NodeHeight > NodeLagBlocks;

    /// <summary>ERR active, volatility freeze, stale oracle, or no price at all (FR-027).</summary>
    public bool IsDegraded => ErrActive || VolatilityFreeze || IsOracleStale || !OraclePriceAvailable;
}
