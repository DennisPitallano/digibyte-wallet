using System.Text.Json;

namespace DigiByte.Wallet.Models;

/// <summary>
/// One oracle reading as the data service serves it (specs/014-digibyte-io-oracle, contracts/oracle-api.md): the price in micro-dollars, when the
/// oracle last updated, the source's stale flag, the protection and restriction flags, and when the data service obtained it. Display only: no
/// transaction is ever built, signed or approved from it. It exists in memory and is replaced by the next answer.
/// </summary>
public sealed class OracleReading
{
    /// <summary>USD per DGB times 10^6. Always above zero.</summary>
    public required long PriceMicroUsd { get; init; }

    /// <summary>The block the oracle last updated at. Always above zero.</summary>
    public required long Height { get; init; }

    /// <summary>When the oracle last updated, in Unix seconds.</summary>
    public required long Time { get; init; }

    /// <summary>The source's own stale flag.</summary>
    public required bool ReportedStale { get; init; }

    public required bool VolatilityFreeze { get; init; }

    public required bool MintingRestricted { get; init; }

    public required bool AllOperationsRestricted { get; init; }

    public required bool ErrActive { get; init; }

    /// <summary>When the data service obtained the answer from digibyte.io, in Unix seconds. The 5-minute limit counts from here.</summary>
    public required long FetchedAt { get; init; }

    public DateTimeOffset FetchedAtUtc => DateTimeOffset.FromUnixTimeSeconds(FetchedAt);

    public DateTimeOffset UpdatedAtUtc => DateTimeOffset.FromUnixTimeSeconds(Time);

    /// <summary>
    /// Reads the data service's answer, checking its shape again because the wallet does not trust the relay's shape: every field present and of
    /// the right type, the price and block above zero, and no update time more than 10 minutes in the future. Unknown extra fields are ignored.
    /// Returns false (and never throws) for anything else.
    /// </summary>
    public static bool TryParse(string? json, DateTimeOffset now, out OracleReading reading)
    {
        reading = null!;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!Whole(root, "priceMicroUsd", out var price) || price <= 0) return false;
            if (!Whole(root, "lastUpdateHeight", out var height) || height <= 0) return false;
            if (!Whole(root, "lastUpdateTime", out var updated) || updated <= 0) return false;
            if (updated > now.AddMinutes(10).ToUnixTimeSeconds()) return false;
            if (!Whole(root, "fetchedAt", out var fetched) || fetched <= 0) return false;
            if (!Flag(root, "reportedStale", out var stale)
                || !Flag(root, "volatilityFreeze", out var freeze)
                || !Flag(root, "mintingRestricted", out var minting)
                || !Flag(root, "allOperationsRestricted", out var allOps)
                || !Flag(root, "errActive", out var err))
                return false;

            reading = new OracleReading
            {
                PriceMicroUsd = price,
                Height = height,
                Time = updated,
                ReportedStale = stale,
                VolatilityFreeze = freeze,
                MintingRestricted = minting,
                AllOperationsRestricted = allOps,
                ErrActive = err,
                FetchedAt = fetched,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Whole(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value);
    }

    private static bool Flag(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = v.GetBoolean();
        return true;
    }
}
