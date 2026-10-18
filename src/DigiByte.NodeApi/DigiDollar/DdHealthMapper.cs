using System.Text.Json;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary>
/// Maps node RPC output to the wallet-facing fields of <c>GET /api/digidollar/health</c>
/// (contracts/nodeapi-digidollar-read.md): oracle price/height, protection flags, node/network height, index lag.
///
/// The exact RPC field names for the protection status are not documented in the source we could read, so the
/// mapper accepts the likely spellings; verify against a live node (quickstart step 4, scenario 12) before shipping.
/// </summary>
public static class DdHealthMapper
{
    /// <summary>Blocks are 15 seconds apart on DigiByte, used to turn the oracle price age into a height.</summary>
    public const int BlockSeconds = 15;

    public sealed record OracleInfo(long PriceMicroUsd, int Height);

    public sealed record ProtectionInfo(bool ErrActive, bool VolatilityFreeze, decimal? SystemCollateralPercent);

    /// <summary>The oracle, or null when there is no usable (positive) price.</summary>
    public static OracleInfo? MapOracle(decimal priceUsd, long ageSeconds, int nodeHeight)
    {
        if (priceUsd <= 0) return null;
        var ageBlocks = ageSeconds <= 0 ? 0 : (int)(ageSeconds / BlockSeconds);
        return new OracleInfo((long)Math.Round(priceUsd * 1_000_000m), Math.Max(0, nodeHeight - ageBlocks));
    }

    public static ProtectionInfo MapProtection(JsonElement? status, decimal systemHealthPercent)
    {
        var err = false;
        var freeze = false;
        if (status is { ValueKind: JsonValueKind.Object } s)
        {
            err = ReadBool(s, "err_active", "errActive", "err", "emergency_redemption_ratio_active");
            freeze = ReadBool(s, "volatility_freeze", "volatilityFreeze", "volatility_freeze_active", "freeze_active");
        }
        return new ProtectionInfo(err, freeze, systemHealthPercent > 0 ? systemHealthPercent : null);
    }

    private static bool ReadBool(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var v))
                return v.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
                    _ => false,
                };
        return false;
    }
}
