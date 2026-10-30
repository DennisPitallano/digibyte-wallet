namespace DigiByte.Web.Services;

/// <summary>
/// The project's current donation address, in one place. The Donation page, the Sponsors "Add me" flow and the API's donation check
/// (<c>Sponsors:DonationAddresses</c> in the API settings) must all agree on it.
/// </summary>
public static class DonationInfo
{
    public const string CurrentAddress = "dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa";

    /// <summary>The smallest donation that earns a place on the Sponsors list, in whole DGB (the API enforces the same minimum).</summary>
    public const int MinimumDgb = 1;

    /// <summary>A BIP21 payment link for the donation address with the minimum amount filled in, for the QR code.</summary>
    public static string Bip21Uri(int amountDgb = MinimumDgb) => $"digibyte:{CurrentAddress}?amount={amountDgb}";
}
