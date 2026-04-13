using System.Text.Json;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Manages user-defined labels for wallet addresses and external addresses.
/// Stored per-wallet in IndexedDB.
/// </summary>
public class AddressLabelService
{
    private readonly ISecureStorage _storage;
    private Dictionary<string, AddressLabel>? _cache;
    private string? _cachedWalletId;

    public AddressLabelService(ISecureStorage storage)
    {
        _storage = storage;
    }

    private string StorageKey(string walletId) => $"address-labels:{walletId}";

    public async Task<Dictionary<string, AddressLabel>> GetAllAsync(string walletId)
    {
        if (_cache != null && _cachedWalletId == walletId) return _cache;
        var json = await _storage.GetAsync(StorageKey(walletId));
        _cache = json != null
            ? JsonSerializer.Deserialize<Dictionary<string, AddressLabel>>(json) ?? new()
            : new();
        _cachedWalletId = walletId;
        return _cache;
    }

    public async Task<string?> GetLabelAsync(string walletId, string address)
    {
        var labels = await GetAllAsync(walletId);
        return labels.TryGetValue(address, out var label) ? label.Label : null;
    }

    public async Task SetLabelAsync(string walletId, string address, string label)
    {
        var labels = await GetAllAsync(walletId);
        if (string.IsNullOrWhiteSpace(label))
        {
            labels.Remove(address);
        }
        else
        {
            labels[address] = new AddressLabel
            {
                Label = label.Trim(),
                UpdatedAt = DateTime.UtcNow,
            };
        }
        await SaveAsync(walletId, labels);
    }

    public async Task RemoveLabelAsync(string walletId, string address)
    {
        var labels = await GetAllAsync(walletId);
        if (labels.Remove(address))
            await SaveAsync(walletId, labels);
    }

    public async Task<List<(string Address, string Label)>> SearchAsync(string walletId, string query)
    {
        var labels = await GetAllAsync(walletId);
        if (string.IsNullOrWhiteSpace(query))
            return labels.Select(kvp => (kvp.Key, kvp.Value.Label)).ToList();

        var q = query.ToLower();
        return labels
            .Where(kvp => kvp.Value.Label.ToLower().Contains(q) || kvp.Key.ToLower().Contains(q))
            .Select(kvp => (kvp.Key, kvp.Value.Label))
            .ToList();
    }

    private async Task SaveAsync(string walletId, Dictionary<string, AddressLabel> labels)
    {
        _cache = labels;
        _cachedWalletId = walletId;
        var json = JsonSerializer.Serialize(labels);
        await _storage.SetAsync(StorageKey(walletId), json);
    }
}

public class AddressLabel
{
    public required string Label { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
