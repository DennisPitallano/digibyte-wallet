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
    // Below this ratio of the expected amount we treat the payment as underpaid
    // rather than accepting it. 1% rounding slack to allow for fiat→DGB rounding.
    private const decimal UnderpayTolerance = 0.99m;
    private const int ConfirmedAt = 6;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<InvoiceMonitor> _logger;
    private readonly Dictionary<string, IBlockchainService> _chainByNetwork = new();

    public InvoiceMonitor(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        ILogger<InvoiceMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<CheckoutNotifier>();

        var now = DateTime.UtcNow;
        var graceCutoff = now - LatePaymentGrace;
        var sessions = await db.Sessions
            .Where(s =>
                s.Status == PaySessionStatus.Pending
                || s.Status == PaySessionStatus.Seen
                || s.Status == PaySessionStatus.Paid
                || (s.Status == PaySessionStatus.Expired && s.ExpiresAt > graceCutoff))
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            await InspectSessionAsync(session, db, notifier, now, ct);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
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

        // Store carries the network + webhook config; merchant is implicit via StoreId.
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.StoreId, ct);
        if (store is null) return;

        // Poll the chain first. A payment arriving right at the expiry deadline must still
        // be honoured — the wallet has no idea about session state, it just signed and sent.
        var chain = GetChainService(store.Network);
        var txs = await chain.GetAddressTransactionsAsync(session.Address);

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
            if (session.Status == PaySessionStatus.Pending && now > session.ExpiresAt)
            {
                session.Status = PaySessionStatus.Expired;
                await PublishAsync(notifier, session, ct);
                await webhookDispatcher.DispatchAsync(store, session, "session.expired", ct);
            }
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
            }
        }
    }

    private static Task PublishAsync(CheckoutNotifier notifier, PaySession session, CancellationToken ct) =>
        notifier.PublishAsync(new CheckoutStatusUpdate
        {
            SessionId = session.Id,
            Status = session.Status.ToString().ToLowerInvariant(),
            ReceivedSatoshis = session.ReceivedSatoshis,
            Confirmations = session.Confirmations,
            Txid = session.PaidTxid,
        }, ct);

    private IBlockchainService GetChainService(string network)
    {
        if (_chainByNetwork.TryGetValue(network, out var existing)) return existing;
        var isTestnet = network is "testnet" or "regtest";
        var http = _httpFactory.CreateClient("DigiPayChain");
        var svc = new BlockchainApiService(http, isTestnet: isTestnet);
        _chainByNetwork[network] = svc;
        return svc;
    }
}
