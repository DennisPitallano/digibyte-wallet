using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The two independent DigiDollar runtime switches (FR-022), read from <c>DigiDollar:ViewEnabled</c> and
/// <c>DigiDollar:SendEnabled</c> in wwwroot/appsettings.json (public, non-secret). A third switch,
/// <c>DigiDollar:OracleEnabled</c> (default on), shows or hides every oracle price and status display; turn it off when the
/// backend has no oracle data (the explorer backend). View can be on with Send off.
/// Effective visibility (network, wallet type, reachability) is computed by <see cref="DigiDollarVisibility"/>.
/// </summary>
public record DigiDollarFeatureFlag(bool ViewEnabled, bool SendEnabled, bool OracleEnabled = true)
{
    public DigiDollarFlags Flags => new(ViewEnabled, SendEnabled);
}
