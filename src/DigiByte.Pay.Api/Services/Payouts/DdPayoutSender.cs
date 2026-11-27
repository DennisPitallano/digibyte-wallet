using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Sends waiting payouts and reconciles every send before it can be retried, so DD is never paid twice (specs/034
/// FR-017 to FR-022, research R3, tasks T033). The debit is written before the network call; a send with no clear
/// answer becomes <c>checking</c> and is never resent until DigiPay knows the first attempt didn't go through.
/// </summary>
public sealed class DdPayoutSender
{
    private readonly DigiPayDbContext _db;
    private readonly DigiDollarNodeClient _node;
    private readonly DdLedgerService _ledger;
    private readonly PayoutAddressService _addresses;
    private readonly WebhookDispatcher _webhooks;
    private readonly AuditLogger _audit;
    private readonly PayoutAlerts _alerts;
    private readonly PayoutOptions _options;
    private readonly ILogger<DdPayoutSender> _log;

    public DdPayoutSender(DigiPayDbContext db, DigiDollarNodeClient node, DdLedgerService ledger, PayoutAddressService addresses,
        WebhookDispatcher webhooks, AuditLogger audit, PayoutAlerts alerts, IOptions<PayoutOptions> options, ILogger<DdPayoutSender> log)
    {
        _db = db;
        _node = node;
        _ledger = ledger;
        _addresses = addresses;
        _webhooks = webhooks;
        _audit = audit;
        _alerts = alerts;
        _options = options.Value;
        _log = log;
    }

    // ==================== Reconcile ====================

    /// <summary>Resolves <c>checking</c> payouts and moves <c>sent</c> ones to <c>confirmed</c>.</summary>
    public async Task ReconcileAsync(DdNodeHealth health, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var checking = await _db.DdPayouts.Where(p => p.Status == DdPayout.StatusChecking).ToListAsync(ct);
        foreach (var payout in checking)
        {
            if (!health.Reachable)
            {
                if (now - (payout.LastAttemptAt ?? now) > Payout.CheckingStuck)
                    _alerts.Raise(PayoutAlerts.CheckingStuck, payout.Id, "Payout {PayoutId} has been checking for over an hour", payout.Id);
                continue;
            }

            var since = ((payout.LastAttemptAt ?? payout.CreatedAt).AddMinutes(-5) - DateTime.UnixEpoch).TotalSeconds;
            IReadOnlyList<DdSend> sends;
            try { sends = await _node.ListSendsAsync((long)since, 200, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "listing sends failed"); continue; }

            var match = sends.FirstOrDefault(s => s.Comment == payout.Id);
            if (match is null)
            {
                var claimed = await _db.DdPayouts.Where(p => p.Txid != null && p.Id != payout.Id).Select(p => p.Txid!).ToListAsync(ct);
                var attemptUnix = (long)(((payout.LastAttemptAt ?? payout.CreatedAt).AddMinutes(-5)) - DateTime.UnixEpoch).TotalSeconds;
                match = sends.FirstOrDefault(s => s.Comment is null && s.Address == payout.Destination && s.AmountCents == payout.AmountCents
                                                  && s.Time >= attemptUnix && !claimed.Contains(s.Txid));
            }

            if (match is not null)
            {
                await MarkSentAsync(payout, match.Txid, ct);
            }
            else if (now - (payout.LastAttemptAt ?? now) >= Payout.CheckingGiveUp)
            {
                await FailAsync(payout, DdPayout.FailureNotFound, null, countsAsFailure: true, reverseDebit: true, ct);
            }
        }

        var sent = await _db.DdPayouts.Where(p => p.Status == DdPayout.StatusSent && p.Txid != null).ToListAsync(ct);
        foreach (var payout in sent)
        {
            if (!health.Reachable) break;
            DdTxStatus status;
            try { status = await _node.GetTxStatusAsync(payout.Txid!, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "tx status failed"); continue; }
            payout.Confirmations = Math.Max(payout.Confirmations, status.Confirmations);
            if (status.Confirmations >= _options.MinConfirmations)
            {
                payout.Status = DdPayout.StatusConfirmed;
                payout.ConfirmedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                await _audit.LogAsync(payout.MerchantId, "payout.confirmed", "Payout", payout.Id,
                    summary: $"DD payout of {payout.AmountCents / 100m:0.00} DD confirmed", metadata: new { txid = payout.Txid });
                await NotifyAsync(payout, "payout.confirmed", ct);
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    // ==================== Send ====================

    /// <summary>Runs the pre-send checks on every waiting payout and sends the ones that pass, one per owner.</summary>
    public async Task SendWaitingAsync(DdNodeHealth health, bool globallyPaused, CancellationToken ct = default)
    {
        var waiting = await _db.DdPayouts.Where(p => p.Status == DdPayout.StatusWaiting).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        if (waiting.Count == 0) return;

        long? available = null, liability = null;
        foreach (var payout in waiting)
        {
            var owner = await Payout.LoadOwnerAsync(_db, payout.MerchantId, payout.StoreId, ct);
            if (owner is null) continue;

            if (globallyPaused || owner.DdPayoutsPausedByOperatorAt is not null) { await WaitAsync(payout, "operator_paused", ct); continue; }
            if (!health.Usable) { await WaitAsync(payout, "node_unavailable", ct); continue; }
            if (!health.ReserveHealthy)
            {
                await WaitAsync(payout, "fee_reserve", ct);
                _alerts.Raise(PayoutAlerts.FeeReserveLow, null,
                    "The DD node's DGB fee reserve is low ({Reserve} DGB); payouts are waiting", health.FeeReserveDgb);
                continue;
            }

            // Address checks just before claiming (FR-002, FR-006, FR-020).
            if (await _addresses.GetPendingAsync(owner, ct) is not null) { await WaitAsync(payout, "change_pending", ct); continue; }
            if (await _addresses.ValidateAsync(payout.Destination, owner.Network, ct) is { } invalid)
            {
                await FailAsync(payout, DdPayout.FailureAddressInvalid, invalid, countsAsFailure: false, reverseDebit: false, ct);
                await AutoPauseAsync(owner, "payout_address_invalid", ct);
                continue;
            }
            if (!string.Equals(payout.Destination, owner.DdPayoutAddress, StringComparison.Ordinal))
            {
                await FailAsync(payout, DdPayout.FailureSuperseded, null, countsAsFailure: false, reverseDebit: false, ct, notify: false);
                continue;
            }

            // Coverage (research R4): DigiPay's own unconfirmed change counts; incoming DD under the threshold doesn't.
            if (available is null)
            {
                try
                {
                    available = await _node.GetWalletBalanceAsync(0, ct) - await _ledger.GetIncomingUnconfirmedAsync(ct);
                    liability = await _ledger.GetLiabilityAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "wallet balance unavailable");
                    await WaitAsync(payout, "node_unavailable", ct);
                    continue;
                }
            }
            if (available < liability)
            {
                await WaitAsync(payout, "coverage", ct);
                _alerts.Raise(PayoutAlerts.CoverageShort, null,
                    "The DD wallet holds {Available} cents but DigiPay owes {Liability}; every payout is waiting", available, liability);
                continue;
            }
            _alerts.Clear(PayoutAlerts.CoverageShort, null);

            await SendAsync(payout, owner, ct);
        }
    }

    private async Task SendAsync(DdPayout payout, IDdPayoutOwner owner, CancellationToken ct)
    {
        // Claim: status and debit saved together before the network call.
        payout.Status = DdPayout.StatusSending;
        payout.WaitReason = null;
        payout.AttemptCount++;
        payout.LastAttemptAt = DateTime.UtcNow;
        _db.DdLedgerEntries.Add(new DdLedgerEntry
        {
            Id = Payout.NewId("dle_"),
            MerchantId = payout.MerchantId,
            StoreId = payout.StoreId,
            Type = DdLedgerEntry.TypeDebit,
            AmountCents = payout.AmountCents,
            PayoutId = payout.Id,
        });
        await _db.SaveChangesAsync(ct);

        var result = await _node.SendAsync(payout.Destination, payout.AmountCents, payout.Id, ct);
        switch (result.Outcome)
        {
            case DdSendOutcome.Sent:
                await MarkSentAsync(payout, result.Txid!, ct);
                break;

            case DdSendOutcome.Rejected when result.InsufficientFunds:
                // Funds still confirming (often DigiPay's own change): wait, don't count it (tasks T033).
                ReverseDebit(payout);
                payout.Status = DdPayout.StatusWaiting;
                payout.WaitReason = "coverage";
                payout.CoverageWaitSince ??= DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                if (DateTime.UtcNow - payout.CoverageWaitSince > TimeSpan.FromMinutes(10))
                    _alerts.Raise(PayoutAlerts.CoverageShort, payout.Id,
                        "Payout {PayoutId} has been refused for insufficient funds for over 10 minutes", payout.Id);
                break;

            case DdSendOutcome.Rejected:
                await FailAsync(payout, DdPayout.FailureRejected, result.Message, countsAsFailure: true, reverseDebit: true, ct);
                break;

            default:
                payout.Status = DdPayout.StatusChecking;
                await _db.SaveChangesAsync(ct);
                await _audit.LogAsync(payout.MerchantId, "payout.checking", "Payout", payout.Id,
                    summary: "No clear answer from the DigiDollar node; checking whether the payout went out",
                    metadata: new { reason = result.Message });
                break;
        }
    }

    // ==================== Outcomes ====================

    private async Task MarkSentAsync(DdPayout payout, string txid, CancellationToken ct)
    {
        payout.Status = DdPayout.StatusSent;
        payout.Txid = txid;
        payout.SentAt = DateTime.UtcNow;
        payout.WaitReason = null;
        payout.CoverageWaitSince = null;
        var owner = await Payout.LoadOwnerAsync(_db, payout.MerchantId, payout.StoreId, ct);
        var resumed = false;
        if (owner is not null)
        {
            owner.DdPayoutConsecutiveFailures = 0;
            if (owner.DdPayoutsAutoPausedAt is not null)
            {
                owner.DdPayoutsAutoPausedAt = null;
                resumed = true;
            }
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(payout.MerchantId, "payout.sent", "Payout", payout.Id,
            summary: $"Sent {payout.AmountCents / 100m:0.00} DD to the payout address",
            metadata: new { txid, destination = payout.Destination, amountCents = payout.AmountCents, trigger = payout.Trigger });
        if (resumed)
            await _audit.LogAsync(payout.MerchantId, "payouts.resumed", OwnerType(payout), payout.StoreId ?? payout.MerchantId,
                summary: "Automatic DD payouts resumed after a successful payout", metadata: new { reason = "payout_sent" });
        await NotifyAsync(payout, "payout.sent", ct);
    }

    private async Task FailAsync(DdPayout payout, string code, string? detail, bool countsAsFailure, bool reverseDebit, CancellationToken ct, bool notify = true)
    {
        if (reverseDebit) ReverseDebit(payout);
        payout.Status = DdPayout.StatusFailed;
        payout.FailureCode = code;
        payout.FailureReason = Payout.FailureText(code, detail);
        payout.FailedAt = DateTime.UtcNow;
        payout.WaitReason = null;
        await _db.SaveChangesAsync(ct);

        if (code == DdPayout.FailureSuperseded) return; // internal re-plan, not a failure the merchant sees (G2)

        await _audit.LogAsync(payout.MerchantId, "payout.failed", "Payout", payout.Id,
            summary: $"DD payout of {payout.AmountCents / 100m:0.00} DD failed", metadata: new { code, detail });
        if (countsAsFailure)
        {
            var owner = await Payout.LoadOwnerAsync(_db, payout.MerchantId, payout.StoreId, ct);
            if (owner is not null)
            {
                owner.DdPayoutConsecutiveFailures++;
                await _db.SaveChangesAsync(ct);
                if (owner.DdPayoutConsecutiveFailures >= Payout.AutoPauseAfterFailures)
                    await AutoPauseAsync(owner, "auto_failures", ct);
            }
        }
        if (notify) await NotifyAsync(payout, "payout.failed", ct);
    }

    private async Task AutoPauseAsync(IDdPayoutOwner owner, string reason, CancellationToken ct)
    {
        if (owner.DdPayoutsAutoPausedAt is not null) return;
        owner.DdPayoutsAutoPausedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(owner.MerchantId, "payouts.paused", owner.OwnerStoreId is null ? "Merchant" : "Store", owner.OwnerStoreId ?? owner.MerchantId,
            summary: "Automatic DD payouts paused; check the payout address", metadata: new { reason });
        _alerts.Raise(PayoutAlerts.StoreAutoPaused, owner.OwnerStoreId ?? owner.MerchantId,
            "Automatic DD payouts paused for {Owner} ({Reason})", owner.OwnerStoreId ?? owner.MerchantId, reason);
    }

    private async Task WaitAsync(DdPayout payout, string reason, CancellationToken ct)
    {
        if (payout.WaitReason == reason) return;
        payout.WaitReason = reason;
        await _db.SaveChangesAsync(ct);
    }

    private void ReverseDebit(DdPayout payout) =>
        _db.DdLedgerEntries.Add(new DdLedgerEntry
        {
            Id = Payout.NewId("dle_"),
            MerchantId = payout.MerchantId,
            StoreId = payout.StoreId,
            Type = DdLedgerEntry.TypeDebitReversal,
            AmountCents = payout.AmountCents,
            PayoutId = payout.Id,
        });

    private static string OwnerType(DdPayout p) => p.StoreId is null ? "Merchant" : "Store";

    private async Task NotifyAsync(DdPayout payout, string eventName, CancellationToken ct)
    {
        if (payout.StoreId is null) return;
        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == payout.StoreId, ct);
        if (store is null) return;
        var payload = await PayoutPayloads.BuildAsync(_db, payout, ct);
        await _webhooks.DispatchEventAsync(store, eventName, "payout", payload, payout.Id, ct);
    }
}

/// <summary>The <c>payout</c> object used by the API and the webhooks (contracts C2 and C5).</summary>
public static class PayoutPayloads
{
    public static async Task<object> BuildAsync(DigiPayDbContext db, DdPayout p, CancellationToken ct = default)
    {
        var items = await (from i in db.DdPayoutItems.AsNoTracking()
                           join e in db.DdLedgerEntries.AsNoTracking() on i.LedgerEntryId equals e.Id
                           where i.PayoutId == p.Id
                           select new { sessionId = e.SessionId, source = e.Source ?? DdLedgerEntry.SourcePayment, amountCents = i.AmountCents })
                          .ToListAsync(ct);
        var owner = await Payout.LoadOwnerAsync(db, p.MerchantId, p.StoreId, ct);
        var failed = p.Status == DdPayout.StatusFailed;
        return new
        {
            id = p.Id,
            merchantId = p.MerchantId,
            storeId = p.StoreId,
            currency = "DD",
            amountCents = p.AmountCents,
            destination = p.Destination,
            status = p.Status,
            waitReason = p.WaitReason,
            txid = failed ? null : p.Txid,
            confirmations = p.Confirmations,
            trigger = p.Trigger,
            items,
            failureCode = p.FailureCode,
            failureReason = p.FailureReason,
            willRetry = failed ? owner is not null && owner.DdPayoutsAutoPausedAt is null && owner.DdPayoutsPausedByOperatorAt is null : (bool?)null,
            createdAt = p.CreatedAt,
            sentAt = p.SentAt,
            confirmedAt = p.ConfirmedAt,
            failedAt = p.FailedAt,
        };
    }
}
