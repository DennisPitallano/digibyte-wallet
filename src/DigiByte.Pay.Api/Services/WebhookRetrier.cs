using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Re-fires webhook deliveries whose <see cref="WebhookDelivery.NextRetryAt"/>
/// has come due. Polls every minute and re-dispatches via
/// <see cref="WebhookDispatcher.ReplayAsync"/>, which persists a fresh
/// delivery row with the next attempt number — so the retry chain stays
/// auditable from the dashboard.
///
/// Retry policy is owned by <see cref="WebhookDispatcher"/>: this service
/// just wakes up deliveries on schedule. If the replay also fails, the
/// dispatcher stamps <see cref="WebhookDelivery.NextRetryAt"/> on the new
/// row and the loop continues until the schedule is exhausted.
///
/// Concurrency: one Pay.Api instance → one retrier. Multi-instance
/// deployments would need SELECT ... FOR UPDATE SKIP LOCKED or a leader
/// election; that's a Q2 concern.
/// </summary>
public class WebhookRetrier : BackgroundService
{
    // 1 minute matches the shortest entry in the retry schedule — anything
    // longer and the first retry could be delayed by up to 59 s; anything
    // shorter just wastes DB round-trips.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    // Per-sweep cap so one giant backlog doesn't monopolise the http client
    // pool. Remaining rows get picked up in the next sweep.
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookRetrier> _logger;

    public WebhookRetrier(IServiceScopeFactory scopeFactory, ILogger<WebhookRetrier> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        // First tick is one interval away — no rush to sweep at boot.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "WebhookRetrier sweep failed"); }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DigiPayDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();

        var now = DateTime.UtcNow;
        var due = await db.WebhookDeliveries
            .Where(d => d.NextRetryAt != null && d.NextRetryAt <= now)
            .OrderBy(d => d.NextRetryAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        int retried = 0;
        foreach (var prev in due)
        {
            // Clear NextRetryAt first and save — this is the "claim" that
            // stops a concurrent sweep (or a long-running dispatch in the
            // same sweep) from picking up the same row twice.
            prev.NextRetryAt = null;
            await db.SaveChangesAsync(ct);

            if (prev.SessionId is null)
            {
                // Synthetic events (webhook.test) have no session to rebuild
                // the payload from — skip them. Shouldn't happen in practice
                // since we never set NextRetryAt on test deliveries, but
                // defensive.
                continue;
            }

            var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == prev.StoreId, ct);
            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == prev.SessionId, ct);
            if (store is null || session is null)
            {
                // Store or session was deleted between original delivery and
                // retry — nothing to resend. Leave the old row dead-lettered.
                continue;
            }

            await dispatcher.ReplayAsync(prev, store, session, ct);
            retried++;
        }

        if (retried > 0)
        {
            _logger.LogInformation("WebhookRetrier re-fired {Count} delivery(ies)", retried);
        }
    }
}
