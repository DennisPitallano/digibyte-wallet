namespace DigiByte.Server.Shared;

/// <summary>
/// Rules for an outbound request to a URL somebody else supplied (Digi-ID callback target, DigiPay webhook).
/// specs/008-security-hardening, data-model.md. Defaults are strict; only <see cref="AllowLoopback"/> is a development switch.
/// </summary>
public sealed class OutboundUrlPolicy
{
    /// <summary>Development only: permits loopback addresses (and nothing else). Bound from <c>Outbound:AllowLoopback</c>; default false.</summary>
    public bool AllowLoopback { get; init; }

    /// <summary>Ports the destination may use. Webhooks: 443, 8443, 8080, 8000. Digi-ID callbacks: 443 only.</summary>
    public int[] AllowedPorts { get; init; } = [443];

    public int TimeoutSeconds { get; init; } = 10;

    public int MaxResponseBytes { get; init; } = 65536;

    public static OutboundUrlPolicy Webhook(bool allowLoopback = false) =>
        new() { AllowLoopback = allowLoopback, AllowedPorts = [443, 8443, 8080, 8000] };

    public static OutboundUrlPolicy DigiId(bool allowLoopback = false) =>
        new() { AllowLoopback = allowLoopback, AllowedPorts = [443] };
}
