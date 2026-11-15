using System.Text.Json.Serialization;

namespace DigiByte.Wallet.Models;

/// <summary>Where a wallet birth height came from (data-model: WalletBirthHeight). Values as written in the data model.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BirthOrigin>))]
public enum BirthOrigin
{
    [JsonStringEnumMemberName("recorded")]
    Recorded,

    [JsonStringEnumMemberName("estimated-from-date")]
    EstimatedFromDate,

    [JsonStringEnumMemberName("unknown")]
    Unknown,
}

/// <summary>The block height at which a wallet started, so a restore does not have to read the chain before it (spec FR-008a).</summary>
/// <param name="Height">The height, or null when it is not known.</param>
/// <param name="Origin">How it was learned, shown to the user on restore.</param>
public sealed record WalletBirthHeight(int? Height, BirthOrigin Origin);

[JsonConverter(typeof(JsonStringEnumConverter<ScanMode>))]
public enum ScanMode
{
    [JsonStringEnumMemberName("normal")]
    Normal,

    [JsonStringEnumMemberName("deep")]
    Deep,
}

[JsonConverter(typeof(JsonStringEnumConverter<ScanSource>))]
public enum ScanSource
{
    [JsonStringEnumMemberName("gateway")]
    Gateway,

    [JsonStringEnumMemberName("explorer")]
    Explorer,
}

/// <summary>
/// Where a restore scan has got to (data-model: ScanProgress). Device-local, never sent to the gateway. <see cref="NextHeight"/> moves only
/// after a whole chunk of blocks has been processed, so an interrupted restore resumes at the first chunk not finished. The transactions
/// found so far are kept too (beyond the data model), otherwise resuming would lose what earlier chunks had already found.
/// </summary>
public sealed record ScanProgress(
    string WalletId,
    int StartHeight,
    int NextHeight,
    int ReceiveHighestUsed,
    int ChangeHighestUsed,
    ScanMode Mode,
    ScanSource Source,
    IReadOnlyList<SyncTransaction> Transactions);
