using System.Text.Json;
using System.Text.Json.Nodes;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services.Payouts;

/// <summary>
/// Adds payout details to DD sessions in the merchant's own session list (specs/034 FR-028, FR-029, tasks T044):
/// <c>ddPayout { payoutId, status }</c> for the latest live payout covering the session, and <c>ddExtraRefundCents</c>.
/// Kept off the public session read, which anyone with the session id can call.
/// </summary>
public static class SessionPayoutInfo
{
    public sealed record Info(string? PayoutId, string? Status, long ExtraRefundCents);

    public static async Task<IReadOnlyDictionary<string, Info>> LoadAsync(DigiPayDbContext db, IEnumerable<PaySession> sessions, CancellationToken ct = default)
    {
        var ids = sessions.Where(s => s.Currency == "DD" || s.BuyerChoice).Select(s => s.Id).ToList();
        if (ids.Count == 0) return new Dictionary<string, Info>();

        var credits = await db.DdLedgerEntries.AsNoTracking()
            .Where(e => e.Type == DdLedgerEntry.TypeCredit && e.SessionId != null && ids.Contains(e.SessionId))
            .Select(e => new { e.Id, e.SessionId, e.Source, e.AmountCents })
            .ToListAsync(ct);
        var creditIds = credits.Select(c => c.Id).ToList();
        var payouts = await (from i in db.DdPayoutItems.AsNoTracking()
                             join p in db.DdPayouts.AsNoTracking() on i.PayoutId equals p.Id
                             where creditIds.Contains(i.LedgerEntryId) && p.Status != DdPayout.StatusFailed
                             select new { i.LedgerEntryId, p.Id, p.Status, p.CreatedAt }).ToListAsync(ct);

        return credits.GroupBy(c => c.SessionId!).ToDictionary(g => g.Key, g =>
        {
            var latest = payouts.Where(p => g.Any(c => c.Id == p.LedgerEntryId)).OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            var extra = g.Where(c => c.Source == DdLedgerEntry.SourceExtraRefund).Sum(c => c.AmountCents);
            return new Info(latest?.Id, latest?.Status, extra);
        });
    }

    /// <summary>The session DTO with <c>ddPayout</c> and <c>ddExtraRefundCents</c> added.</summary>
    public static JsonNode Attach(object dto, string sessionId, IReadOnlyDictionary<string, Info> info)
    {
        var node = JsonSerializer.SerializeToNode(dto, JsonSerializerOptions.Web)!.AsObject();
        if (info.TryGetValue(sessionId, out var i))
        {
            node["ddPayout"] = i.PayoutId is null ? null : new JsonObject { ["payoutId"] = i.PayoutId, ["status"] = i.Status };
            node["ddExtraRefundCents"] = i.ExtraRefundCents;
        }
        else
        {
            node["ddPayout"] = null;
            node["ddExtraRefundCents"] = 0;
        }
        return node;
    }
}
