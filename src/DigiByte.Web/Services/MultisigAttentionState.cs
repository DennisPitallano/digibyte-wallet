using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Web.Services;

/// <summary>One of the active wallet's shared wallets, as the menu, the switcher and the home shortcut need it.</summary>
public sealed record SharedWalletEntry(string Id, string Name, MultisigWalletConfig? Config);

/// <summary>
/// The one owner of "how many multisig payments need you" for the active wallet, so the Pending tab, the More tab,
/// the Multisig row and the home shortcut can never disagree (specs/026-multisig-wallet-experience, FR-004, FR-010a).
/// Counts come from payments already saved on this device: no network call is made. When the saved data cannot be
/// read, nothing is shown rather than a wrong number.
/// </summary>
public sealed class MultisigAttentionState : IDisposable
{
    private static readonly AttentionSummary None = new([]);

    private readonly MultisigWalletService _multisig;
    private readonly ActiveWalletContext _active;
    private readonly WalletService _wallets;
    private readonly AppState _app;

    // What the last refresh was for. AppState.OnChange carries no detail, so these are kept and compared.
    private string? _loadedWalletId;
    private bool _loadedUnlocked;
    private bool _everLoaded;
    private int _version;

    public MultisigAttentionState(MultisigWalletService multisig, ActiveWalletContext active, WalletService wallets, AppState app)
    {
        _multisig = multisig;
        _active = active;
        _wallets = wallets;
        _app = app;
        _multisig.PendingChanged += OnPendingChanged;
        _app.OnChange += OnAppChanged;
    }

    /// <summary>Counts for the active wallet's shared wallets. Empty until the first refresh.</summary>
    public AttentionSummary Current { get; private set; } = None;

    /// <summary>The active wallet's shared wallets, in the order the service lists them.</summary>
    public IReadOnlyList<SharedWalletEntry> Wallets { get; private set; } = [];

    /// <summary>Raised after every refresh.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads when nothing is loaded yet, or when what is loaded was for another wallet or another unlock state.
    /// Otherwise returns at once: saved-payment changes are followed through events.
    /// </summary>
    public Task EnsureLoadedAsync() => IsCurrent ? Task.CompletedTask : RefreshAsync();

    private bool IsCurrent => _everLoaded && _loadedWalletId == _active.WalletId && _loadedUnlocked == _wallets.IsUnlocked;

    public async Task RefreshAsync()
    {
        var version = ++_version;
        var owner = _active.WalletId;
        var unlocked = _wallets.IsUnlocked;
        var summary = None;
        IReadOnlyList<SharedWalletEntry> entries = [];

        try
        {
            if (!string.IsNullOrEmpty(owner))
            {
                var key = _active.PublicKey;
                var list = new List<SharedWalletEntry>();
                var pending = new List<PendingMultisigTransaction>();
                foreach (var (id, name) in await _multisig.ListWalletsAsync(owner, key))
                {
                    var config = await _multisig.GetConfigAsync(id, owner, key);
                    list.Add(new SharedWalletEntry(id, config?.DisplayName ?? name, config));
                    try { pending.AddRange(await _multisig.GetPendingTransactionsAsync(id)); }
                    catch { /* a wallet whose payments can't be read counts nothing */ }
                }
                var keys = list.ToDictionary(w => w.Id, w => w.Config?.CoSigners.FirstOrDefault(c => c.IsLocal)?.PublicKeyHex);
                summary = AttentionCounter.Count(pending, id => keys.GetValueOrDefault(id));
                entries = list;
            }
        }
        catch
        {
            summary = None;
            entries = [];
        }

        // A newer refresh started while this one was reading: its result is the one to keep.
        if (version != _version) return;

        _loadedWalletId = owner;
        _loadedUnlocked = unlocked;
        _everLoaded = true;
        Current = summary;
        Wallets = entries;
        Changed?.Invoke();
    }

    private void OnPendingChanged(string walletId) => _ = RefreshAsync();

    private void OnAppChanged()
    {
        if (IsCurrent) return;
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _multisig.PendingChanged -= OnPendingChanged;
        _app.OnChange -= OnAppChanged;
    }
}
