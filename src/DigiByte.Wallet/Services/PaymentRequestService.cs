using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>What a status check found for one request, to be stored (spec 036).</summary>
/// <param name="PaidAnnouncedAt">Set when this check announced the paid moment; null leaves the stored value.</param>
/// <param name="LockNotifiedAt">Set when this check sent the locked notification; null leaves the stored value.</param>
public sealed record RequestUpdate(
    PaymentRequestStatus Status,
    IReadOnlyList<RequestPayment> Payments,
    DateTime CheckedAt,
    DateTime? PaidAnnouncedAt,
    DateTime? LockNotifiedAt);

/// <summary>
/// Payment requests, stored on the device per wallet under <c>payment_requests:{walletId}</c> (spec 036, research R8).
/// Nothing here is sent anywhere (FR-028).
/// </summary>
public class PaymentRequestService
{
    /// <summary>
    /// How requests are stored: property names as the wallet always wrote them (PascalCase), and statuses by name.
    /// Reading still accepts the old numeric status, so requests saved before spec 036 keep loading.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>The device-wide list used before spec 036.</summary>
    public const string LegacyKey = "payment_requests";

    private readonly ISecureStorage _storage;
    private readonly Dictionary<string, List<PaymentRequest>> _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PaymentRequestService(ISecureStorage storage)
    {
        _storage = storage;
    }

    /// <summary>Raised after any request of any wallet is saved, with the wallet id.</summary>
    public event Action<string>? Changed;

    private static string KeyFor(string walletId) => $"{LegacyKey}:{walletId}";

    /// <summary>
    /// The wallet's requests, newest first. When <paramref name="walletAddresses"/> is given, requests in the old
    /// device-wide list whose address is one of them move to this wallet first, as untracked (data-model "Migration").
    /// </summary>
    public async Task<IReadOnlyList<PaymentRequest>> GetAllAsync(string walletId, IReadOnlySet<string>? walletAddresses = null)
    {
        await _gate.WaitAsync();
        try
        {
            var list = await LoadAsync(walletId);
            if (walletAddresses is not null) await MigrateAsync(walletId, list, walletAddresses);
            return list.ToList();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Makes a request on an address the caller already has, without its own address, so it is not tracked.
    /// Used for single-key wallets, whose only address every request shares (FR-006).
    /// </summary>
    public async Task<PaymentRequest> CreateUntrackedAsync(string walletId, string address, decimal? amountDgb, string? label, string? message)
    {
        var request = new PaymentRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Address = address,
            AmountDgb = amountDgb,
            Label = label,
            Message = message,
            Tracked = false,
            Status = PaymentRequestStatus.NotTracked,
        };
        await AddAsync(walletId, request);
        return request;
    }

    /// <summary>Stores a request made elsewhere in this service, newest first.</summary>
    private async Task AddAsync(string walletId, PaymentRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var list = await LoadAsync(walletId);
            list.Insert(0, request);
            await SaveAsync(walletId, list);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(walletId);
    }

    /// <summary>
    /// Stores what a status check found. Announcement times are only ever set, never cleared, so a paid request
    /// is announced once however many checks or tabs run (FR-010).
    /// </summary>
    public async Task ApplyAsync(string walletId, string requestId, RequestUpdate update)
    {
        if (!await MutateAsync(walletId, requestId, r =>
        {
            r.Status = update.Status;
            r.Payments = update.Payments.ToList();
            r.LastCheckedAt = update.CheckedAt;
            r.PaidAnnouncedAt ??= update.PaidAnnouncedAt;
            r.LockNotifiedAt ??= update.LockNotifiedAt;
        })) return;
        Changed?.Invoke(walletId);
    }

    /// <summary>The person opened the request; it no longer counts on the Request tab (FR-014a).</summary>
    public async Task MarkOpenedAsync(string walletId, string requestId)
    {
        if (await MutateAsync(walletId, requestId, r => r.OpenedSincePaid = true))
            Changed?.Invoke(walletId);
    }

    /// <summary>
    /// Re-reads one request from storage, bypassing the cache, so a second tab's announcement is seen before this
    /// tab announces the same payment (spec 036 C2).
    /// </summary>
    public async Task<PaymentRequest?> ReadFreshAsync(string walletId, string requestId)
    {
        await _gate.WaitAsync();
        try
        {
            _cache.Remove(walletId);
            return (await LoadAsync(walletId)).FirstOrDefault(r => r.Id == requestId);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Applies a change to one stored request; false when there is no such request.</summary>
    private async Task<bool> MutateAsync(string walletId, string requestId, Action<PaymentRequest> change)
    {
        await _gate.WaitAsync();
        try
        {
            var list = await LoadAsync(walletId);
            var r = list.FirstOrDefault(x => x.Id == requestId);
            if (r is null) return false;
            change(r);
            await SaveAsync(walletId, list);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes one request from the list; false when there is no such request.</summary>
    private async Task<bool> RemoveFromListAsync(string walletId, string requestId)
    {
        await _gate.WaitAsync();
        try
        {
            var list = await LoadAsync(walletId);
            if (list.RemoveAll(x => x.Id == requestId) == 0) return false;
            await SaveAsync(walletId, list);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Raises <see cref="Changed"/> for subclasses.</summary>
    private void OnChanged(string walletId) => Changed?.Invoke(walletId);

    private async Task<List<PaymentRequest>> LoadAsync(string walletId)
    {
        if (_cache.TryGetValue(walletId, out var cached)) return cached;
        var json = await _storage.GetAsync(KeyFor(walletId));
        List<PaymentRequest> list;
        try
        {
            list = json is null ? [] : JsonSerializer.Deserialize<List<PaymentRequest>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            list = [];
        }
        _cache[walletId] = list;
        return list;
    }

    // The wallet's list is saved before the legacy list shrinks, so a failure in between can only leave a request
    // in both places (the next load skips the duplicate by id), never in neither.
    private async Task MigrateAsync(string walletId, List<PaymentRequest> list, IReadOnlySet<string> walletAddresses)
    {
        var legacy = await _storage.GetAsync(LegacyKey);
        var result = RequestMigration.Split(legacy, walletAddresses);
        if (!result.Changed) return;

        var known = list.Select(r => r.Id).ToHashSet();
        list.AddRange(result.Moved.Where(r => !known.Contains(r.Id)));
        list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        await SaveAsync(walletId, list);
        if (result.RemainingJson is null) await _storage.RemoveAsync(LegacyKey);
        else await _storage.SetAsync(LegacyKey, result.RemainingJson);
    }

    private async Task SaveAsync(string walletId, List<PaymentRequest> list)
    {
        _cache[walletId] = list;
        await _storage.SetAsync(KeyFor(walletId), JsonSerializer.Serialize(list, JsonOptions));
    }
}
