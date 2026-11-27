using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Reads the node wallet's DD receives and keeps the ledger in step (specs/034 research R4): one credit per receive
/// and source, confirmations updated until they reach the payout threshold, and reversals handled (FR-023).
/// It reads the wallet, not sessions, so DD that arrives after a session closed is still credited (FR-016).
/// </summary>
public sealed class DdLedgerScanner
{
    private const int PageSize = 200;
    private const int MaxPages = 50;

    private readonly DigiPayDbContext _db;
    private readonly DigiDollarNodeClient _node;
    private readonly PayoutAlerts _alerts;
    private readonly AuditLogger _audit;
    private readonly PayoutOptions _options;
    private readonly ILogger<DdLedgerScanner> _log;

    public DdLedgerScanner(DigiPayDbContext db, DigiDollarNodeClient node, PayoutAlerts alerts, AuditLogger audit,
        IOptions<PayoutOptions> options, ILogger<DdLedgerScanner> log)
    {
        _db = db;
        _node = node;
        _alerts = alerts;
        _audit = audit;
        _options = options.Value;
        _log = log;
    }

    /// <summary>Scans the wallet's receives, newest first, until a page holds nothing new or still confirming.</summary>
    public async Task ScanAsync(CancellationToken ct = default)
    {
        var minConf = _options.MinConfirmations;
        for (var page = 0; page < MaxPages; page++)
        {
            var receives = await _node.ListReceivesAsync(PageSize, page * PageSize, ct);
            if (receives.Count == 0) break;

            var allSettled = true;
            foreach (var receive in receives)
            {
                var settled = await ApplyAsync(receive, minConf, ct);
                allSettled &= settled;
            }
            await _db.SaveChangesAsync(ct);
            if (allSettled || receives.Count < PageSize) break;
        }

        await CheckRecentReversalsAsync(minConf, ct);
        await Payout.SetSettingAsync(_db, OperatorSetting.LedgerLastScanAt, Payout.Iso(DateTime.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Applies one receive. Returns true when it was already known and fully confirmed (nothing to do).</summary>
    private async Task<bool> ApplyAsync(DdReceive receive, int minConf, CancellationToken ct)
    {
        var existing = await _db.DdLedgerEntries
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.Txid == receive.Txid && e.Address == receive.Address)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            var settled = true;
            foreach (var credit in existing)
            {
                if (credit.ReversedAt is not null) continue;
                if (receive.Confirmations < 0)
                {
                    await ReverseAsync(credit, ct);
                    continue;
                }
                if (credit.Confirmations != receive.Confirmations) credit.Confirmations = receive.Confirmations;
                if (credit.ConfirmedAt is null && receive.Confirmations >= minConf) credit.ConfirmedAt = DateTime.UtcNow;
                settled &= credit.ConfirmedAt is not null;
            }
            return settled;
        }

        if (receive.Confirmations < 0) return true; // conflicted before we ever saw it: nothing to credit

        var owner = await DdAddressRegistry.ResolveAsync(_db, receive.Address, ct);
        if (owner is null)
        {
            _alerts.Raise(PayoutAlerts.UnattributedReceive, receive.Address,
                "DD arrived at {Address} (txid {Txid}, {Cents} cents), which DigiPay has no record of. It was not credited.",
                receive.Address, receive.Txid, receive.AmountCents);
            return true;
        }

        foreach (var (source, cents, sessionId) in await ClassifyAsync(owner, receive, ct))
        {
            _db.DdLedgerEntries.Add(new DdLedgerEntry
            {
                Id = Payout.NewId("dle_"),
                MerchantId = owner.MerchantId,
                StoreId = owner.StoreId,
                Type = DdLedgerEntry.TypeCredit,
                Source = source,
                AmountCents = cents,
                Txid = receive.Txid,
                Address = receive.Address,
                SessionId = sessionId,
                Confirmations = receive.Confirmations,
                ConfirmedAt = receive.Confirmations >= minConf ? DateTime.UtcNow : null,
            });
        }
        return false;
    }

    /// <summary>
    /// Splits a receive into sources (data-model "Classifying a credit"): donation address → donation; a buyer-choice
    /// session locked to DGB → extra_refund; after expiry → late; up to the amount still due → payment; the rest →
    /// overpayment.
    /// </summary>
    private async Task<List<(string Source, long Cents, string? SessionId)>> ClassifyAsync(DdAddress owner, DdReceive receive, CancellationToken ct)
    {
        if (owner.Kind == DdAddress.KindDonation) return [(DdLedgerEntry.SourceDonation, receive.AmountCents, null)];

        var session = owner.SessionId is null ? null : await _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == owner.SessionId, ct);
        if (session is null) return [(DdLedgerEntry.SourcePayment, receive.AmountCents, owner.SessionId)];

        // specs/033 FR-025: a buyer-choice session that locked to DGB still received DD at its DD option.
        if (session.BuyerChoice && session.LockedAt is not null && session.Currency != "DD")
            return [(DdLedgerEntry.SourceExtraRefund, receive.AmountCents, session.Id)];

        var receivedAt = receive.Time > 0 ? DateTimeOffset.FromUnixTimeSeconds(receive.Time).UtcDateTime : DateTime.UtcNow;
        if (receivedAt > session.ExpiresAt) return [(DdLedgerEntry.SourceLate, receive.AmountCents, session.Id)];

        var due = session.AmountCentsDd;
        if (due is null or 0)
        {
            due = await _db.SessionOptions.AsNoTracking()
                .Where(o => o.SessionId == session.Id && o.Currency == "DD")
                .Select(o => o.AmountCentsDd).FirstOrDefaultAsync(ct);
        }
        if (due is null or 0) return [(DdLedgerEntry.SourcePayment, receive.AmountCents, session.Id)];

        var paidSoFar = await _db.DdLedgerEntries
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.SessionId == session.Id && e.Source == DdLedgerEntry.SourcePayment && e.ReversedAt == null)
            .SumAsync(e => (long?)e.AmountCents, ct) ?? 0;
        paidSoFar += _db.DdLedgerEntries.Local
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.SessionId == session.Id && e.Source == DdLedgerEntry.SourcePayment
                        && _db.Entry(e).State == EntityState.Added)
            .Sum(e => e.AmountCents);

        var payment = Math.Clamp(due.Value - paidSoFar, 0, receive.AmountCents);
        var parts = new List<(string, long, string?)>();
        if (payment > 0) parts.Add((DdLedgerEntry.SourcePayment, payment, session.Id));
        if (receive.AmountCents - payment > 0) parts.Add((DdLedgerEntry.SourceOverpayment, receive.AmountCents - payment, session.Id));
        return parts;
    }

    /// <summary>
    /// Recently confirmed credits are checked against the DD index: one that dropped back to the mempool after it was
    /// confirmed has been undone by a chain reorganisation.
    /// </summary>
    private async Task CheckRecentReversalsAsync(int minConf, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-6);
        var recent = await _db.DdLedgerEntries
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.ConfirmedAt != null && e.ConfirmedAt >= since && e.ReversedAt == null)
            .ToListAsync(ct);
        foreach (var txid in recent.Select(c => c.Txid).Distinct())
        {
            if (txid is null) continue;
            DdTxStatus status;
            try { status = await _node.GetTxStatusAsync(txid, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "tx status check failed for {Txid}", txid); continue; }
            if (status.Status != "mempool") continue;
            foreach (var credit in recent.Where(c => c.Txid == txid)) await ReverseAsync(credit, ct);
        }
    }

    /// <summary>
    /// A credit's receive is gone. Not yet paid out: it simply leaves the balance. Already paid out (or being sent):
    /// DigiPay absorbs the loss, the store's balance is not reduced, and the operator is alerted (FR-023).
    /// </summary>
    private async Task ReverseAsync(DdLedgerEntry credit, CancellationToken ct)
    {
        credit.ReversedAt = DateTime.UtcNow;
        var covering = await (from i in _db.DdPayoutItems
                              join p in _db.DdPayouts on i.PayoutId equals p.Id
                              where i.LedgerEntryId == credit.Id && Payout.LiveStatuses.Contains(p.Status)
                              select new { Item = i, Payout = p }).ToListAsync(ct);

        var paidOut = covering.Where(c => Payout.DebitedStatuses.Contains(c.Payout.Status)).ToList();
        foreach (var waiting in covering.Where(c => c.Payout.Status == DdPayout.StatusWaiting))
        {
            // The payout was built on DD that is gone: close it so the planner rebuilds it from what is left.
            waiting.Payout.Status = DdPayout.StatusFailed;
            waiting.Payout.FailureCode = DdPayout.FailureSuperseded;
            waiting.Payout.FailedAt = DateTime.UtcNow;
        }

        if (paidOut.Count == 0) return;

        var absorbed = paidOut.Sum(c => c.Item.AmountCents);
        _db.DdLedgerEntries.Add(new DdLedgerEntry
        {
            Id = Payout.NewId("dle_"),
            MerchantId = credit.MerchantId,
            StoreId = credit.StoreId,
            Type = DdLedgerEntry.TypeReversalAbsorbed,
            Source = credit.Source,
            AmountCents = absorbed,
            Txid = credit.Txid,
            Address = credit.Address,
            SessionId = credit.SessionId,
            PayoutId = paidOut[0].Payout.Id,
        });
        _alerts.Raise(PayoutAlerts.ReversalAbsorbed, credit.Id,
            "A DD receive already paid out was reversed: txid {Txid}, {Cents} cents, payout {PayoutId}. DigiPay absorbs it; top up the wallet.",
            credit.Txid, absorbed, paidOut[0].Payout.Id);
        await _audit.LogAsync(credit.MerchantId, "payout.reversal_absorbed", "Payout", paidOut[0].Payout.Id,
            summary: $"A payment already paid out ({absorbed / 100m:0.00} DD) was undone on the network; DigiPay absorbed it",
            metadata: new { txid = credit.Txid, amountCents = absorbed, sessionId = credit.SessionId });
    }
}
