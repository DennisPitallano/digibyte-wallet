namespace DigiByte.Server.Shared;

/// <summary>Why an outbound destination was refused. Only these short categories ever reach a client (resolved IPs and DNS details stay in the logs).</summary>
public enum OutboundReason
{
    InvalidUrl,
    MustUseHttps,
    AddressNotAllowed,
    RedirectsNotAllowed,
    SiteUnreachable,
}

public static class OutboundReasonExtensions
{
    public static string ToText(this OutboundReason reason) => reason switch
    {
        OutboundReason.InvalidUrl => "invalid request",
        OutboundReason.MustUseHttps => "must use https",
        OutboundReason.AddressNotAllowed => "address not allowed",
        OutboundReason.RedirectsNotAllowed => "redirects not allowed",
        OutboundReason.SiteUnreachable => "site unreachable",
        _ => "invalid request",
    };
}

/// <summary>Thrown from the connect-time check when a hostname resolves to an address the policy forbids. <see cref="Detail"/> is for server logs only.</summary>
public sealed class OutboundBlockedException(string detail) : IOException("destination not allowed")
{
    public string Detail { get; } = detail;
}
