using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>
/// The one place pages ask about the "search further" offer (spec 027): whether Home shows it for a wallet, and what the
/// person did about it. It applies <see cref="SearchOfferRule"/> to the wallet's stored first-receive state, its origin and
/// its answer. Nothing here touches keys or the network, and nothing here throws: a problem hides the offer.
/// </summary>
public sealed class SearchOfferCoordinator(FirstReceiveStore firstReceive, SearchOfferStore store)
{
    /// <summary>Raised after the offer is dismissed or a search is recorded as finished, so an offer on screen can hide.</summary>
    public event Action? Changed;

    /// <summary>
    /// True when Home should offer the wider search for this wallet. The first-receive state is the stored one, so this only
    /// turns true once a completed history load has recorded the wallet as empty (FR-002).
    /// </summary>
    public async Task<bool> ShouldShowAsync(string? walletId, string? walletType)
    {
        if (string.IsNullOrEmpty(walletId)) return false;
        try
        {
            return SearchOfferRule.ShouldShow(
                walletType,
                await store.GetOriginAsync(walletId),
                await firstReceive.GetAsync(walletId),
                await store.GetAnswerAsync(walletId));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The person dismissed the offer: it does not come back for this wallet (FR-005).</summary>
    public Task DismissAsync(string? walletId) => AnswerAsync(walletId, SearchOfferAnswer.Dismissed);

    /// <summary>A wider search finished for this wallet, whatever it found: the offer has done its job (FR-006).</summary>
    public Task MarkSearchedAsync(string? walletId) => AnswerAsync(walletId, SearchOfferAnswer.Searched);

    /// <summary>Records how the wallet came to be on this device, once (FR-002a). Never throws.</summary>
    public async Task MarkOriginAsync(string? walletId, WalletOrigin origin)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        try { await store.SetOriginAsync(walletId, origin); } catch { }
    }

    private async Task AnswerAsync(string? walletId, SearchOfferAnswer answer)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        try { await store.SetAnswerAsync(walletId, answer); } catch { }
        Changed?.Invoke();
    }
}
