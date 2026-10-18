using System.Text.RegularExpressions;
using DigiByte.Crypto.Addresses;
using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.DigiDollar;

public sealed record DdAddressesRequest(string[]? Addresses, int? MinConf);

public sealed record DdHistoryRequest(string[]? Addresses, int? Count, string? Before);

/// <summary>
/// Wallet-facing, address-scoped, read-only DigiDollar endpoints (contracts/nodeapi-digidollar-read.md).
/// Addresses are accepted only in POST bodies (never in URL paths or query strings) so they are not written to
/// standard access logs next to a client IP (FR-023). Nothing here touches the node's shared DD wallet.
/// </summary>
public static partial class DigiDollarReadHandlers
{
    public const int MaxAddresses = 200;
    public const int MaxHistoryCount = 200;
    public const int DefaultHistoryCount = 50;

    /// <summary>Beyond this many blocks of index lag the batch endpoints answer 503 (contract).</summary>
    public const int MaxIndexLagBlocks = 6;

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex TxidPattern();

    private static string NetworkName(NodeConfig node) => node.IsTestnet ? "testnet" : "mainnet";

    private static IResult Disabled() =>
        Results.Json(new { error = "digidollar_disabled" }, statusCode: 503);

    private static IResult Error(int status, string code, object? extra = null) =>
        Results.Json(extra is null ? new { error = code } : new { error = code, detail = extra }, statusCode: status);

    private static bool IndexBehind(DdIndexStatus status, out int? lag)
    {
        lag = status.LagBlocks;
        return lag is null || lag > MaxIndexLagBlocks;
    }

    /// <summary>Validate and decode a batch of addresses, de-duplicated, preserving order.</summary>
    private static IResult? DecodeAddresses(string[]? addresses, string network, out List<(string Address, byte[] Key)> decoded)
    {
        decoded = [];
        if (addresses is null || addresses.Length == 0)
            return Error(400, "invalid_request", "addresses is required");
        if (addresses.Length > MaxAddresses)
            return Error(413, "too_many_addresses");

        var invalid = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in addresses)
        {
            if (!DigiDollarAddressValidator.TryDecode(a, network, out _, out var key)) { invalid.Add(a ?? ""); continue; }
            if (seen.Add(a!)) decoded.Add((a!, key));
        }
        return invalid.Count > 0 ? Error(400, "invalid_address", invalid) : null;
    }

    public static async Task<IResult> Utxos(
        DdAddressesRequest? req, DigiDollarConfig config, NodeConfig node, IDdIndex index, DdIndexStatus status, CancellationToken ct)
    {
        if (!config.Enabled) return Disabled();
        if (DecodeAddresses(req?.Addresses, NetworkName(node), out var decoded) is { } bad) return bad;
        if (IndexBehind(status, out var lag)) return Error(503, "indexer_behind", new { lagBlocks = lag });

        var minConf = Math.Clamp(req?.MinConf ?? 0, 0, 1_000_000);
        var utxos = await index.GetUtxosAsync(decoded.Select(d => d.Key).ToList(), minConf, ct);

        var byKey = utxos.GroupBy(u => Convert.ToHexString(u.OutputKey)).ToDictionary(g => g.Key, g => g.ToList());
        var body = new Dictionary<string, object[]>(StringComparer.Ordinal);
        foreach (var (address, key) in decoded)
        {
            body[address] = byKey.TryGetValue(Convert.ToHexString(key), out var list)
                ? list.Select(u => (object)new
                {
                    txid = u.Txid,
                    vout = u.Vout,
                    amountCents = u.AmountCents,
                    scriptPubKey = u.ScriptPubKeyHex,
                    valueSats = u.ValueSats,
                    confirmations = u.Confirmations,
                    spentByMempool = u.SpentByMempool,
                }).ToArray()
                : [];
        }
        return Results.Json(body);
    }

    public static async Task<IResult> History(
        DdHistoryRequest? req, DigiDollarConfig config, NodeConfig node, IDdIndex index, DdIndexStatus status, CancellationToken ct)
    {
        if (!config.Enabled) return Disabled();
        var network = NetworkName(node);
        if (DecodeAddresses(req?.Addresses, network, out var decoded) is { } bad) return bad;
        if (req?.Before is { } before && !TxidPattern().IsMatch(before)) return Error(400, "invalid_cursor");
        if (IndexBehind(status, out var lag)) return Error(503, "indexer_behind", new { lagBlocks = lag });

        var count = Math.Clamp(req?.Count ?? DefaultHistoryCount, 1, MaxHistoryCount);
        var page = await index.GetHistoryAsync(decoded.Select(d => d.Key).ToList(), count, req?.Before, ct);

        object Amount(DdHistoryAmount a) => new
        {
            address = DigiDollarAddressValidator.Encode(a.OutputKey, network),
            amountCents = a.AmountCents,
        };

        return Results.Json(new
        {
            items = page.Items.Select(i => new
            {
                txid = i.Txid,
                blockHeight = i.BlockHeight,
                blockTime = i.BlockTime is { } t ? DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime : (DateTime?)null,
                confirmations = i.Confirmations,
                feeSats = i.FeeSats,
                ddInputs = i.DdInputs.Select(Amount).ToArray(),
                ddOutputs = i.DdOutputs.Select(Amount).ToArray(),
            }).ToArray(),
            next = page.Next,
        });
    }

    public static async Task<IResult> TxStatus(string txid, DigiDollarConfig config, IDdIndex index, CancellationToken ct)
    {
        if (!config.Enabled) return Disabled();
        if (!TxidPattern().IsMatch(txid)) return Error(400, "invalid_txid");

        var s = await index.GetTxStatusAsync(txid.ToLowerInvariant(), ct);
        return Results.Json(new
        {
            status = s.State switch
            {
                ChainTxState.Mempool => "mempool",
                ChainTxState.Confirmed => "confirmed",
                _ => "unknown",
            },
            confirmations = s.Confirmations,
        });
    }
}
