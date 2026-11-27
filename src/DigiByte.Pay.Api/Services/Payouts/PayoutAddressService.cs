using DigiByte.Crypto.Addresses;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Checks, sets, changes and cancels DD payout addresses (specs/034 FR-001 to FR-010, research R7). Address checks
/// reuse <see cref="DigiDollarAddressValidator"/> from DigiByte.Crypto (constitution Principle IV).
/// </summary>
public sealed class PayoutAddressService
{
    private readonly DigiPayDbContext _db;
    private readonly AuditLogger _audit;
    private readonly WebhookDispatcher _webhooks;
    private readonly DdLedgerService _ledger;
    private readonly PayoutOptions _options;

    public PayoutAddressService(DigiPayDbContext db, AuditLogger audit, WebhookDispatcher webhooks, DdLedgerService ledger, IOptions<PayoutOptions> options)
    {
        _db = db;
        _audit = audit;
        _webhooks = webhooks;
        _ledger = ledger;
        _options = options.Value;
    }

    /// <summary>
    /// Why <paramref name="address"/> can't be a payout address for <paramref name="network"/>, as one of
    /// <c>format</c>, <c>checksum</c>, <c>wrong_network</c>, <c>dgb_address</c>, <c>digipay_address</c>; null when it can.
    /// </summary>
    public async Task<string?> ValidateAsync(string? address, string network, CancellationToken ct = default)
    {
        var reason = ValidateFormat(address, network);
        if (reason is not null) return reason;
        return await DdAddressRegistry.IsDigiPayAddressAsync(_db, address!.Trim(), ct) ? "digipay_address" : null;
    }

    /// <summary>The checks that need no database: format, checksum, network, and "is this a DGB address".</summary>
    public static string? ValidateFormat(string? address, string network)
    {
        var a = address?.Trim();
        if (string.IsNullOrEmpty(a) || !a.All(char.IsAsciiLetterOrDigit)) return "format";
        if (DigiDollarAddressValidator.TryDecode(a, null, out var detected, out _))
            return detected == network ? null : "wrong_network";
        if (AddressValidator.IsValid(a)) return "dgb_address";
        var looksDd = (a.StartsWith("DD", StringComparison.Ordinal) || a.StartsWith("TD", StringComparison.Ordinal) || a.StartsWith("RD", StringComparison.Ordinal))
                      && a.Length is >= 40 and <= 60
                      && a.All(c => "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz".Contains(c));
        return looksDd ? "checksum" : "format";
    }

    public Task<PayoutAddressChange?> GetPendingAsync(IDdPayoutOwner owner, CancellationToken ct = default) =>
        _db.PayoutAddressChanges.FirstOrDefaultAsync(c => c.MerchantId == owner.MerchantId && c.StoreId == owner.OwnerStoreId
                                                         && c.Status == PayoutAddressChange.StatusPending, ct);

    /// <summary>
    /// Records a change. It takes effect at once for a first address on an owner holding nothing (not even DD still
    /// confirming); otherwise after the waiting period, with payouts held meanwhile (FR-005, FR-006).
    /// </summary>
    public async Task<PayoutAddressChange> RequestAsync(IDdPayoutOwner owner, string newAddress, string sessionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var balance = await _ledger.GetBalanceAsync(owner.MerchantId, owner.OwnerStoreId, ct);
        var inFlight = await _db.DdPayouts.AnyAsync(p => p.MerchantId == owner.MerchantId && p.StoreId == owner.OwnerStoreId && Payout.InFlightStatuses.Contains(p.Status), ct);
        var immediate = owner.DdPayoutAddress is null && balance.ConfirmedCents <= 0 && balance.ConfirmingCents <= 0 && !inFlight;

        var change = new PayoutAddressChange
        {
            Id = Payout.NewId("pac_"),
            MerchantId = owner.MerchantId,
            StoreId = owner.OwnerStoreId,
            OldAddress = owner.DdPayoutAddress,
            NewAddress = newAddress.Trim(),
            RequestedAt = now,
            EffectiveAt = immediate ? now : now + _options.ChangeHold,
            RequestedBySessionId = sessionId,
        };
        _db.PayoutAddressChanges.Add(change);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(owner.MerchantId, "payout_address.requested", owner.OwnerStoreId is null ? "Merchant" : "Store", owner.OwnerStoreId ?? owner.MerchantId,
            summary: immediate ? "Set the DD payout address" : $"Requested a DD payout address change, taking effect {Payout.Iso(change.EffectiveAt)}",
            metadata: new { old = change.OldAddress, @new = change.NewAddress, effectiveAt = change.EffectiveAt, changeId = change.Id });
        await NotifyAsync(owner, change, "payout_address.change_requested", ct);

        if (immediate) await ApplyAsync(owner, change, ct);
        return change;
    }

