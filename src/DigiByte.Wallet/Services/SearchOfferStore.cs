using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Keeps, per wallet, how it came to be on this device and whether its person has answered the "search further" offer
/// (spec 027, data-model.md section 4). Nothing here is secret. A storage problem never throws to the caller. An origin that
/// cannot be read is <see cref="WalletOrigin.Unknown"/>; an answer that cannot be read is
/// <see cref="SearchOfferAnswer.Dismissed"/>, so a storage problem can hide the offer but never show it again to someone
/// who dismissed it.
/// </summary>
public sealed class SearchOfferStore(ISecureStorage storage)
{
    private const string CreatedValue = "created";
    private const string RestoredValue = "restored";
    private const string WatchOnlyValue = "watch-only";
    private const string DismissedValue = "dismissed";
    private const string SearchedValue = "searched";

    public static string OriginKey(string walletId) => $"walletOrigin:{walletId}";
    public static string AnswerKey(string walletId) => $"searchOffer:{walletId}";

    public async Task<WalletOrigin> GetOriginAsync(string walletId)
    {
        try
        {
            return await storage.GetAsync(OriginKey(walletId)) switch
            {
                CreatedValue => WalletOrigin.Created,
                RestoredValue => WalletOrigin.Restored,
                WatchOnlyValue => WalletOrigin.WatchOnly,
                _ => WalletOrigin.Unknown,
            };
        }
        catch
        {
            return WalletOrigin.Unknown;
        }
    }

    /// <summary>Records the origin once. <see cref="WalletOrigin.Unknown"/> is not written, and a recorded origin is not replaced.</summary>
    public async Task SetOriginAsync(string walletId, WalletOrigin origin)
    {
        var value = origin switch
        {
            WalletOrigin.Created => CreatedValue,
            WalletOrigin.Restored => RestoredValue,
            WalletOrigin.WatchOnly => WatchOnlyValue,
            _ => null,
        };
        if (value is null) return;

        try
        {
            if (await storage.GetAsync(OriginKey(walletId)) is not null) return;
            await storage.SetAsync(OriginKey(walletId), value);
        }
        catch { }
    }

    public async Task<SearchOfferAnswer> GetAnswerAsync(string walletId)
    {
        try
        {
            return await storage.GetAsync(AnswerKey(walletId)) switch
            {
                null => SearchOfferAnswer.None,
                SearchedValue => SearchOfferAnswer.Searched,
                _ => SearchOfferAnswer.Dismissed,
            };
        }
        catch
        {
            return SearchOfferAnswer.Dismissed;
        }
    }

    /// <summary>Records the answer. <see cref="SearchOfferAnswer.None"/> is not written, so an answer never goes back to none.</summary>
    public async Task SetAnswerAsync(string walletId, SearchOfferAnswer answer)
    {
        var value = answer switch
        {
            SearchOfferAnswer.Dismissed => DismissedValue,
            SearchOfferAnswer.Searched => SearchedValue,
            _ => null,
        };
        if (value is null) return;

        try { await storage.SetAsync(AnswerKey(walletId), value); } catch { }
    }
}
