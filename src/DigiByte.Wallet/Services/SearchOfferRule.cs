namespace DigiByte.Wallet.Services;

/// <summary>How a wallet came to be on this device (spec 027, data-model.md section 4).</summary>
public enum WalletOrigin
{
    /// <summary>Not recorded: the wallet was on the device before this was tracked. Never stored.</summary>
    Unknown,
    /// <summary>Created on this device from a newly generated recovery phrase.</summary>
    Created,
    /// <summary>Restored from an existing recovery phrase.</summary>
    Restored,
    /// <summary>Added as watch-only from an account public key.</summary>
    WatchOnly,
}

/// <summary>What the person has done about the "search further" offer for one wallet.</summary>
public enum SearchOfferAnswer
{
    /// <summary>Nothing yet. Never stored.</summary>
    None,
    /// <summary>They dismissed it.</summary>
    Dismissed,
    /// <summary>A wider search has finished for the wallet.</summary>
    Searched,
}

/// <summary>
/// Decides whether Home offers the wider search (spec 027, FR-001 to FR-006). Pure.
/// </summary>
public static class SearchOfferRule
{
    /// <summary>
    /// True when all of these hold: the wallet has further receive addresses to search; it was not created on this device;
    /// a completed load, taken after its address search settled, found nothing (<see cref="FirstReceiveState.Empty"/>); and
    /// the person has not answered. A wallet from before origins were recorded counts as possibly restored (FR-002b).
    /// </summary>
    public static bool ShouldShow(string? walletType, WalletOrigin origin, FirstReceiveState state, SearchOfferAnswer answer) =>
        walletType is "hd" or "xpub"
        && origin != WalletOrigin.Created
        && state == FirstReceiveState.Empty
        && answer == SearchOfferAnswer.None;
}
