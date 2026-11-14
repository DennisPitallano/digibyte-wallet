using System.Text.Json.Serialization;

namespace DigiByte.Wallet.Models;

/// <summary>Where the wallet got a transaction record from (data-model: WalletSyncState).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SyncOrigin
{
    /// <summary>A gateway proof for it was checked against the wallet own headers. The raw transaction is kept.</summary>
    GatewayProven,

    /// <summary>It came from an explorer and has not been proven yet.</summary>
    ExplorerSourced,
}

/// <summary>One transaction the wallet knows about.</summary>
/// <param name="Txid">The transaction id.</param>
/// <param name="Height">The height of its block, 0 while it is not in a block.</param>
/// <param name="BlockHash">The hash of that block, empty while it is not in a block.</param>
/// <param name="Raw">The serialized transaction as hex. Required for gateway-proven entries, optional for explorer-sourced ones.</param>
/// <param name="Origin">Where the record came from.</param>
/// <param name="Disputed">The gateway contradicts what the explorer reported about it.</param>
/// <param name="IsOwnSend">The wallet signed and sent it.</param>
public sealed record SyncTransaction(string Txid, int Height, string BlockHash, string? Raw, SyncOrigin Origin, bool Disputed = false, bool IsOwnSend = false)
{
    public bool IsConfirmed => Height > 0 && BlockHash.Length == 64;
}

/// <summary>An output of the wallet that is not spent as far as the state knows. Sent to the gateway only as <c>txid:vout</c>.</summary>
public sealed record SyncOutpoint(string Txid, int Vout, long Amount, string ScriptPubKey, bool IsChangeToSelf = false)
{
    public string Key => $"{Txid}:{Vout}";
}

/// <summary>
/// The wallet own record that makes a routine refresh incremental (spec FR-009a). One per wallet, on the device only, never sent to the
/// gateway, never part of an export or backup.
/// </summary>
/// <param name="WalletId">The local wallet id.</param>
/// <param name="LastSyncedHeight">The height the last completed refresh reached, minus the safety margin used at bootstrap.</param>
/// <param name="LastSyncedHash">The hash of the block at that height in the wallet verified chain.</param>
/// <param name="Transactions">Every transaction the wallet knows.</param>
/// <param name="UnspentOutpoints">The wallet unspent outputs, sent as <c>knownOutpoints</c>.</param>
/// <param name="UpdatedAt">Informational.</param>
public sealed record WalletSyncState(
    string WalletId,
    int LastSyncedHeight,
    string LastSyncedHash,
    IReadOnlyList<SyncTransaction> Transactions,
    IReadOnlyList<SyncOutpoint> UnspentOutpoints,
    DateTimeOffset UpdatedAt);
