using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// The order signers are listed in a room: the viewer first, then who has signed, then who is here, then who is away
/// (specs/026-multisig-wallet-experience, FR-030). Applied after <see cref="SignerCards"/> and the presence
/// smoothing, so neither is changed by it.
/// </summary>
public static class SignerOrder
{
    public static IReadOnlyList<SignerCardModel> Sort(IEnumerable<SignerCardModel> cards) =>
        // OrderBy is stable: inside each group the co-signers keep the order they were given in.
        cards.OrderBy(Rank).ToList();

    private static int Rank(SignerCardModel card)
    {
        if (card.IsYou) return 0;
        if (card.Status == SignerStatus.Signed) return 1;
        return card.Presence == SignerPresence.Online ? 2 : 3;
    }
}
