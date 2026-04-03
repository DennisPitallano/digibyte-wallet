using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Manages payment requests / invoices stored locally.
/// </summary>
public class PaymentRequestService
{
    private readonly ISecureStorage _storage;
    private const string RequestsKey = "payment_requests";
    private List<PaymentRequest>? _cache;

    public PaymentRequestService(ISecureStorage storage)
    {
        _storage = storage;
    }

    public async Task<List<PaymentRequest>> GetAllAsync()
    {
        if (_cache != null) return _cache;
        var json = await _storage.GetAsync(RequestsKey);
        _cache = json != null
            ? JsonSerializer.Deserialize<List<PaymentRequest>>(json) ?? []
            : [];
        return _cache;
    }

    public async Task<PaymentRequest> CreateAsync(string address, decimal? amountDgb, string? label, string? message)
    {
        var request = new PaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Address = address,
            AmountDgb = amountDgb,
            Label = label,
            Message = message,
        };

        var all = await GetAllAsync();
        all.Insert(0, request);
        await SaveAsync(all);
        return request;
    }

    public async Task DeleteAsync(string id)
    {
        var all = await GetAllAsync();
        all.RemoveAll(r => r.Id == id);
        await SaveAsync(all);
    }

    private async Task SaveAsync(List<PaymentRequest> requests)
    {
        _cache = requests;
        var json = JsonSerializer.Serialize(requests);
        await _storage.SetAsync(RequestsKey, json);
    }
}
