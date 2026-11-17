using System.Globalization;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>Builds the signer cards for signing and creation rooms (specs/018-multisig-ux-polish, FR-001/FR-017).</summary>
public static class SignerCards
{
    /// <param name="isOnline">Smoothed presence (see <see cref="PresenceDebouncer"/>); when null the raw flag is used.</param>
    public static IReadOnlyList<SignerCardModel> Build(IReadOnlyList<SignerInput> inputs, string? localKey, Func<string, bool>? isOnline = null)
    {
        var duplicated = inputs
            .GroupBy(i => Normalise(i.DisplayName))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        return inputs.Select(i =>
        {
            var online = isOnline?.Invoke(i.PublicKeyHex) ?? i.Online;
            return new SignerCardModel(
                i.PublicKeyHex,
                i.DisplayName,
                IsYou: !string.IsNullOrEmpty(localKey) && string.Equals(i.PublicKeyHex, localKey, StringComparison.OrdinalIgnoreCase),
                InitialsOf(i.DisplayName),
                duplicated.Contains(Normalise(i.DisplayName)) ? Fingerprint(i.PublicKeyHex) : null,
                online ? SignerPresence.Online : SignerPresence.Offline,
                i.Status,
                i.IsCreator);
        }).ToList();
    }

    /// <summary>
    /// Creation rooms drop a disconnected co-signer, so there is no Offline state here: joined co-signers show Joined or
    /// Ready, and co-signers who have not arrived show as "Not joined yet" slots.
    /// </summary>
    public static IReadOnlyList<SignerCardModel> ForCreationRoom(IReadOnlyList<SignerInput> joined, string? localKey, int totalSigners)
    {
        var cards = Build(joined, localKey)
            .Select(c => c with { Presence = null })
            .ToList();

        for (var i = cards.Count; i < totalSigners; i++)
            cards.Add(new SignerCardModel("", "Not joined yet", false, "?", null, null, SignerStatus.NotJoined, false));

        return cards;
    }

    /// <summary>Up to two initials from the first letters of the first two words; "?" when there is no letter to use.</summary>
    public static string InitialsOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var initials = new List<string>();
        foreach (var word in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!char.IsLetter(word, 0)) continue;
            var first = StringInfo.GetNextTextElement(word);
            initials.Add(first.ToUpperInvariant());
            if (initials.Count == 2) break;
        }
        return initials.Count == 0 ? "?" : string.Concat(initials);
    }

    private static string Normalise(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Last four characters of the key; the first ones are the same for every compressed key.</summary>
    private static string Fingerprint(string key) => key.Length <= 4 ? key : key[^4..];
}
