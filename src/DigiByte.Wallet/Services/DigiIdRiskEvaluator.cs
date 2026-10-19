namespace DigiByte.Wallet.Services;

public enum DigiIdRisk { None, NewSite, InsecureSite }

/// <summary>
/// Decides when a Digi-ID sign-in deserves a guardian warning: the site uses plain HTTP, or the user has never
/// signed in there before (phishing sites are always new). Pure and deterministic. Domains are compared exactly
/// (ignoring case), so a lookalike or a subdomain is never treated as a known site.
/// </summary>
public static class DigiIdRiskEvaluator
{
    public static DigiIdRisk Evaluate(string domain, bool isUnsecure, IEnumerable<string> knownDomains)
    {
        if (isUnsecure) return DigiIdRisk.InsecureSite;

        var key = (domain ?? "").Trim();
        if (key.Length == 0) return DigiIdRisk.NewSite;

        var known = knownDomains.Any(d => string.Equals(d?.Trim(), key, StringComparison.OrdinalIgnoreCase));
        return known ? DigiIdRisk.None : DigiIdRisk.NewSite;
    }
}
