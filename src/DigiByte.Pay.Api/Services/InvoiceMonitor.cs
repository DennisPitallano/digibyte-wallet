using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Hubs;
using DigiByte.Wallet.Services;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Polls active sessions' receive addresses and transitions their status as
/// payments appear in the mempool and gain confirmations. v0 uses a single
/// polling loop — later we can move to a per-address long-poll or push notifications
/// from a node.
/// </summary>
public class InvoiceMonitor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Set to <c>false</c> to keep the background polling loop from running; passes then run only through
    /// <see cref="RunOnceAsync"/>. The test host turns it off so each test decides exactly when a pass runs.
    /// </summary>
    public const string BackgroundLoopConfigKey = "DigiPay:InvoiceMonitor:BackgroundLoop";
    // Below this ratio of the expected amount we treat the payment as underpaid
    // rather than accepting it. 1% rounding slack to allow for fiat→DGB rounding.
    private const decimal UnderpayTolerance = 0.99m;
    private const int ConfirmedAt = 6;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChainServiceProvider _chains;
    private readonly IDdChainProvider _ddChains;
    private readonly DdAvailability _ddAvailability;
    private readonly ILogger<InvoiceMonitor> _logger;
    private readonly bool _backgroundLoop;

    public InvoiceMonitor(
        IServiceScopeFactory scopeFactory,
        IChainServiceProvider chains,
        IDdChainProvider ddChains,
        DdAvailability ddAvailability,
        ILogger<InvoiceMonitor> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _chains = chains;
        _ddChains = ddChains;
        _ddAvailability = ddAvailability;
        _logger = logger;
        _backgroundLoop = configuration.GetValue(BackgroundLoopConfigKey, true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_backgroundLoop) return;

        // Give the host a moment to finish wiring up the DB, SignalR hub, etc.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InvoiceMonitor tick failed");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    // Grace window for rescuing payments that arrive after a session was marked Expired.
    // Merchant's funds will land on-chain regardless; this keeps the session status honest
    // by flipping expired → paid/confirmed when a late tx lands against the invoice address.
    private static readonly TimeSpan LatePaymentGrace = TimeSpan.FromDays(3);

    /// <summary>Runs one polling pass now. The background loop calls the same pass; tests use this to avoid waiting.</summary>
    public Task RunOnceAsync(CancellationToken ct = default) => TickAsync(ct);

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<CheckoutNotifier>();

        var now = DateTime.UtcNow;
        var sessions = await SessionsToWatch(db, now).ToListAsync(ct);

        foreach (var session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            await InspectSessionAsync(session, db, notifier, now, ct);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sessions each tick polls the chain for: open ones, plus expired ones still inside the
    /// late-payment grace window. Sessions moved by the sandbox advance endpoint
    /// (<see cref="PaySession.Simulated"/>) are left alone — the chain has no such payment,
    /// so polling would expire or rewind them.
    /// </summary>
    public static IQueryable<PaySession> SessionsToWatch(DigiPayDbContext db, DateTime now)
    {
        var graceCutoff = now - LatePaymentGrace;
        return db.Sessions.Where(s =>
            !s.Simulated
            && (s.Status == PaySessionStatus.Pending
                || s.Status == PaySessionStatus.Seen
                || s.Status == PaySessionStatus.Paid
                || (s.Status == PaySessionStatus.Expired && s.ExpiresAt > graceCutoff)
                // specs/035 research R5: DD adds up every payment to the address, so an underpaid DD payment stays
                // watched through the late window and a second part can bring it to paid.
                || (s.Status == PaySessionStatus.Underpaid && s.Currency == "DD" && s.ExpiresAt > graceCutoff)
                // A locked buyer-choice payment is watched through the late window for funds sent in the other
                // currency (specs/033 FR-025), whatever its status, until it is refunded or voided.
                || (s.BuyerChoice && s.LockedAt != null && s.ExpiresAt > graceCutoff
                    && s.Status != PaySessionStatus.Refunded && s.Status != PaySessionStatus.Voided)));
    }

    private async Task InspectSessionAsync(
        PaySession session,
        DigiPayDbContext db,
        CheckoutNotifier notifier,
        DateTime now,
        CancellationToken ct)
    {
        var scope = _scopeFactory.CreateScope();
        var webhookDispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<AuditLogger>();

        // Store carries the network + webhook config; merchant is implicit via StoreId.
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.StoreId, ct);
        if (store is null) return;

        // specs/033 buyer choice: until funds arrive the payment can be paid in either currency, so every option is
        // watched; the first funds lock it to that option (research R4). Once locked, funds on the other option are an
        // extra payment for the merchant to refund (FR-025).
        if (session.BuyerChoice)
        {
            var options = await db.SessionOptions.AsNoTracking().Where(o => o.SessionId == session.Id).ToListAsync(ct);
            if (session.LockedAt is null)
            {
                if (!await TryLockAsync(session, options, store, scope, notifier, now, ct))
                {
                    await ExpireIfDueAsync(session, store, db, notifier, webhookDispatcher, now, ct);
                    return;
                }
            }
            else
            {
                await CheckExtraPaymentAsync(session, options, store, scope, webhookDispatcher, auditLogger, now, ct);
            }
        }

        // DigiDollar sessions read the public explorer's DigiDollar data (specs/035), not the DGB chain.
        if (session.Currency == "DD")
        {
            await InspectDdSessionAsync(session, store, db, notifier, webhookDispatcher, auditLogger, now, ct);
            return;
        }

        // Poll the chain first. A payment arriving right at the expiry deadline must still
        // be honoured — the wallet has no idea about session state, it just signed and sent.
        var chain = _chains.For(store.Network);
        // The explorer client throws when it can't answer; this monitor keeps reading that as "nothing seen yet", as it always has,
        // so one unreachable explorer doesn't stop the tick for every other session.
        List<TransactionInfo> txs;
        try { txs = await chain.GetAddressTransactionsAsync(session.Address); }
        catch (Exception) when (!ct.IsCancellationRequested) { txs = []; }

        TransactionInfo? paymentTx = null;
        long receivedSats = 0;
        foreach (var tx in txs)
        {
            var matched = tx.Outputs.Where(o => o.Address == session.Address).Sum(o => o.AmountSatoshis);
            if (matched > 0 && paymentTx is null)
            {
                paymentTx = tx;
                receivedSats = matched;
                break;
            }
        }

        if (paymentTx is null)
        {
            // No payment on chain yet. Expire pending sessions past their deadline.
            await ExpireIfDueAsync(session, store, db, notifier, webhookDispatcher, now, ct);
            return;
        }

        var confirmations = paymentTx.Confirmations;
        session.ReceivedSatoshis = receivedSats;
        session.Confirmations = confirmations;
        session.PaidTxid = paymentTx.TxId;

        var previous = session.Status;
        var minimum = (long)Math.Floor(session.AmountSatoshis * UnderpayTolerance);

        if (receivedSats < minimum)
        {
            session.Status = PaySessionStatus.Underpaid;
        }
        else if (confirmations >= ConfirmedAt)
        {
            session.Status = PaySessionStatus.Confirmed;
            session.PaidAt ??= paymentTx.Timestamp;
        }
        else if (confirmations >= 1)
        {
            session.Status = PaySessionStatus.Paid;
            session.PaidAt ??= paymentTx.Timestamp;
        }
        else
        {
            session.Status = PaySessionStatus.Seen;
            session.SeenAt ??= now;
        }

        if (session.Status != previous || previous == PaySessionStatus.Paid)
        {
            await PublishAsync(notifier, session, ct);
            // Only fire webhooks on genuine status transitions (not on every tick).
            if (session.Status != previous)
            {
                var eventName = "session." + session.Status.ToString().ToLowerInvariant();
                await webhookDispatcher.DispatchAsync(store, session, eventName, ct);

                // Late reconciliation: a payment landed against an already-expired
                // invoice during the grace window. The webhook above keeps the
                // merchant's integration in sync, but it's worth a separate audit
                // row so a human reading the audit log later can spot why an
                // "expired" session is now "paid" without diffing webhook history.
                if (previous == PaySessionStatus.Expired
                    && session.Status is PaySessionStatus.Paid or PaySessionStatus.Confirmed)
                {
                    await auditLogger.LogAsync(
                        session.MerchantId,
                        action: "session.late_reconciled",
                        targetType: "Session",
                        targetId: session.Id,
                        summary: $"Late payment landed on expired session {session.Id} ({session.AmountSatoshis / 100_000_000m:0.########} DGB)",
                        metadata: new
                        {
                            previousStatus = previous.ToString().ToLowerInvariant(),
                            newStatus = session.Status.ToString().ToLowerInvariant(),
                            txid = session.PaidTxid,
                            receivedSatoshis = session.ReceivedSatoshis,
                            expiresAt = session.ExpiresAt,
                        });
                }
            }
        }
    }

    /// <summary>
    /// Expires a pending session past its deadline, announcing it (checkout update and <c>session.expired</c> webhook)
    /// exactly once. The move from pending to expired is claimed in the database first, so when two passes overlap
    /// (a second app instance, or a manual <see cref="RunOnceAsync"/> next to the background loop) only the pass that
    /// made the change sends the webhook. A pass that loses the claim leaves the session as it loaded it, so it never
    /// overwrites what the winning pass saved.
    /// </summary>
    private static async Task ExpireIfDueAsync(PaySession session, PayStore store, DigiPayDbContext db, CheckoutNotifier notifier,
        WebhookDispatcher webhookDispatcher, DateTime now, CancellationToken ct)
    {
        if (session.Status != PaySessionStatus.Pending || now <= session.ExpiresAt) return;
        var claimed = await db.Sessions
            .Where(s => s.Id == session.Id && s.Status == PaySessionStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, PaySessionStatus.Expired), ct);
        if (claimed == 0) return;
        session.Status = PaySessionStatus.Expired;
        await PublishAsync(notifier, session, ct);
        await webhookDispatcher.DispatchAsync(store, session, "session.expired", ct);
    }

    private static Task PublishAsync(CheckoutNotifier notifier, PaySession session, CancellationToken ct) =>
        notifier.PublishAsync(new CheckoutStatusUpdate
        {
            SessionId = session.Id,
            Status = session.Status.ToString().ToLowerInvariant(),
            ReceivedSatoshis = session.ReceivedSatoshis,
            Confirmations = session.Confirmations,
            Txid = session.PaidTxid,
            Currency = session.Currency,
            LockedAt = session.LockedAt,
            AmountCentsDd = session.AmountCentsDd,
        }, ct);

    /// <summary>
    /// Funds received on one option of a buyer-choice payment: satoshis for DGB (from the explorer), cents for DD
    /// (from the public explorer, specs/035), and the first txid when known. A source that can't be read counts as nothing
    /// received, so a DD outage never stops DGB detection (FR-015).
    /// </summary>
    private async Task<(long Amount, string? Txid)> ReceivedOnAsync(PaySessionOption option, PayStore store, IServiceScope scope, CancellationToken ct)
    {
        if (option.Currency == "DD")
        {
            var dd = await ReadDdAsync(store.Network, option.Address, null, ct);
            return dd is null ? (0, null) : (dd.Cents, dd.FirstTxid);
        }

        List<TransactionInfo> txs;
        try { txs = await _chains.For(store.Network).GetAddressTransactionsAsync(option.Address); }
        catch (Exception) when (!ct.IsCancellationRequested) { return (0, null); }
        long sats = 0;
        string? first = null;
        foreach (var tx in txs)
        {
            var matched = tx.Outputs.Where(o => o.Address == option.Address).Sum(o => o.AmountSatoshis);
            if (matched <= 0) continue;
            sats += matched;
            first ??= tx.TxId;
        }
        return (sats, first);
    }

    /// <summary>Locks an unlocked buyer-choice payment to the first option with funds; DGB wins a tie. False when none has.</summary>
    private async Task<bool> TryLockAsync(PaySession session, List<PaySessionOption> options, PayStore store, IServiceScope scope,
        CheckoutNotifier notifier, DateTime now, CancellationToken ct)
    {
        foreach (var option in options.OrderBy(o => o.Currency == "DGB" ? 0 : 1))
        {
            var (amount, _) = await ReceivedOnAsync(option, store, scope, ct);
            if (amount <= 0) continue;
            SessionOptionService.LockTo(session, option, now);
            _logger.LogInformation("Buyer-choice session {SessionId} locked to {Currency}", session.Id, option.Currency);
            await PublishAsync(notifier, session, ct);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Funds on the option a locked buyer-choice payment didn't lock to: recorded once as an extra payment (webhook and
    /// audit <c>session.extra_payment</c>), and its amount kept up to date afterwards without another event (FR-025).
    /// </summary>
    private async Task CheckExtraPaymentAsync(PaySession session, List<PaySessionOption> options, PayStore store, IServiceScope scope,
        WebhookDispatcher webhookDispatcher, AuditLogger auditLogger, DateTime now, CancellationToken ct)
    {
        foreach (var option in options.Where(o => o.Currency != session.Currency))
        {
            var (amount, txid) = await ReceivedOnAsync(option, store, scope, ct);
            if (amount <= 0) continue;
            var first = session.ExtraCurrency is null;
            if (!first && session.ExtraAmount == amount) continue;
            session.ExtraCurrency = option.Currency;
            session.ExtraAmount = amount;
            session.ExtraTxid ??= txid;
            session.ExtraSeenAt ??= now;
            if (!first) continue;
            await webhookDispatcher.DispatchAsync(store, session, "session.extra_payment", ct);
            await auditLogger.LogAsync(
                session.MerchantId,
                action: "session.extra_payment",
                targetType: "Session",
                targetId: session.Id,
                summary: $"Extra {option.Currency} payment on session {session.Id}, paid in {session.Currency}; refund the buyer",
                metadata: new { currency = option.Currency, amount, txid, lockedTo = session.Currency });
        }
    }

    /// <summary>What the explorer shows paid to one DD address (specs/035 research R5).</summary>
    /// <param name="Cents">Every DD output to the address, over all its transactions: total received, not the balance.</param>
    /// <param name="FirstTxid">The first transaction paying the address.</param>
    /// <param name="Confirmations">Confirmations of the transaction that completed the amount due (or of the latest one
    /// when it isn't complete or no amount is due). 0 while it's in the mempool.</param>
    internal sealed record DdReceipt(long Cents, string? FirstTxid, int Confirmations);

    /// <summary>Reads one DD address from the explorer; null when the explorer can't be reached (the caller waits).</summary>
    private async Task<DdReceipt?> ReadDdAsync(string network, string address, long? dueCents, CancellationToken ct)
    {
        DigiByte.Wallet.Services.DdHistoryResult history;
        try { history = await _ddChains.For(network).GetHistoryAsync([address], 100, null, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "DigiDollar explorer unavailable for {Address}", address);
            return null;
        }

        // Oldest first: confirmed by height, then the mempool.
        var paying = history.Items
            .Select(e => (Entry: e, Cents: e.DdOutputs.Where(o => o.Address == address).Sum(o => o.AmountCents)))
            .Where(x => x.Cents > 0)
            .OrderBy(x => x.Entry.BlockHeight is null ? 1 : 0).ThenBy(x => x.Entry.BlockHeight)
            .ToList();
        if (paying.Count == 0) return new DdReceipt(0, null, 0);

        long total = 0;
        int? completing = null;
        foreach (var (entry, cents) in paying)
        {
            total += cents;
            if (completing is null && dueCents is > 0 && total >= dueCents) completing = entry.Confirmations;
        }
        return new DdReceipt(total, paying[0].Entry.Txid, completing ?? paying[^1].Entry.Confirmations);
    }

    /// <summary>
    /// DigiDollar variant of <see cref="InspectSessionAsync"/> (specs/035): reads the address on the public explorer and
    /// moves the payment through the same states as DGB. While the explorer can't be reached the payment waits; a
    /// transaction that disappears entirely leaves the status where it was, as with DGB.
    /// </summary>
    private async Task InspectDdSessionAsync(
        PaySession session,
        PayStore store,
        DigiPayDbContext db,
        CheckoutNotifier notifier,
        WebhookDispatcher webhookDispatcher,
        AuditLogger auditLogger,
        DateTime now,
        CancellationToken ct)
    {
        var requiredCents = session.AmountCentsDd ?? 0;
        if (requiredCents <= 0)
        {
            _logger.LogWarning("DD session {SessionId} missing AmountCentsDd", session.Id);
            return;
        }
        var minimum = (long)Math.Floor(requiredCents * (double)UnderpayTolerance);

        // While the explorer is reported down for this network, DD payments wait (FR-014); DGB never asks.
        if (!await _ddAvailability.IsAvailableAsync(store.Network, ct)) return;
        var receipt = await ReadDdAsync(store.Network, session.Address, minimum, ct);
        if (receipt is null) return; // explorer failed mid-tick: wait, never fail

        if (receipt.Cents == 0)
        {
            // No payment on chain yet. Expire pending sessions past their deadline.
            await ExpireIfDueAsync(session, store, db, notifier, webhookDispatcher, now, ct);
            return;
        }

        // ReceivedSatoshis holds received cents for DD; consumers read session.Currency for the unit.
        session.ReceivedSatoshis = receipt.Cents;
        session.Confirmations = receipt.Confirmations;
        session.PaidTxid ??= receipt.FirstTxid;
        await MarkDdPaidAsync(db, session.Address, now, ct);

        var previous = session.Status;
        if (receipt.Cents < minimum)
        {
            session.Status = PaySessionStatus.Underpaid;
        }
        else if (receipt.Confirmations >= ConfirmedAt)
        {
            session.Status = PaySessionStatus.Confirmed;
            session.PaidAt ??= now;
        }
        else if (receipt.Confirmations >= 1)
        {
            session.Status = PaySessionStatus.Paid;
            session.PaidAt ??= now;
        }
        else
        {
            session.Status = PaySessionStatus.Seen;
            session.SeenAt ??= now;
        }

        if (session.Status != previous || previous == PaySessionStatus.Paid)
        {
            await PublishAsync(notifier, session, ct);
            if (session.Status != previous)
            {
                await webhookDispatcher.DispatchAsync(store, session, "session." + session.Status.ToString().ToLowerInvariant(), ct);
                if (previous == PaySessionStatus.Expired && session.Status is PaySessionStatus.Paid or PaySessionStatus.Confirmed)
                    await auditLogger.LogAsync(session.MerchantId, action: "session.late_reconciled", targetType: "Session",
                        targetId: session.Id, summary: $"Late DigiDollar payment reconciled on expired session {session.Id}",
                        metadata: new { receivedCents = receipt.Cents, txid = session.PaidTxid });
            }
        }
    }

    /// <summary>Records when DD first arrived on one of DigiPay's DD addresses (research R11).</summary>
    private static async Task MarkDdPaidAsync(DigiPayDbContext db, string address, DateTime now, CancellationToken ct)
    {
        var row = await db.DdAddresses.FirstOrDefaultAsync(a => a.Address == address && a.FirstPaidAt == null, ct);
        if (row is not null) row.FirstPaidAt = now;
    }
}
