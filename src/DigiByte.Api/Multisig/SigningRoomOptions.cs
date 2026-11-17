namespace DigiByte.Api.Multisig;

/// <summary>
/// Online multisig signing rooms (specs/017-multisig-online-signing, FR-017, FR-027). Bound from <c>MultisigSigning</c>.
/// </summary>
public sealed class SigningRoomOptions
{
    public const string SectionName = "MultisigSigning";

    /// <summary>Operator cap on a room's lifetime. Choices above it are not offered and are refused.</summary>
    public int MaxLifetimeMinutes { get; set; } = 120;

    /// <summary>Lifetime used when the opener picks none.</summary>
    public int DefaultLifetimeMinutes { get; set; } = 30;

    /// <summary>Choices the wallet offers, before the cap is applied.</summary>
    public int[] AllowedLifetimesMinutes { get; set; } = [15, 30, 60, 120];

    /// <summary>Failed join attempts allowed per connection per minute.</summary>
    public int FailedJoinsPerMinute { get; set; } = 5;

    /// <summary>Rooms a connection may open per hour.</summary>
    public int RoomsOpenedPerHour { get; set; } = 10;

    /// <summary>Lifetimes actually offered: the allowed choices at or below the cap, in order.</summary>
    public IReadOnlyList<int> EffectiveLifetimes() =>
        AllowedLifetimesMinutes.Where(m => m > 0 && m <= MaxLifetimeMinutes).Distinct().Order().ToList();

    /// <summary>The default, or the largest offered choice below it when the cap is lower than the default.</summary>
    public int EffectiveDefault()
    {
        var offered = EffectiveLifetimes();
        if (offered.Contains(DefaultLifetimeMinutes)) return DefaultLifetimeMinutes;
        var below = offered.Where(m => m <= DefaultLifetimeMinutes).ToList();
        return below.Count > 0 ? below.Max() : offered.FirstOrDefault();
    }
}
