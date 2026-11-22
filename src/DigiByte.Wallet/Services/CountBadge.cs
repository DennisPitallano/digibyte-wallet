namespace DigiByte.Wallet.Services;

/// <summary>The text of a count bubble on the menu and on cards (specs/026-multisig-wallet-experience, FR-004).</summary>
public static class CountBadge
{
    /// <summary>The highest number shown as it is; anything above reads "99+".</summary>
    public const int Max = 99;

    /// <summary>An empty string when there is nothing to show, the number up to <see cref="Max"/>, "99+" above it.</summary>
    public static string Text(int count) => count <= 0 ? "" : count > Max ? Max + "+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
