using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DigiByte.Api.Services;

/// <summary>A transaction as the donation check needs it: confirmations and where the money went.</summary>
public sealed record DonationTx(string TxId, int Confirmations, IReadOnlyList<DonationOutput> Outputs);

public sealed record DonationOutput(string Address, long Satoshis);

/// <summary>
/// Looks a transaction up on one public explorer. Unlike the wallet's explorer clients (which turn every failure into "nothing"),
/// this tells a definite "not found" (returns null) apart from an outage (throws), because the donation check must never pass or
/// skip when the explorers cannot be reached.
/// </summary>
public interface IExplorerTxLookup
{
    string Name { get; }

    /// <returns>The transaction, or null when the explorer answered that it does not exist.</returns>
    /// <exception cref="Exception">Any other failure: timeout, server error, unreadable answer.</exception>
    Task<DonationTx?> LookupAsync(string txId, CancellationToken ct);
}

/// <summary>Blockbook explorers (digibyteblockexplorer.com): GET /api/v2/tx/{txid}.</summary>
public sealed class BlockbookTxLookup(HttpClient http, string baseUrl) : IExplorerTxLookup
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    public string Name => "blockbook";

    public async Task<DonationTx?> LookupAsync(string txId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{_baseUrl}/api/v2/tx/{txId}", ct);
        // Blockbook answers 200 with {"error": "..."} when the transaction is unknown, and 404 on some deployments.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var tx = await response.Content.ReadFromJsonAsync<BlockbookTx>(ct)
                 ?? throw new InvalidOperationException("Unreadable explorer answer");
        if (tx.TxId is null)
        {
            if (!string.IsNullOrEmpty(tx.Error)) return null;
            throw new InvalidOperationException("Unreadable explorer answer");
        }
        var outputs = (tx.Vout ?? []).Select(v => new DonationOutput(
            v.Addresses?.FirstOrDefault() ?? "",
            long.TryParse(v.Value, out var sats) ? sats : 0)).ToList();
        return new DonationTx(tx.TxId, tx.Confirmations, outputs);
    }

    private sealed class BlockbookTx
    {
        [JsonPropertyName("txid")] public string? TxId { get; set; }
        [JsonPropertyName("confirmations")] public int Confirmations { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("vout")] public List<Vout>? Vout { get; set; }
    }

    private sealed class Vout
    {
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("addresses")] public List<string>? Addresses { get; set; }
    }
}

/// <summary>Esplora explorers (digiexplorer.info): GET /tx/{txid} and GET /blocks/tip/height.</summary>
public sealed class EsploraTxLookup(HttpClient http, string baseUrl) : IExplorerTxLookup
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    public string Name => "esplora";

    public async Task<DonationTx?> LookupAsync(string txId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{_baseUrl}/tx/{txId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var tx = await response.Content.ReadFromJsonAsync<EsploraTx>(ct)
                 ?? throw new InvalidOperationException("Unreadable explorer answer");
        if (tx.TxId is null) throw new InvalidOperationException("Unreadable explorer answer");

        var confirmations = 0;
        if (tx.Status?.Confirmed == true && tx.Status.BlockHeight > 0)
        {
            var heightText = await http.GetStringAsync($"{_baseUrl}/blocks/tip/height", ct);
            confirmations = int.TryParse(heightText.Trim(), out var tip) && tip >= tx.Status.BlockHeight
                ? tip - tx.Status.BlockHeight + 1
                : 1;
        }
        var outputs = (tx.Vout ?? []).Select(v => new DonationOutput(v.Address ?? "", v.Value)).ToList();
        return new DonationTx(tx.TxId, confirmations, outputs);
    }

    private sealed class EsploraTx
    {
        [JsonPropertyName("txid")] public string? TxId { get; set; }
        [JsonPropertyName("status")] public StatusInfo? Status { get; set; }
        [JsonPropertyName("vout")] public List<Vout>? Vout { get; set; }
    }

    private sealed class StatusInfo
    {
        [JsonPropertyName("confirmed")] public bool Confirmed { get; set; }
        [JsonPropertyName("block_height")] public int BlockHeight { get; set; }
    }

    private sealed class Vout
    {
        [JsonPropertyName("scriptpubkey_address")] public string? Address { get; set; }
        [JsonPropertyName("value")] public long Value { get; set; }
    }
}