    public async Task CancelAsync(IDdPayoutOwner owner, PayoutAddressChange change, string sessionId, CancellationToken ct = default)
    {
        change.Status = PayoutAddressChange.StatusCancelled;
        change.CancelledAt = DateTime.UtcNow;
        change.CancelledBySessionId = sessionId;
        change.AcknowledgedAt = null; // the cancellation is a new event for the banner
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(owner.MerchantId, "payout_address.cancelled", owner.OwnerStoreId is null ? "Merchant" : "Store", owner.OwnerStoreId ?? owner.MerchantId,
            summary: "Cancelled a pending DD payout address change",
            metadata: new { old = change.OldAddress, @new = change.NewAddress, changeId = change.Id });
        await NotifyAsync(owner, change, "payout_address.change_cancelled", ct);
    }

    /// <summary>Applies every pending change whose waiting period has passed.</summary>
    public async Task<int> ApplyDueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await _db.PayoutAddressChanges
            .Where(c => c.Status == PayoutAddressChange.StatusPending && c.EffectiveAt <= now)
            .ToListAsync(ct);
        foreach (var change in due)
        {
            var owner = await Payout.LoadOwnerAsync(_db, change.MerchantId, change.StoreId, ct);
            if (owner is null)
            {
                change.Status = PayoutAddressChange.StatusCancelled;
                change.CancelledAt = now;
                await _db.SaveChangesAsync(ct);
                continue;
            }
            await ApplyAsync(owner, change, ct);
        }
        return due.Count;
    }

    private async Task ApplyAsync(IDdPayoutOwner owner, PayoutAddressChange change, CancellationToken ct)
    {
        owner.DdPayoutAddress = change.NewAddress;
        owner.DdPayoutAddressSetAt = DateTime.UtcNow;
        // A new address is the merchant acting on a failing one (FR-022).
        owner.DdPayoutsAutoPausedAt = null;
        owner.DdPayoutConsecutiveFailures = 0;
        change.Status = PayoutAddressChange.StatusApplied;
        change.AppliedAt = DateTime.UtcNow;
        change.AcknowledgedAt = null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(owner.MerchantId, "payout_address.applied", owner.OwnerStoreId is null ? "Merchant" : "Store", owner.OwnerStoreId ?? owner.MerchantId,
            summary: "The DD payout address took effect",
            metadata: new { old = change.OldAddress, @new = change.NewAddress, changeId = change.Id });
        await NotifyAsync(owner, change, "payout_address.changed", ct);
    }

    private async Task NotifyAsync(IDdPayoutOwner owner, PayoutAddressChange change, string eventName, CancellationToken ct)
    {
        if (owner is not PayStore store) return;
        await _webhooks.DispatchEventAsync(store, eventName, "payoutAddress", ToPayload(change), payoutId: null, ct);
    }

    public static object ToPayload(PayoutAddressChange c) => new
    {
        changeId = c.Id,
        storeId = c.StoreId,
        merchantId = c.MerchantId,
        oldAddress = c.OldAddress,
        newAddress = c.NewAddress,
        requestedAt = c.RequestedAt,
        effectiveAt = c.EffectiveAt,
        status = c.Status,
    };
}
