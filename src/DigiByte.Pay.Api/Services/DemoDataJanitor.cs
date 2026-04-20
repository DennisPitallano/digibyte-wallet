using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Periodically removes sandbox rows (<c>mer_demo_*</c>, <c>sto_demo_*</c>,
/// <c>ses_demo_*</c>) older than <see cref="DemoDataTtl"/>. The public
/// <c>/v1/pay/test/demo-session</c> endpoint mints fresh demo tuples on
/// every call; without this cleanup the DB grows forever as visitors poke
/// the <c>/embed/demo.html</c> harness on pay.dgbwallet.app.
///
/// Safety: we only touch rows where every id starts with the demo prefix,
/// so real merchant data is never at risk. Related webhook deliveries and
/// sessions cascade via the EF relationships.
/// </summary>
public class DemoDataJanitor : BackgroundService
{
    // A day is enough for anyone clicking through the demo to finish,
    // short enough that stale demo rows never pile up.
    private static readonly TimeSpan DemoDataTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DemoDataJanitor> _logger;

    public DemoDataJanitor(IServiceScopeFactory scopeFactory, ILogger<DemoDataJanitor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup so redeploys clean accumulated demo rows
        // without waiting an hour.
        await SweepAsync(stoppingToken);

        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "DemoDataJanitor sweep failed"); }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();

        var cutoff = DateTime.UtcNow - DemoDataTtl;

        // Sessions first (they reference stores/merchants); demo sessions use
        // the CreatedAt column as their age signal.
        var sessions = await db.Sessions
            .Where(s => s.Id.StartsWith("ses_demo_") && s.CreatedAt < cutoff)
            .ToListAsync(ct);
        db.Sessions.RemoveRange(sessions);

        var stores = await db.Stores
            .Where(s => s.Id.StartsWith("sto_demo_") && s.CreatedAt < cutoff)
            .ToListAsync(ct);
        db.Stores.RemoveRange(stores);

        var merchants = await db.Merchants
            .Where(m => m.Id.StartsWith("mer_demo_") && m.CreatedAt < cutoff)
            .ToListAsync(ct);
        db.Merchants.RemoveRange(merchants);

        if (sessions.Count + stores.Count + merchants.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "DemoDataJanitor pruned {Sessions} sessions, {Stores} stores, {Merchants} merchants",
                sessions.Count, stores.Count, merchants.Count);
        }
    }
}
