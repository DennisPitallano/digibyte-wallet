using DigiByte.Pay.Api.Data;
using Microsoft.Extensions.Options;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// The DD payout loop (specs/034 research R8). Every cycle: node health, ledger scan, reconcile sends, apply due
/// address changes, plan payouts, send. An unreachable node means "waiting", never "failed" (FR-034), and nothing here
/// is on the DGB path (FR-036). Single instance, like <see cref="WebhookRetrier"/>.
/// </summary>
public sealed class DdPayoutWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PayoutOptions _options;
    private readonly PayoutAlerts _alerts;
    private readonly ILogger<DdPayoutWorker> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _warnedDisabled;

    public DdPayoutWorker(IServiceScopeFactory scopes, IOptions<PayoutOptions> options, PayoutAlerts alerts, ILogger<DdPayoutWorker> log)
    {
        _scopes = scopes;
        _options = options.Value;
        _alerts = alerts;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled) return;
        using var timer = new PeriodicTimer(_options.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "DD payout cycle failed"); }
        }
    }

    /// <summary>One full cycle. Public so tests drive it by hand.</summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<DigiPayDbContext>();
            var node = sp.GetRequiredService<DigiDollarNodeClient>();

            await sp.GetRequiredService<StepUpService>().AuditAbandonedAsync(ct);

            var health = await node.GetHealthAsync(ct);
            await TrackNodeAsync(db, health, ct);

            var paused = await Payout.GloballyPausedAsync(db, _options, ct);
            if (health.Usable)
            {
                _warnedDisabled = false;
                await sp.GetRequiredService<DdLedgerScanner>().ScanAsync(ct);
            }
            else if (health.Reachable && !_warnedDisabled)
            {
                _warnedDisabled = true;
                _log.LogWarning("The DD node reports DigiDollar disabled or not deployed; DD payouts are idle");
            }

            var sender = sp.GetRequiredService<DdPayoutSender>();
            await sender.ReconcileAsync(health, ct);
            await sp.GetRequiredService<PayoutAddressService>().ApplyDueAsync(ct);
            await sp.GetRequiredService<DdPayoutPlanner>().PlanAsync(paused, ct);
            await sender.SendWaitingAsync(health, paused, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TrackNodeAsync(DigiPayDbContext db, DdNodeHealth health, CancellationToken ct)
    {
        var since = Payout.ParseIso(await Payout.GetSettingAsync(db, OperatorSetting.NodeUnreachableSince, ct));
        if (health.Usable)
        {
            if (since is not null)
            {
                await Payout.SetSettingAsync(db, OperatorSetting.NodeUnreachableSince, null, ct);
                await db.SaveChangesAsync(ct);
                _alerts.Clear(PayoutAlerts.NodeUnavailable6h, null);
            }
            return;
        }
        if (since is null)
        {
            await Payout.SetSettingAsync(db, OperatorSetting.NodeUnreachableSince, Payout.Iso(DateTime.UtcNow), ct);
            await db.SaveChangesAsync(ct);
            return;
        }
        if (DateTime.UtcNow - since.Value > Payout.NodeDownAlert)
        {
            var waiting = db.DdPayouts.Any(p => p.Status == DdPayout.StatusWaiting);
            if (waiting)
                _alerts.Raise(PayoutAlerts.NodeUnavailable6h, null,
                    "The DigiDollar node has been unavailable since {Since} with payouts waiting", Payout.Iso(since.Value));
        }
    }
}
