namespace DigiByte.Api.Services;

/// <summary>
/// Settings of the oracle relay (specs/014-digibyte-io-oracle). The base address is configuration, never input from a caller, so the route can
/// only ever ask the one place it is set to.
/// </summary>
public sealed class OracleOptions
{
    public const string SectionName = "Oracle";

    /// <summary>Switches the whole route off (it then answers 404). On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the oracle data is read from, without a trailing slash.</summary>
    public string BaseUrl { get; set; } = "https://digibyte.io/api";

    /// <summary>The base address as an address, or false when it is not https (or http on a loopback address, for development).</summary>
    public bool TryGetBase(out string baseUrl)
    {
        baseUrl = "";
        if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri))
            return false;
        var allowed = uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        if (!allowed)
            return false;
        baseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    /// <summary>The route exists only when it is switched on and its base address is acceptable.</summary>
    public bool IsUsable => Enabled && TryGetBase(out _);
}
