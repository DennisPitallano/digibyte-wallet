namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Server switch for the DigiDollar surfaces of specs/033: DD on the POS till, payment links and one-off payments,
/// the buyer's DGB/DD choice at checkout, and DD donations. Off by default. It is turned on in production only once
/// spec 035 (DigiDollar on the merchant's own key, watched on the public explorer) is live there
/// (033 FR-032, research R8). Explicit API sessions with <c>currency: "DD"</c> are not affected.
/// </summary>
public sealed class DdSurfacesGate(IConfiguration config)
{
    /// <summary>Configuration key; <c>DIGIPAY_DD_SURFACES</c> overrides it, like the other Railway env vars.</summary>
    public const string ConfigKey = "DigiPay:DdSurfacesEnabled";
    public const string EnvVar = "DIGIPAY_DD_SURFACES";

    public bool Enabled
    {
        get
        {
            if (Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } env)
                return bool.TryParse(env, out var fromEnv) ? fromEnv : env == "1";
            return config.GetValue(ConfigKey, false);
        }
    }
}
