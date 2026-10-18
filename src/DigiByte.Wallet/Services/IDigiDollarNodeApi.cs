using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>The backend is unreachable, timed out, answered 5xx, or returned something unusable (FR-003).</summary>
public sealed class DdBackendUnavailableException : Exception
{
    public DdBackendUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The backend answered with a client error (400/413) or rejected a broadcast.</summary>
public sealed class DdApiException : Exception
{
    public DdApiException(string code, int statusCode, string? detail = null) : base($"{code} ({statusCode})")
    {
        Code = code;
        StatusCode = statusCode;
        Detail = detail;
    }

    /// <summary>The server's error code (for example <c>invalid_address</c>) or, for a broadcast, the node's reject reason.</summary>
    public string Code { get; }

    public int StatusCode { get; }

    public string? Detail { get; }
}

public readonly record struct DdAmountAt(string Address, long AmountCents);

public sealed record DdHistoryEntry(
    string Txid,
    int? BlockHeight,
    DateTimeOffset? BlockTime,
    int Confirmations,
    long? FeeSats,
    IReadOnlyList<DdAmountAt> DdInputs,
    IReadOnlyList<DdAmountAt> DdOutputs);

public sealed record DdHistoryResult(IReadOnlyList<DdHistoryEntry> Items, string? Next);

public enum DdTxState
{
    Unknown,
    Mempool,
    Confirmed,
}

public readonly record struct DdTxStatusResult(DdTxState State, int Confirmations);

/// <summary>
/// The wallet's view of the NodeApi DigiDollar read surface (contracts/nodeapi-digidollar-read.md).
/// Public chain data only; the wallet never calls a wallet-scoped endpoint, and addresses travel only in POST bodies.
/// </summary>
public interface IDigiDollarNodeApi
{
    /// <summary>Never throws for an unreachable backend: returns <c>Available = false</c> (FR-003).</summary>
    Task<NetworkHealth> GetHealthAsync(CancellationToken ct);

    /// <summary>Unspent DD outputs per address (every requested address has an entry).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DigiDollarUtxo>>> GetUtxosAsync(
        IReadOnlyCollection<string> addresses, int minConf, CancellationToken ct);

    Task<DdHistoryResult> GetHistoryAsync(
        IReadOnlyCollection<string> addresses, int count, string? before, CancellationToken ct);

    Task<DdTxStatusResult> GetTxStatusAsync(string txid, CancellationToken ct);

    /// <summary>Broadcast a signed raw transaction through the shared endpoint; returns the txid.</summary>
    Task<string> BroadcastAsync(string rawTxHex, CancellationToken ct);
}
