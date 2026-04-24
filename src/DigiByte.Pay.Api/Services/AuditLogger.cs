using System.Security.Cryptography;
using System.Text;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Writes <see cref="AuditEvent"/> rows for sensitive merchant actions.
/// Scoped (follows the DbContext lifetime) so the same instance is reused
/// across an HTTP request. Entries are written in the same DbContext as
/// the mutating call; the caller is expected to have already saved the
/// primary change, then calls <see cref="LogAsync"/> which appends and
/// saves the audit row. Failures here are swallowed — audit logging must
/// never mask a successful business operation.
/// </summary>
public class AuditLogger
{
    private readonly DigiPayDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditLogger> _log;

    public AuditLogger(DigiPayDbContext db, IHttpContextAccessor http, ILogger<AuditLogger> log)
    {
        _db = db;
        _http = http;
        _log = log;
    }

    public Task LogAsync(
        string merchantId,
        string action,
        string targetType,
        string targetId,
        string? summary = null,
        object? metadata = null)
    {
        var ctx = _http.HttpContext;
        var (actorType, actorId) = ResolveActor(ctx);
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();

        string? metaJson = null;
        if (metadata is not null)
        {
            try
            {
                metaJson = System.Text.Json.JsonSerializer.Serialize(metadata);
                if (metaJson.Length > 2048) metaJson = metaJson[..2048];
            }
            catch { /* ignore — audit must not throw */ }
        }

        var row = new AuditEvent
        {
            Id = $"aud_{RandomId(16)}",
            MerchantId = merchantId,
            ActorType = actorType,
            ActorId = actorId,
            ActorIp = ip,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Summary = summary,
            Metadata = metaJson,
        };
        _db.AuditEvents.Add(row);
        return SaveSafelyAsync();
    }

    private async Task SaveSafelyAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "audit log write failed");
        }
    }

    private static (string ActorType, string? ActorId) ResolveActor(HttpContext? ctx)
    {
        if (ctx is null) return ("System", null);
        var header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return ("System", null);
        var token = header["Bearer ".Length..].Trim();
        var underscore = token.LastIndexOf('_');
        if (underscore <= 0) return ("System", null);
        var prefix = token[..underscore];
        if (prefix.StartsWith("dgp_", StringComparison.Ordinal)) return ("ApiKey", prefix);
        if (prefix.StartsWith("dps_", StringComparison.Ordinal)) return ("Session", prefix);
        return ("System", null);
    }

    private static string RandomId(int lengthChars)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(lengthChars);
        var sb = new StringBuilder(lengthChars);
        foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }
}
