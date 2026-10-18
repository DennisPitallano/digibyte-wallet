using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The two independent DigiDollar runtime switches (FR-022), read from <c>DigiDollar:ViewEnabled</c> and
/// <c>DigiDollar:SendEnabled</c> in wwwroot/appsettings.json (public, non-secret). View can be on with Send off.
/// Effective visibility (network, wallet type, reachability) is computed by <see cref="DigiDollarVisibility"/>.
/// </summary>
public record DigiDollarFeatureFlag(bool ViewEnabled, bool SendEnabled)
{
    public DigiDollarFlags Flags => new(ViewEnabled, SendEnabled);
}
