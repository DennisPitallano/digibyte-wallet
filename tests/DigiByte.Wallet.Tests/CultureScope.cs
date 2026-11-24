using System.Globalization;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Runs a test under a given culture and puts the previous one back afterwards, so number-style tests can't leak into
/// other tests (spec 028: the wallet's culture follows the chosen language). Only the current flow's culture changes;
/// the process-wide defaults are never touched, so tests running in parallel are unaffected.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    public CultureScope(string name)
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
