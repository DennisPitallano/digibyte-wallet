using System.Text.Json;
using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Services;

public class WalletService : IWalletService, IDdSessionProvider, IDdFeeSource, IPsbtKeyProvider, IGatewayWalletScopeProvider, IRequestAddressSource
{
    private readonly IKeyStore _keyStore;
    private readonly WalletKeyStore _walletStore;
    private readonly ICryptoService _crypto;
    private readonly IBlockchainService _blockchain;
    private readonly TransactionTracker _txTracker;
    private readonly DigiIdSweepStore? _digiIdSweeps;
    private readonly ProtectedSecretStore _vault;
    private readonly ICommonPasswordList? _passwordList;

    // Multi-wallet session: holds all unlocked wallets in memory
    private class UnlockedWalletSession
    {
        public WalletInfo Info { get; set; } = default!;
        public HdKeyDerivation? Hd { get; set; }
        public Key? SingleKey { get; set; }

        /// <summary>The chain lookup for used change and receive addresses, shared by callers. Never faults. Cleared if it fails, so it runs again.</summary>
        public Task? AddressDiscovery { get; set; }

        /// <summary>A failed discovery does not start again before this time.</summary>
        public DateTime AddressDiscoveryRetryAfter { get; set; }

        /// <summary>The wider search of receive addresses now running for this wallet, if any: a second caller joins it.</summary>
        public Task<ReceiveSearchResult>? ReceiveSearch { get; set; }

        /// <summary>The cancel token of that search: once it is cancelled, a new caller waits for it to end and starts afresh.</summary>
        public CancellationToken ReceiveSearchToken { get; set; }

        /// <summary>
        /// One at a time for anything that moves the address on show (a new address, a Digi-ID sweep). Two overlapping moves
        /// whose saves both failed would each put back what the other had set, leaving an address on show that nothing records.
        /// </summary>
        public SemaphoreSlim AddressOnShowGate { get; } = new(1, 1);
    }

    private readonly Dictionary<string, UnlockedWalletSession> _sessions = new();
    private string? _activeWalletId;
    private string _networkMode = "testnet";

    // Address derivation cache — avoid repeated elliptic curve math. Each cache remembers
    // which wallet and how many receive and change addresses it was built for.
    private (string? WalletId, string Receive, int ChangeCount)? _cachedAddressesFor;
    private List<string>? _cachedAddresses;
    /// <summary>Index in <see cref="_cachedAddresses"/> where an HD wallet's change addresses start (receive addresses come first).</summary>
    private int _cachedChangeStart = int.MaxValue;
    private (string? WalletId, string Receive, int ChangeCount)? _cachedKeyMapFor;
    private Dictionary<string, ExtKey>? _cachedAddressKeyMap;
    private const int UnscannedChangeLimit = 100;
    private string? _unscannedChangeWalletId;
    private HashSet<string>? _unscannedChangeAddresses;

    private const int MaxWallets = 10;

    /// <summary>Change indexes always scanned. Only these are scanned in Legacy format too: the wallet's own change is SegWit.</summary>
    private const int MinScannedChange = 10;

    /// <summary>Consecutive unused change addresses that end the search for used ones (the usual BIP44 gap limit).</summary>
    private const int ChangeGapLimit = 20;

    /// <summary>Receive indexes always scanned. Only these are scanned in Legacy format too: new receive addresses are SegWit.</summary>
    private const int MinScannedReceive = 20;

    /// <summary>
    /// Consecutive unused receive addresses that end the search for used ones. It is also the most unused addresses
    /// the wallet will hand out in a row, so that a wallet restored from its seed can find every address that was paid.
    /// </summary>
    public const int ReceiveGapLimit = 20;

    /// <summary>How far past the last used address a restore through the light-client gateway looks when asked to look deep.</summary>
    public const int DeepScanGapLimit = 500;

    /// <summary>
    /// How far one automatic search may go past its starting index. A source that shows history for every address (a mock, a
    /// faulty explorer) would otherwise keep it going forever. Payments further along are found by
    /// <see cref="SearchReceiveAddressesAsync"/>, which the user starts.
    /// </summary>
    private const int MaxAddressSearch = 500;

    /// <summary>Address lookups run in groups of this size, so a long search keeps the page responsive and its progress moving.</summary>
    private const int LookupBatchSize = 50;

    /// <summary>How long a balance or history read waits for discovery before answering from the saved scan.</summary>
    public TimeSpan AddressDiscoveryWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a send waits for discovery. Longer, because it decides the change address and the coins to spend.</summary>
    public TimeSpan AddressDiscoverySendWait { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How long after a failed discovery before another starts, so an outage doesn't repeat its lookups on every refresh.</summary>
    public TimeSpan AddressDiscoveryRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    private UnlockedWalletSession? ActiveSession =>
        _activeWalletId != null && _sessions.TryGetValue(_activeWalletId, out var s) ? s : null;

    // Convenience accessors that delegate to the active session
    private HdKeyDerivation? _hd => ActiveSession?.Hd;
    private Key? _singleKey => ActiveSession?.SingleKey;

    public bool IsUnlocked => ActiveSession != null;
    public WalletInfo? ActiveWallet => ActiveSession?.Info;

    /// <summary>
    /// The DGB outputs that can pay a DigiDollar transaction fee (see <see cref="IDdFeeSource"/>): P2WPKH outputs of the
    /// active seed-based wallet's receive and change addresses, each with its signing key (kept in memory only).
    /// </summary>
    public async Task<IReadOnlyList<DdFeeUtxo>> GetFeeUtxosAsync(CancellationToken ct)
    {
        EnsureUnlocked();
        if (_hd is null || _hd.IsWatchOnly) return [];
        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);

        var network = CurrentNetwork;
        var targets = new List<(string Address, ExtKey Key, Script Script)>();
        foreach (var (address, key) in GetAddressKeyMap())
        {
            var script = BitcoinAddress.Create(address, network).ScriptPubKey;
            // Native SegWit only: the key must match the P2WPKH script it is supposed to unlock.
            if (PayToWitPubKeyHashTemplate.Instance.CheckScriptPubKey(script)
                && key.PrivateKey.PubKey.WitHash.ScriptPubKey == script)
                targets.Add((address, key, script));
        }

        var semaphore = new SemaphoreSlim(10);
        var lookups = targets.Select(async t =>
        {
            await semaphore.WaitAsync(ct);
            try { return (t.Key, Utxos: await _blockchain.GetUtxosAsync(t.Address)); }
            finally { semaphore.Release(); }
        });

        var result = new List<DdFeeUtxo>();
        foreach (var (key, utxos) in await Task.WhenAll(lookups))
            foreach (var u in utxos)
                result.Add(new DdFeeUtxo(u.TxId, u.OutputIndex, u.AmountSatoshis, u.Confirmations, key.PrivateKey));
        return result;
    }

    /// <summary>Where leftover DGB goes after a DigiDollar fee: the wallet's next change address (SegWit).</summary>
    public Task<Script> GetDgbChangeScriptAsync(CancellationToken ct)
    {
        EnsureUnlocked();
        if (_hd is null || _hd.IsWatchOnly || ActiveWallet is null)
            throw new InvalidOperationException("DigiDollar needs an unlocked seed-based wallet.");
        var key = _hd.DeriveChangeKey(ActiveWallet.NextChangeIndex);
        return Task.FromResult(_hd.GetAddress(key).ScriptPubKey);
    }

    public Task MarkDgbChangeUsedAsync() => AdvanceChangeIndexAsync();

    // ─── PSBT signing (IPsbtKeyProvider, specs/007-psbt-signing) ───

    /// <summary>False when locked or watch-only.</summary>
    public bool CanSign => ActiveSession is { } session
        && (session.SingleKey != null || session.Hd is { IsWatchOnly: false });

    Network IPsbtKeyProvider.Network => EffectiveNetwork;

    /// <summary>
    /// The scripts of the addresses this wallet scans, after waiting a limited time for discovery, so a PSBT loaded straight
    /// after unlock still recognizes coins on newly found addresses. Public keys only: works for watch-only wallets.
    /// </summary>
    public async Task<IReadOnlyDictionary<Script, string>> GetOwnedScriptsAsync()
    {
        EnsureUnlocked();
        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
        var network = EffectiveNetwork;
        var owned = new Dictionary<Script, string>();
        foreach (var address in GetAllAddresses())
            owned[BitcoinAddress.Create(address, network).ScriptPubKey] = address;
        return owned;
    }

    /// <summary>
    /// Signs a copy, one input at a time, for listed indexes whose script is one of this wallet's addresses. A script-based
    /// input that merely contains the wallet's key (multisig, wrapped SegWit) has a different script, so it is never signed.
    /// </summary>
    public PSBT SignInputs(PSBT psbt, IReadOnlyList<int> inputIndexes)
    {
        if (!CanSign)
            throw new InvalidOperationException("This wallet can't sign: it's locked or watch-only.");

        var network = EffectiveNetwork;
        var keysByScript = new Dictionary<Script, Key>();
        if (_singleKey is { } single)
        {
            keysByScript[single.PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit)] = single;
            keysByScript[single.PubKey.GetScriptPubKey(ScriptPubKeyType.Legacy)] = single;
        }
        else
        {
            foreach (var (address, key) in GetAddressKeyMap())
                keysByScript[BitcoinAddress.Create(address, network).ScriptPubKey] = key.PrivateKey;
        }

        var keysByIndex = new Dictionary<int, Key>();
        foreach (var index in inputIndexes)
        {
            if (index < 0 || index >= psbt.Inputs.Count) continue;
            var script = psbt.Inputs[index].GetTxOut()?.ScriptPubKey;
            if (script is not null && keysByScript.TryGetValue(script, out var key))
                keysByIndex[index] = key;
        }
        return DigiByte.Crypto.Psbt.PsbtSigner.SignInputs(psbt, keysByIndex);
    }

    /// <summary>
    /// Checks a PIN without unlocking: unlike <see cref="UnlockWalletAsync"/>, the session and active wallet don't change
    /// (research R7). Clears the decrypted bytes before returning.
    /// </summary>
    public async Task<bool> VerifyPinAsync(string walletId, string pin)
    {
        var opened = await OpenSeedAsync(walletId, pin);
        if (!opened.Success)
            return false;

        Array.Clear(opened.Value!);
        return true;
    }

    /// <summary>
    /// The active wallet as DigiDollar sees it (see <see cref="IDdSessionProvider"/>). A derivation is returned only for an
    /// unlocked seed-based wallet on mainnet or testnet (FR-029, FR-030).
    /// </summary>
    public DdWalletSession? GetDigiDollarSession()
    {
        var session = ActiveSession;
        if (session is null) return null;

        var kind = DigiDollarVisibility.KindFromWalletType(session.Info.WalletType);
        var network = _networkMode;
        var canDerive = kind == DdWalletKind.Seed
                        && session.Hd is { IsWatchOnly: false }
                        && DigiDollarVisibility.IsSupportedNetwork(network);

        return new DdWalletSession(
            session.Info.Id, kind, network,
            canDerive ? session.Hd!.CreateDigiDollarDerivation(network) : null);
    }

    /// <summary>
    /// Returns the hex-encoded compressed public key for the primary receiving address (index 0).
    /// Used for multisig co-signer identification.
    /// </summary>
    public string? GetPublicKey()
    {
        if (_hd != null)
            return _hd.DeriveReceivingKey(0).GetPublicKey().ToHex();
        if (_singleKey != null)
            return _singleKey.PubKey.ToHex();
        return null;
    }

    /// <summary>
    /// Returns the hex-encoded private key for multisig signing (index 0 receiving key).
    /// Caller must handle securely.
    /// </summary>
    public string? GetPrivateKeyForMultisig()
    {
        if (_hd != null)
            return Convert.ToHexString(_hd.DeriveReceivingKey(0).PrivateKey.ToBytes()).ToLowerInvariant();
        if (_singleKey != null)
            return Convert.ToHexString(_singleKey.ToBytes()).ToLowerInvariant();
        return null;
    }

    /// <param name="sweepStore">
    /// Where Digi-ID recovery sweeps are remembered. Optional so that code that builds a wallet without it keeps working; the
    /// Digi-ID recovery methods then refuse to run.
    /// </param>
    private readonly WalletSyncStateStore? _syncStates;
    private readonly GatewayBootstrapCoordinator? _gatewayBootstrap;
    private readonly GatewayOwnSendRecorder? _ownSends;
    private readonly GatewayRestoreServices? _gatewayRestore;
    private readonly WalletHistorySync? _historySync;
    private readonly HistorySyncOptions? _historyOptions;

    public WalletService(WalletKeyStore walletStore, ICryptoService crypto, IBlockchainService blockchain, TransactionTracker txTracker,
        DigiIdSweepStore? sweepStore = null, TimeProvider? time = null, IMigrationLock? migrationLock = null, ICommonPasswordList? passwordList = null,
        WalletSyncStateStore? syncStateStore = null, GatewayBootstrapCoordinator? gatewayBootstrap = null,
        GatewayOwnSendRecorder? ownSendRecorder = null, GatewayRestoreServices? gatewayRestore = null,
        WalletHistorySync? historySync = null, HistorySyncOptions? historyOptions = null)
    {
        _historySync = historySync;
        _historyOptions = historyOptions;
        _keyStore = walletStore;
        _walletStore = walletStore;
        _crypto = crypto;
        _blockchain = blockchain;
        _txTracker = txTracker;
        _digiIdSweeps = sweepStore;
        _vault = new ProtectedSecretStore(walletStore.Storage, crypto, time, migrationLock);
        _passwordList = passwordList;
        _syncStates = syncStateStore;
        _gatewayBootstrap = gatewayBootstrap;
        _ownSends = ownSendRecorder;
        _gatewayRestore = gatewayRestore;
    }

    public void SetNetwork(bool isTestnet)
    {
        _networkMode = isTestnet ? "testnet" : "mainnet";
        if (_blockchain is BlockchainApiService apiService)
            apiService.SetNetwork(isTestnet);
        else if (_blockchain is FallbackBlockchainService fallbackService)
            fallbackService.SetNetwork(isTestnet);
    }

    /// <summary>
    /// Set network mode: "mainnet", "testnet", or "regtest"
    /// </summary>
    public void SetNetworkMode(string mode)
    {
        _networkMode = mode;
        if (_blockchain is FallbackBlockchainService fallbackService)
            fallbackService.SetNetworkMode(mode);
        else if (_blockchain is BlockchainApiService apiService)
            apiService.SetNetwork(mode == "testnet");
    }

    public Network CurrentNetworkPublic => EffectiveNetwork;

    /// <summary>
    /// For privatekey wallets, use the network detected from the WIF key.
    /// For HD wallets, use the user-selected network.
    /// For privatekey/xpub wallets, use the network detected from the key prefix.
    /// For HD wallets, use the user-selected network.
    /// </summary>
    private Network EffectiveNetwork =>
        ActiveWallet?.WalletType is "privatekey" or "xpub" && ActiveWallet.WifNetwork != null
            ? ActiveWallet.WifNetwork switch
            {
                "mainnet" => DigiByteNetwork.Mainnet,
                "testnet" => DigiByteNetwork.Testnet,
                "regtest" => DigiByteNetwork.Regtest,
                _ => CurrentNetwork,
            }
            : CurrentNetwork;

    private Network CurrentNetwork => _networkMode switch
    {
        "regtest" => DigiByteNetwork.Regtest,
        "testnet" => DigiByteNetwork.Testnet,
        _ => DigiByteNetwork.Mainnet,
    };

    public async Task<WalletInfo> CreateWalletAsync(string name, string mnemonic, string pin, string? color = null, CredentialKind kind = CredentialKind.Pin, bool allowLegacyProtection = false)
    {
        var count = await _walletStore.GetWalletCountAsync();
        if (count >= MaxWallets)
            throw new InvalidOperationException($"Maximum of {MaxWallets} wallets reached. Delete an existing wallet first.");

        var walletId = Guid.NewGuid().ToString("N");

        RequireAcceptableCredential(pin, kind);
        var protection = await StoreNewSecretAsync(walletId, mnemonic, pin, kind, allowLegacyProtection);

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            CredentialType = CredentialTypeName(kind),
            ProtectionLevel = protection,
            Color = color ?? WalletInfo.ColorPalette[count % WalletInfo.ColorPalette.Length]
        };

        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(wallet));
        await _walletStore.SetActiveWalletIdAsync(walletId);

        var parsedMnemonic = MnemonicGenerator.FromWords(mnemonic);
        var session = new UnlockedWalletSession
        {
            Info = wallet,
            Hd = new HdKeyDerivation(parsedMnemonic, network: CurrentNetwork),
        };
        _sessions[walletId] = session;
        _historySync?.ResetSession(walletId);
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);

        return wallet;
    }

    /// <summary>
    /// Create a wallet from a single WIF private key (paper wallet / legacy import).
    /// </summary>
    public async Task<WalletInfo> CreateWalletFromWifAsync(string name, string wif, string pin, string? color = null, CredentialKind kind = CredentialKind.Pin, bool allowLegacyProtection = false)
    {
        var count = await _walletStore.GetWalletCountAsync();
        if (count >= MaxWallets)
            throw new InvalidOperationException($"Maximum of {MaxWallets} wallets reached. Delete an existing wallet first.");

        var walletId = Guid.NewGuid().ToString("N");

        // Auto-detect network from the key prefix (before anything is stored)
        var detectedNetwork = PrivateKeyImporter.DetectNetwork(wif);
        if (detectedNetwork == null)
            throw new InvalidOperationException("Could not detect network from private key.");

        // Protect the WIF with the credential (same pattern as mnemonic)
        RequireAcceptableCredential(pin, kind);
        var protection = await StoreNewSecretAsync(walletId, wif, pin, kind, allowLegacyProtection);

        var networkName = detectedNetwork == DigiByteNetwork.Mainnet ? "mainnet"
            : detectedNetwork == DigiByteNetwork.Testnet ? "testnet" : "regtest";

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            WalletType = "privatekey",
            WifNetwork = networkName,
            CredentialType = CredentialTypeName(kind),
            ProtectionLevel = protection,
            Color = color ?? WalletInfo.ColorPalette[count % WalletInfo.ColorPalette.Length]
        };

        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(wallet));
        await _walletStore.SetActiveWalletIdAsync(walletId);

        var session = new UnlockedWalletSession
        {
            Info = wallet,
            SingleKey = PrivateKeyImporter.ParseWif(wif, detectedNetwork),
        };
        _sessions[walletId] = session;
        _historySync?.ResetSession(walletId);
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);

        return wallet;
    }

    /// <summary>
    /// Create a watch-only wallet from an extended public key (xpub).
    /// Can view balances and receive, but cannot sign or send transactions.
    /// </summary>
    public async Task<WalletInfo> CreateWatchOnlyWalletAsync(string name, string xpub, string pin, string? color = null, CredentialKind kind = CredentialKind.Pin, bool allowLegacyProtection = false)
    {
        var count = await _walletStore.GetWalletCountAsync();
        if (count >= MaxWallets)
            throw new InvalidOperationException($"Maximum of {MaxWallets} wallets reached. Delete an existing wallet first.");

        var walletId = Guid.NewGuid().ToString("N");

        var detectedNetwork = HdKeyDerivation.DetectXpubNetwork(xpub.Trim());
        var extPubKey = HdKeyDerivation.ParseXpub(xpub.Trim())
            ?? throw new InvalidOperationException("Invalid extended public key.");

        var networkName = detectedNetwork == DigiByteNetwork.Mainnet ? "mainnet"
            : detectedNetwork == DigiByteNetwork.Testnet ? "testnet" : "regtest";

        // Protect the xpub with the credential (same pattern as mnemonic/WIF)
        RequireAcceptableCredential(pin, kind);
        var protection = await StoreNewSecretAsync(walletId, xpub.Trim(), pin, kind, allowLegacyProtection);

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            WalletType = "xpub",
            WifNetwork = networkName,
            CredentialType = CredentialTypeName(kind),
            ProtectionLevel = protection,
            Color = color ?? WalletInfo.ColorPalette[count % WalletInfo.ColorPalette.Length]
        };

        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(wallet));
        await _walletStore.SetActiveWalletIdAsync(walletId);

        var session = new UnlockedWalletSession
        {
            Info = wallet,
            Hd = new HdKeyDerivation(extPubKey, detectedNetwork ?? CurrentNetwork),
        };
        _sessions[walletId] = session;
        _historySync?.ResetSession(walletId);
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);

        return wallet;
    }

    public async Task<WalletInfo?> GetWalletAsync(string walletId)
    {
        var json = await _walletStore.GetWalletInfoAsync(walletId);
        return json != null ? JsonSerializer.Deserialize<WalletInfo>(json) : null;
    }

    public async Task<string?> GetActiveWalletIdAsync()
    {
        return await _walletStore.GetActiveWalletIdAsync();
    }

    public async Task<bool> HasWalletAsync()
    {
        var id = await _walletStore.GetActiveWalletIdAsync();
        return id != null;
    }

    public async Task<bool> UnlockWalletAsync(string walletId, string pin)
    {
        var opened = await OpenSeedAsync(walletId, pin);
        if (!opened.Success)
            return false;
        var decrypted = opened.Value!;

        var walletInfo = await GetWalletAsync(walletId);
        var seedString = System.Text.Encoding.UTF8.GetString(decrypted);
        var session = new UnlockedWalletSession { Info = walletInfo! };

        if (walletInfo?.WalletType == "privatekey")
        {
            // WIF-imported wallet — single key, no HD derivation
            var wifNetwork = PrivateKeyImporter.DetectNetwork(seedString) ?? CurrentNetwork;
            session.SingleKey = PrivateKeyImporter.ParseWif(seedString, wifNetwork);
        }
        else if (walletInfo?.WalletType == "xpub")
        {
            // Watch-only wallet — xpub, no private keys (auto-detect network from key prefix)
            var xpubNetwork = HdKeyDerivation.DetectXpubNetwork(seedString) ?? CurrentNetwork;
            var extPubKey = HdKeyDerivation.ParseXpub(seedString);
            if (extPubKey == null) return false;
            session.Hd = new HdKeyDerivation(extPubKey, xpubNetwork);
        }
        else
        {
            // Standard HD wallet from mnemonic
            var mnemonic = MnemonicGenerator.FromWords(seedString);
            session.Hd = new HdKeyDerivation(mnemonic, network: CurrentNetwork);
        }

        _sessions[walletId] = session;
        _historySync?.ResetSession(walletId);
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);
        await _walletStore.SetActiveWalletIdAsync(walletId);
        return true;
    }

    public async Task<string[]?> GetSeedPhraseAsync(string walletId, string pin)
    {
        var opened = await OpenSeedAsync(walletId, pin);
        if (!opened.Success)
            return null;
        var decrypted = opened.Value!;

        var mnemonicWords = System.Text.Encoding.UTF8.GetString(decrypted);
        return mnemonicWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Decrypt the raw seed bytes for a wallet. Used by biometric enrollment.
    /// </summary>
    public async Task<byte[]?> GetDecryptedSeedAsync(string walletId, string pin)
    {
        var opened = await OpenSeedAsync(walletId, pin);
        return opened.Success ? opened.Value : null;
    }

    /// <summary>
    /// True once, right after the wallet was migrated to the new protection by an unlock in this session; used to show
    /// the one-time upgrade notice (the old copy is kept for 7 days).
    /// </summary>
    public bool TakeMigrationNotice(string walletId) => _vault.TakeMigrationNotice(WalletKeyStore.SeedSlot(walletId));

    /// <summary>
    /// Opens a wallet's stored seed with its credential, reading the new format first and falling back to the legacy copy.
    /// A successful open migrates a legacy wallet to the new format (silently; a migration problem never fails the open)
    /// and removes an expired legacy copy. See <see cref="ProtectedSecretStore"/>.
    /// </summary>
    private async Task<CryptoResult<byte[]>> OpenSeedAsync(string walletId, string credential)
    {
        var slot = WalletKeyStore.SeedSlot(walletId);
        if (!await _vault.ExistsAsync(slot))
            return Remember(CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable));

        var info = await GetWalletAsync(walletId);
        var kind = info?.CredentialType == "passphrase" ? CredentialKind.Passphrase : CredentialKind.Pin;
        var opened = await _vault.OpenAsync(slot, credential, kind);

        // A wallet created with legacy protection (the device could not run the stronger one) becomes standard as soon as an
        // unlock on a capable device has upgraded it.
        if (opened.Success && info is { ProtectionLevel: "legacy" } && await _vault.HasV2Async(slot))
        {
            info.ProtectionLevel = "standard";
            await SaveWalletRecordAsync(walletId, info);
            SyncSessionProtection(info);
        }
        return Remember(opened);
    }

    /// <summary>
    /// If the automatic upgrade of this wallet's protection was put off by the device (cannot run it, or out of memory),
    /// says why, once, so the page can show a calm note. The wallet works as before.
    /// </summary>
    public CryptoFailure? TakeMigrationDeferred(string walletId) => _vault.TakeMigrationDeferred(WalletKeyStore.SeedSlot(walletId));

    /// <summary>
    /// Why the most recent unlock, PIN check or seed read failed, or null if it succeeded. Lets a page avoid calling an
    /// environmental problem (out of memory, a newer or unreadable format) a wrong credential, and avoid counting it as
    /// an attempt. See <see cref="CredentialMessages"/>.
    /// </summary>
    public CryptoFailure? LastCredentialFailure { get; private set; }

    private CryptoResult<byte[]> Remember(CryptoResult<byte[]> result)
    {
        LastCredentialFailure = result.Failure;
        return result;
    }

    /// <summary>
    /// Unlock a wallet from pre-decrypted seed bytes (used by biometric unlock).
    /// </summary>
    public async Task<bool> UnlockWalletFromSeedAsync(string walletId, byte[] decryptedSeed)
    {
        var walletInfo = await GetWalletAsync(walletId);
        if (walletInfo == null) return false;

        var seedString = System.Text.Encoding.UTF8.GetString(decryptedSeed);
        var session = new UnlockedWalletSession { Info = walletInfo };

        if (walletInfo.WalletType == "privatekey")
        {
            var wifNetwork = PrivateKeyImporter.DetectNetwork(seedString) ?? CurrentNetwork;
            session.SingleKey = PrivateKeyImporter.ParseWif(seedString, wifNetwork);
        }
        else if (walletInfo.WalletType == "xpub")
        {
            var xpubNetwork = HdKeyDerivation.DetectXpubNetwork(seedString) ?? CurrentNetwork;
            var extPubKey = HdKeyDerivation.ParseXpub(seedString);
            if (extPubKey == null) return false;
            session.Hd = new HdKeyDerivation(extPubKey, xpubNetwork);
        }
        else
        {
            var mnemonic = MnemonicGenerator.FromWords(seedString);
            session.Hd = new HdKeyDerivation(mnemonic, network: CurrentNetwork);
        }

        _sessions[walletId] = session;
        _historySync?.ResetSession(walletId);
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);
        await _walletStore.SetActiveWalletIdAsync(walletId);
        return true;
    }

    public Task LockWalletAsync()
    {
        _sessions.Clear();
        _historySync?.ResetAll();
        _cachedAddressKeyMap = null;
        _activeWalletId = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// What the gateway source needs to know: the active wallet and every address it watches, with the change ones marked. Null when no
    /// wallet is unlocked or when the wallet is not on mainnet (the gateway has a mainnet checkpoint only). Addresses are the only wallet
    /// data that ever leaves the device, and only to the gateway the user configured.
    /// </summary>
    public Task<GatewayWalletScope?> GetScopeAsync()
    {
        if (!IsUnlocked || _activeWalletId is null)
            return Task.FromResult<GatewayWalletScope?>(null);
        if ((ActiveWallet?.WifNetwork ?? _networkMode) != "mainnet")
            return Task.FromResult<GatewayWalletScope?>(null);
        var addresses = GetAllAddresses();
        var changeKeys = GetChangeAddressKeys();
        var change = addresses.Where(a => changeKeys.Contains(AddressKey.Normalize(a))).ToHashSet();
        return Task.FromResult<GatewayWalletScope?>(new GatewayWalletScope(_activeWalletId, addresses.ToList(), change));
    }

    /// <summary>
    /// Puts a transaction this wallet just broadcast into the gateway record at once (FR-009a). Never throws: the send has already
    /// happened and nothing here may change its result.
    /// </summary>
    /// <summary>Remembers the verified block height at which a new wallet started (spec FR-008a). Best effort: never affects creating the wallet.</summary>
    public async Task RecordBirthHeightAsync()
    {
        if (_gatewayRestore is null || _activeWalletId is null || _gatewayBootstrap is not { IsEnabled: true })
            return;
        try
        {
            await _gatewayRestore.Birth.RecordAtCreationAsync(_activeWalletId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gateway] Could not record the birth height: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Restores the unlocked wallet through the light-client gateway (spec FR-008, T095), starting at the height worked out from the date
    /// the user gave, or the recorded birth height. Returns null when the gateway is not used here (feature off, not mainnet, no start
    /// height, any failure): the ordinary explorer discovery then does the restore, so funds are never skipped. A result that says
    /// "use the explorer" carries the explanation to show.
    /// </summary>
    public async Task<RestoreResult?> RestoreViaGatewayAsync(DateTimeOffset? firstUse, bool deep = false, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_gatewayRestore is null || _gatewayBootstrap is not { IsEnabled: true } || !IsUnlocked || _activeWalletId is null || _hd is null)
            return null;
        if ((ActiveWallet?.WifNetwork ?? _networkMode) != "mainnet")
            return null;
        try
        {
            var walletId = _activeWalletId;
            var birth = firstUse is { } date
                ? await _gatewayRestore.Birth.SetFromDateAsync(walletId, date)
                : await _gatewayRestore.Birth.GetAsync(walletId);
            if (birth.Height is not { } start)
                return null;
            var hd = _hd;
            var source = new HdRestoreAddresses(hd);
            return await _gatewayRestore.Scanner.RestoreAsync(walletId, source, start, deep ? DeepScanGapLimit : ReceiveGapLimit,
                deep ? ScanMode.Deep : ScanMode.Normal, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gateway] Restore through the gateway did not finish ({ex.GetType().Name}); the explorer restore carries on.");
            return null;
        }
    }

    private sealed class HdRestoreAddresses(HdKeyDerivation hd) : IRestoreAddressSource
    {
        public string Receive(int index) => hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString();

        public string Change(int index) => hd.DeriveChangeAddress(index, ScriptPubKeyType.Segwit).ToString();
    }

    private async Task RecordOwnSendAsync(byte[] rawTx)
    {
        if (_ownSends is null)
            return;
        try
        {
            if (await GetScopeAsync() is { } scope)
                await _ownSends.RecordAsync(scope, rawTx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gateway] Could not record the send: {ex.GetType().Name}");
        }
    }

    public async Task<WalletBalance> GetBalanceAsync()
    {
        EnsureUnlocked();
        await WaitForAddressDiscoveryAsync(AddressDiscoveryWait);

        // Read with the addresses: a search that ends during the read below must not make this balance look complete.
        var addressesSettled = AddressesSettled;
        var addresses = GetAllAddresses();
        if (ChangeOnlySync)
        {
            try
            {
                var result = await _historySync!.RefreshAsync(_activeWalletId!, HistoryNetwork, addresses, BuildHotHints());
                ReportTransactions(result.Transactions, addresses);
                return new WalletBalance
                {
                    ConfirmedSatoshis = result.ConfirmedSatoshis,
                    UnconfirmedSatoshis = result.PendingSatoshis,
                    FiatCurrency = ActiveWallet!.FiatCurrency,
                    AddressesSettled = addressesSettled,
                };
            }
            catch (SummaryUnavailableException)
            {
                // No source can give summaries right now (regtest, gateway first, every explorer down): read in full, as before.
            }
        }

        var totalSatoshis = await _blockchain.GetBalanceAsync(addresses);

        return new WalletBalance
        {
            ConfirmedSatoshis = totalSatoshis,
            FiatCurrency = ActiveWallet!.FiatCurrency,
            AddressesSettled = addressesSettled,
        };
    }

    /// <summary>Change-only sync applies: wired in, switched on, and the source can give summaries (spec 023).</summary>
    private bool ChangeOnlySync =>
        _historySync is { CanSync: true } && (_historyOptions?.ChangeOnly ?? true) && _activeWalletId is not null;

    /// <summary>The network the saved summaries and history are kept under (used by the /dev/node benchmark too).</summary>
    public string HistoryNetwork => ActiveWallet?.WifNetwork ?? _networkMode;

    /// <summary>
    /// Whether the address has been used, for address discovery. With change-only sync it reads the address's summary (seeding the
    /// refresh that follows, FR-011) without recording a failure as a cooldown; otherwise, or when no source can give summaries,
    /// <see cref="IBlockchainService.HasAddressHistoryAsync"/> as before.
    /// </summary>
    private async Task<bool> IsAddressUsedAsync(string address)
    {
        if (ChangeOnlySync && _blockchain is IAddressSummarySource source)
        {
            try
            {
                var summary = await source.GetAddressSummaryAsync(address, recordFailure: false);
                _historySync!.Offer(_activeWalletId!, HistoryNetwork, summary);
                return summary.IsUsed;
            }
            catch (SummaryUnavailableException)
            {
            }
        }
        return await _blockchain.HasAddressHistoryAsync(address);
    }

    private static readonly HistorySyncOptions DefaultSyncOptions = new();

    /// <summary>Tiered refresh applies (spec 024): change-only sync applies and the tiered switch is on.</summary>
    private bool TieringActive => ChangeOnlySync && (_historyOptions ?? DefaultSyncOptions).TieringActive;

    /// <summary>
    /// The wallet's hints for the hot set (spec 024, FR-002): the receive address on show and the 3 handed out before it, and the
    /// next change address, each with its legacy-format copy when that is watched. Null when tiering doesn't apply or the wallet
    /// has no HD keys; the sync then reads every address as in 0.7.0.
    /// </summary>
    private volatile IReadOnlyCollection<string> _extraHotAddresses = [];

    /// <summary>
    /// Addresses every refresh treats as hot, on top of the wallet's own picks. The payment request watcher sets it to
    /// the addresses of open requests (spec 036, T017), so a payment to one shows on the next refresh. Replaces the
    /// previous set. The wallet never reads payment requests itself, so the two services don't depend on each other.
    /// </summary>
    public void SetExtraHotAddresses(IReadOnlyCollection<string> addresses) => _extraHotAddresses = addresses.ToArray();

    private HotAddressHints? BuildHotHints()
    {
        if (!TieringActive || ActiveSession is not { Hd: { } hd, Info: { } info }) return null;

        void AddReceive(List<string> list, int index)
        {
            if (index < 0) return;
            list.Add(hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString());
            if (index < MinScannedReceive) list.Add(hd.DeriveReceivingAddress(index, ScriptPubKeyType.Legacy).ToString());
        }

        var onShow = new List<string>();
        AddReceive(onShow, info.NextReceivingIndex);
        var handedOut = new List<string>();
        for (var k = 1; k <= HotAddressSelector.HandedOutKept; k++)
            AddReceive(handedOut, info.NextReceivingIndex - k);
        handedOut.AddRange(_extraHotAddresses);
        var nextChange = new List<string> { hd.DeriveChangeAddress(info.NextChangeIndex, ScriptPubKeyType.Segwit).ToString() };
        if (info.NextChangeIndex < MinScannedChange)
            nextChange.Add(hd.DeriveChangeAddress(info.NextChangeIndex, ScriptPubKeyType.Legacy).ToString());

        // Pending sends are found by the sync from its saved history.
        return new HotAddressHints(onShow, handedOut, nextChange, []);
    }

    /// <summary>
    /// The next refresh reads every address (spec 024, FR-006): for the refresh button and pull to refresh. Unlock and sends already
    /// start from an empty set of summaries.
    /// </summary>
    public void RequestFullCheck()
    {
        if (_activeWalletId is { } id)
            _historySync?.ForgetSnapshot(id);
    }

    /// <summary>When every watched address was last read from the chain (FR-008); null when unknown or change-only sync is off.</summary>
    public DateTimeOffset? GetFullyCheckedAt()
    {
        if (!IsUnlocked || !ChangeOnlySync) return null;
        return _historySync!.GetFullyCheckedAt(_activeWalletId!, HistoryNetwork, GetAllAddresses());
    }

    /// <summary>The send coin plan for these addresses, and the summaries' confirmed total (spec 024, FR-010, FR-011).</summary>
    private (SendCoinPlan Plan, long SummariesConfirmed) PlanCoins(IReadOnlyList<string> addresses)
    {
        var summaries = TieringActive ? _historySync!.TryGetSendSummaries(_activeWalletId!, HistoryNetwork, addresses) : null;
        var plan = SendCoinPlanner.Plan(addresses, summaries);
        return (plan, summaries?.Values.Sum(s => s.Confirmed) ?? 0);
    }

    /// <summary>How many addresses a send would read coins from right now, without reading (the /dev/node benchmark).</summary>
    public int PlanSendCoinReads()
    {
        EnsureUnlocked();
        if (_singleKey != null) return 2;
        return PlanCoins(GetAddressKeyMap().Keys.ToList()).Plan.Funded.Count;
    }

    /// <summary>
    /// Reads coins fresh, funded addresses first (spec 024, FR-010): the rest are read only when <paramref name="needsRest"/> says the
    /// coins found aren't enough, so "insufficient funds" is only ever reported after every address was read.
    /// </summary>
    private async Task<List<(string Address, ExtKey Key, List<UtxoInfo> Utxos)>> ReadCoinsAsync(
        Dictionary<string, ExtKey> keyMap, Func<SendCoinPlan, long, long, bool> needsRest)
    {
        var (plan, summariesConfirmed) = PlanCoins(keyMap.Keys.ToList());
        var results = await ReadCoinsOfAsync(plan.Funded, keyMap);
        var found = results.Sum(r => r.Utxos.Sum(u => u.AmountSatoshis));
        if (needsRest(plan, found, summariesConfirmed))
            results.AddRange(await ReadCoinsOfAsync(plan.Rest, keyMap));
        return results;
    }

    private async Task<List<(string Address, ExtKey Key, List<UtxoInfo> Utxos)>> ReadCoinsOfAsync(
        IReadOnlyList<string> addresses, Dictionary<string, ExtKey> keyMap)
    {
        // In parallel, 10 at a time: each coin must keep its address, because Esplora doesn't return its script.
        using var semaphore = new SemaphoreSlim(10);
        var reads = addresses.Select(async address =>
        {
            await semaphore.WaitAsync();
            try { return (Address: address, Key: keyMap[address], Utxos: await _blockchain.GetUtxosAsync(address)); }
            finally { semaphore.Release(); }
        });
        return (await Task.WhenAll(reads)).ToList();
    }

    /// <summary>Broadcasts, then drops the wallet's in-memory summaries so the next refresh reflects the send (FR-023).</summary>
    private async Task<string> BroadcastAsync(byte[] rawTx)
    {
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);
        if (_activeWalletId is { } id)
            _historySync?.ForgetSnapshot(id);
        return txId;
    }

    public Task<string> GetReceivingAddressAsync(string format = "default")
    {
        EnsureUnlocked();
        var type = format == "legacy" ? ScriptPubKeyType.Legacy : ScriptPubKeyType.Segwit;

        if (_singleKey != null)
        {
            var defaultType = format == "default" ? ScriptPubKeyType.Legacy : type;
            var addr = _singleKey.PubKey.GetAddress(defaultType, EffectiveNetwork);
            return Task.FromResult(addr.ToString());
        }
        var index = ActiveWallet!.NextReceivingIndex;
        var address = _hd!.DeriveReceivingAddress(index, format == "default" ? ScriptPubKeyType.Segwit : type);
        return Task.FromResult(address.ToString());
    }

    /// <summary>
    /// Returns the account-level extended public key (xpub) for the active wallet,
    /// or null for WIF single-key wallets which have no HD structure.
    /// Safe to share externally: enables watch-only address derivation (BIP84 0/index)
    /// but cannot sign transactions.
    /// </summary>
    public Task<string?> GetAccountXpubAsync()
    {
        EnsureUnlocked();
        if (_hd is null) return Task.FromResult<string?>(null);
        var xpub = _hd.GetAccountExtPubKey().ToString(EffectiveNetwork);
        return Task.FromResult<string?>(xpub);
    }

    /// <summary>
    /// The DigiDollar account public key (<c>xpub</c>/<c>tpub</c> at <c>m/86'/coin'/0'</c>) for DigiPay, with the first DD
    /// receive address (<c>0/0</c>) it gives, so the merchant can compare it with DigiPay's (specs/035, contracts/wallet-dd-key.md W2).
    /// It comes from the same derivation the wallet scans DD with. Null for wallets that have no DigiDollar account: watch-only,
    /// multisig and private-key wallets, and networks without DigiDollar. It can't spend.
    /// </summary>
    public Task<(string Key, string FirstAddress)?> GetDigiDollarAccountKeyAsync()
    {
        EnsureUnlocked();
        if (GetDigiDollarSession()?.Derivation is not { } dd)
            return Task.FromResult<(string Key, string FirstAddress)?>(null);

        var network = dd.Network == "mainnet" ? DigiByteNetwork.Mainnet : DigiByteNetwork.Testnet;
        var key = dd.GetAccountExtPubKey().ToString(network);
        var firstAddress = dd.GetAddress(dd.Derive(DigiDollarChain.Receive, 0));
        return Task.FromResult<(string Key, string FirstAddress)?>((key, firstAddress));
    }

    /// <summary>
    /// Get all addresses for this wallet. For WIF wallets, returns both Legacy and SegWit.
    /// Results are cached per wallet — HD derivation (elliptic curve math) is only done once.
    /// </summary>
    public List<string> GetAllAddresses()
    {
        EnsureUnlocked();

        // Return cached if nothing it depends on has changed
        var receive = _singleKey is null && ActiveWallet is { } active ? ScanSetOf(active) : null;
        var cacheKey = (_activeWalletId, receive?.Fingerprint ?? "", ScannedChangeCount);
        if (_cachedAddresses != null && _cachedAddressesFor == cacheKey)
            return _cachedAddresses;

        List<string> addresses;
        var changeStart = int.MaxValue;
        if (_singleKey != null)
        {
            var net = EffectiveNetwork;
            addresses = [
                _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, net).ToString(),
                _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, net).ToString(),
            ];
        }
        else
        {
            addresses = new List<string>();
            // The base run, the watched indexes beyond it and the tail after the highest paid one (spec 027).
            foreach (var i in receive?.Indexes ?? [])
            {
                addresses.Add(_hd!.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString());
                if (i < MinScannedReceive)
                    addresses.Add(_hd.DeriveReceivingAddress(i, ScriptPubKeyType.Legacy).ToString());
            }
            changeStart = addresses.Count;
            for (int i = 0; i < ScannedChangeCount; i++)
            {
                addresses.Add(_hd!.DeriveChangeAddress(i, ScriptPubKeyType.Segwit).ToString());
                if (i < MinScannedChange)
                    addresses.Add(_hd.DeriveChangeAddress(i, ScriptPubKeyType.Legacy).ToString());
            }
        }

        _cachedAddresses = addresses;
        _cachedChangeStart = changeStart;
        _cachedAddressesFor = cacheKey;
        return addresses;
    }

    /// <summary>
    /// Normalized change addresses of an HD wallet, from the same scan as <see cref="GetAllAddresses"/>. Empty for
    /// single-key wallets, which send their change back to their own addresses.
    /// </summary>
    private HashSet<string> GetChangeAddressKeys()
    {
        var all = GetAllAddresses();
        var keys = new HashSet<string>();
        for (var i = _cachedChangeStart; i < all.Count; i++)
            keys.Add(AddressKey.Normalize(all[i]));
        return keys;
    }

    public Task<List<(int Index, string Address)>> GetReceivingAddressesAsync(int count, int startIndex = 0)
    {
        EnsureUnlocked();
        if (_singleKey != null)
        {
            var net = EffectiveNetwork;
            return Task.FromResult(new List<(int, string)>
            {
                (0, _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, net).ToString()),
                (1, _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, net).ToString()),
            });
        }
        var addresses = _hd!.DeriveReceivingAddressRange(count, startIndex)
            .Select(a => (a.Index, a.Address.ToString()))
            .ToList();
        return Task.FromResult(addresses);
    }

    /// <summary>
    /// Get the address-to-private-key mapping for HD wallets. Cached per wallet.
    /// Used by SendAsync/SendBatchAsync to avoid re-deriving keys on every send.
    /// </summary>
    private Dictionary<string, ExtKey> GetAddressKeyMap()
    {
        EnsureUnlocked();
        if (_singleKey != null)
            throw new InvalidOperationException("Use _singleKey directly for WIF wallets.");

        var receive = ScanSetOf(ActiveWallet!);
        var cacheKey = (_activeWalletId, receive.Fingerprint, ScannedChangeCount);
        if (_cachedAddressKeyMap != null && _cachedKeyMapFor == cacheKey)
            return _cachedAddressKeyMap;

        var map = new Dictionary<string, ExtKey>();
        foreach (var i in receive.Indexes)
        {
            var key = _hd!.DeriveReceivingKey(i);
            map[_hd.GetAddress(key, ScriptPubKeyType.Segwit).ToString()] = key;
            if (i < MinScannedReceive)
                map[_hd.GetAddress(key, ScriptPubKeyType.Legacy).ToString()] = key;
        }
        for (int i = 0; i < ScannedChangeCount; i++)
        {
            var key = _hd!.DeriveChangeKey(i);
            map[_hd.GetAddress(key, ScriptPubKeyType.Segwit).ToString()] = key;
            if (i < MinScannedChange)
                map[_hd.GetAddress(key, ScriptPubKeyType.Legacy).ToString()] = key;
        }

        _cachedAddressKeyMap = map;
        _cachedKeyMapFor = cacheKey;
        return map;
    }

    /// <summary>
    /// How many change indexes to scan. Always reaches the next change address, so change can never land outside the scan.
    /// </summary>
    private int ScannedChangeCount => Math.Max(MinScannedChange, (ActiveWallet?.NextChangeIndex ?? 0) + 1);

    /// <summary>
    /// Which receive indexes the wallet checks: a base run that always reaches the address on show and every address the
    /// chain has shown as used, the watched indexes beyond it, and the tail after the highest paid one (spec 027).
    /// </summary>
    private static ReceiveScanSet ScanSetOf(WalletInfo info) =>
        ReceiveScanSet.From(info.NextReceivingIndex, info.UsedReceivingCount, info.ReceiveBaseFloor, info.WatchedReceiveIndexes,
            info.HandedOutTop);

    /// <summary>
    /// Moves to a new receive address, saved before it is returned so that it stays scanned after a reload. Skips addresses
    /// the chain shows as used. Throws <see cref="ReceiveAddressException"/>, with a message for the user and the
    /// <see cref="ReceiveAddressFailure"/> a screen translates, if it cannot be saved or if <see cref="ReceiveGapLimit"/>
    /// unused addresses are already out.
    /// </summary>
    public async Task<(int Index, string Address)> GenerateNewReceivingAddressAsync()
    {
        EnsureUnlocked();
        var session = ActiveSession!;
        if (session.Hd is null)
            throw new ReceiveAddressException(ReceiveAddressFailure.SingleAddress, "This wallet has a single address.");
        var info = session.Info;

        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
        if (!ScanSetOf(info).CanHandOutNew)
        {
            // Addresses handed out in this session may have been paid since the last lookup.
            session.AddressDiscovery = null;
            session.AddressDiscoveryRetryAfter = default;
            await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
            if (!ScanSetOf(info).CanHandOutNew)
                throw TooManyUnused(ScanSetOf(info));
        }

        int index;
        await session.AddressOnShowGate.WaitAsync();
        try
        {
            // Read again inside the gate: another hand-out may have gone first.
            var set = ScanSetOf(info);
            if (!set.CanHandOutNew)
                throw TooManyUnused(set);

            index = set.NextNewIndex;
            var previousTop = info.HandedOutTop;
            var undo = MoveAddressOnShow(info, index, index);
            info.HandedOutTop = Math.Max(info.HandedOutTop ?? index, index);
            try
            {
                await PersistAddressIndexesAsync(info);
            }
            catch (Exception ex)
            {
                // Nothing has been shown yet, so fail: an address lost on reload could be paid and then never scanned.
                undo();
                info.HandedOutTop = previousTop;
                throw new ReceiveAddressException(ReceiveAddressFailure.SaveFailed,
                    "Could not save the new address. Your current address is still good to use.", ex);
            }
        }
        finally { session.AddressOnShowGate.Release(); }

        return (index, session.Hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString());
    }

    /// <summary>
    /// Hands out a new receive address for a payment request without changing the address Receive shows (spec 036,
    /// FR-001, FR-003). Same rules as <see cref="GenerateNewReceivingAddressAsync"/>: saved before it is returned, and
    /// refused with <see cref="ReceiveAddressException"/> once <see cref="ReceiveGapLimit"/> unused addresses are out,
    /// counting Receive's and requests' together (FR-005), or when the wallet has a single address.
    /// </summary>
    public async Task<(int Index, string Address)> HandOutRequestAddressAsync()
    {
        EnsureUnlocked();
        var session = ActiveSession!;
        if (session.Hd is null)
            throw new ReceiveAddressException(ReceiveAddressFailure.SingleAddress, "This wallet has a single address.");
        var info = session.Info;

        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
        if (!ScanSetOf(info).CanHandOutNew)
        {
            // Addresses handed out in this session may have been paid since the last lookup.
            session.AddressDiscovery = null;
            session.AddressDiscoveryRetryAfter = default;
            await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
            if (!ScanSetOf(info).CanHandOutNew)
                throw TooManyUnused(ScanSetOf(info));
        }

        int index;
        await session.AddressOnShowGate.WaitAsync();
        try
        {
            var set = ScanSetOf(info);
            if (!set.CanHandOutNew)
                throw TooManyUnused(set);

            index = set.NextNewIndex;
            var undo = RecordHandOut(info, index);
            try
            {
                await PersistAddressIndexesAsync(info);
            }
            catch (Exception ex)
            {
                undo();
                throw new ReceiveAddressException(ReceiveAddressFailure.SaveFailed,
                    "Could not save the new address. Your current address is still good to use.", ex);
            }
        }
        finally { session.AddressOnShowGate.Release(); }

        return (index, session.Hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString());
    }

    /// <summary>
    /// Records a request's address as handed out (spec 036, data-model "Recording a request hand-out"). Follows the rules
    /// of <see cref="MoveAddressOnShow"/> for the watched list and the floor, but never assigns
    /// <see cref="WalletInfo.NextReceivingIndex"/>: only <see cref="WalletInfo.HandedOutTop"/> moves. Nothing is saved
    /// here. Returns what undoes exactly these changes.
    /// </summary>
    private static Action RecordHandOut(WalletInfo info, int index)
    {
        var previousTop = info.HandedOutTop;
        var previousFloor = info.ReceiveBaseFloor;
        var baseCount = ScanSetOf(info).BaseCount;
        var added = false;

        if (index > baseCount && info.WatchedReceiveIndexes.All(w => w.Index != index))
        {
            info.ReceiveBaseFloor = Math.Max(info.ReceiveBaseFloor, baseCount);
            info.WatchedReceiveIndexes.Add(new WatchedReceiveIndex(index, false));
            info.WatchedReceiveIndexes.Sort((a, b) => a.Index.CompareTo(b.Index));
            added = true;
        }
        info.HandedOutTop = index;

        return () =>
        {
            info.HandedOutTop = previousTop;
            info.ReceiveBaseFloor = previousFloor;
            if (added) info.WatchedReceiveIndexes.RemoveAll(w => !w.Paid && w.Index == index);
        };
    }

    /// <summary>
    /// Why no more addresses can be handed out. After a search has found addresses far along, the reason is not what a
    /// restore reaches (a restore needs the search run again to reach them at all) but how far past the last paid address
    /// the wallet keeps looking.
    /// </summary>
    private static ReceiveAddressException TooManyUnused(ReceiveScanSet set) => new(
        set.MaxPaid is null ? ReceiveAddressFailure.TooManyUnusedPastLastUsed : ReceiveAddressFailure.TooManyUnusedPastLastPaid,
        TooManyUnusedMessage(set, "Use one of them first"));

    private static string TooManyUnusedMessage(ReceiveScanSet set, string whatToDo) =>
        $"You have {ReceiveGapLimit} unused addresses in a row. {whatToDo}: " + (set.MaxPaid is null
            ? $"restoring from your seed only looks {ReceiveGapLimit} addresses past the last one used."
            : $"the wallet only keeps looking {ReceiveGapLimit} addresses past the last one that was paid.");

    /// <summary>
    /// The one place the address on show moves (spec 027, data-model.md "Moving the address on show"). <paramref name="first"/>
    /// is the first index being handed out and <paramref name="shown"/> the new address on show. When <paramref name="first"/>
    /// is not contiguous with the base run, every index from it to <paramref name="shown"/> becomes an unpaid watched entry and
    /// the base run is pinned where it is: otherwise the base run would become every address up to the new one. Nothing is
    /// saved here. Returns what undoes exactly these changes, for a caller whose save fails; it does not put back an earlier
    /// copy of the watched list, which would erase results applied meanwhile.
    /// </summary>
    private static Action MoveAddressOnShow(WalletInfo info, int first, int shown)
    {
        var previous = info.NextReceivingIndex;
        var previousFloor = info.ReceiveBaseFloor;
        var baseCount = ScanSetOf(info).BaseCount;
        var added = new HashSet<int>();

        if (first > baseCount)
        {
            info.ReceiveBaseFloor = Math.Max(info.ReceiveBaseFloor, baseCount);
            var known = info.WatchedReceiveIndexes.Select(w => w.Index).ToHashSet();
            for (var i = first; i <= shown; i++)
            {
                if (!known.Add(i)) continue;
                info.WatchedReceiveIndexes.Add(new WatchedReceiveIndex(i, false));
                added.Add(i);
            }
        }
        info.NextReceivingIndex = shown;

        return () =>
        {
            info.NextReceivingIndex = previous;
            info.ReceiveBaseFloor = previousFloor;
            // The list may have been replaced by a save or a result since: work on the one the wallet has now.
            info.WatchedReceiveIndexes.RemoveAll(w => !w.Paid && added.Contains(w.Index));
        };
    }

    /// <summary>
    /// Records that the wallet has history, when it is recorded as empty or not decided yet. Payments a search finds are old
    /// payments: a wallet still recorded as empty when its next history load sees them would celebrate them as its first
    /// payment (spec 027, FR-013a). Call this before changing the wallet to include a found address, so no load can run in
    /// between. A wallet that already has history, or has celebrated, is left as it is. Never throws.
    /// </summary>
    public async Task MarkHasHistoryAsync(string walletId)
    {
        try
        {
            await new FirstReceiveStore(_walletStore.Storage).SetAsync(walletId, FirstReceiveState.History);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wallet] Could not record that the wallet has history: {ex.GetType().Name}");
        }
    }

    /// <summary>The furthest a search can be asked to look: this many addresses from the first one.</summary>
    public const int MaxReceiveSearch = 5000;

    /// <summary>How many addresses a search looks up before it saves what it found and reports progress.</summary>
    public const int ReceiveSearchBatchSize = 20;

    private const int ReceiveSearchStep = 500;

    /// <summary>The largest count a finished search has covered for the active wallet. Never decreases.</summary>
    public int ReceiveSearchedTo => ActiveWallet?.ReceiveSearchedTo ?? 0;

    /// <summary>
    /// The count a search is offered with: 500 past what the wallet already checks, the tail after its highest found address,
    /// and the furthest finished search, whichever is furthest (spec 027, FR-010).
    /// </summary>
    public int DefaultReceiveSearchUpTo
    {
        get
        {
            if (ActiveWallet is not { } info) return ReceiveSearchStep;
            var set = ScanSetOf(info);
            var covered = Math.Max(Math.Max(set.BaseCount, info.ReceiveSearchedTo), set.MaxPaid is { } max ? max + ReceiveScanSet.TailLength + 1 : 0);
            return Math.Min(MaxReceiveSearch, covered + ReceiveSearchStep);
        }
    }

    /// <summary>
    /// Looks for paid receive addresses among the wallet's first <paramref name="upTo"/> addresses, far past where the
    /// automatic search stops: for a wallet whose key also hands out addresses somewhere else (spec 027). Each address found
    /// paid is checked from then on, so its funds are in the balance and can be spent with nothing to move. An address that
    /// no one can have used is looked up first: a source that reports history for it is not believed, and nothing is changed.
    /// Never looks at or past <paramref name="upTo"/>. Throws <see cref="ArgumentOutOfRangeException"/> for a count outside
    /// 1 to <see cref="MaxReceiveSearch"/>; for nothing else. Stopping, a failed lookup and a source that can't be believed come
    /// back as the result's outcome, with what was covered and what was added. Cancel is honoured between batches. One search
    /// runs per wallet at a time: a second call while one runs returns that run.
    /// </summary>
    public Task<ReceiveSearchResult> SearchReceiveAddressesAsync(
        int upTo, IProgress<ReceiveSearchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureUnlocked();
        var session = ActiveSession!;

        // A single-key wallet has no further addresses.
        if (session.Hd is null)
            return Task.FromResult(new ReceiveSearchResult(
                ReceiveSearchOutcome.NothingToCheck, upTo, Math.Max(0, upTo), [], 0, false, false, false, 0, null));

        if (upTo < 1 || upTo > MaxReceiveSearch)
            return Task.FromException<ReceiveSearchResult>(
                new ArgumentOutOfRangeException(nameof(upTo), upTo, $"Enter a whole number from 1 to {MaxReceiveSearch}."));

        if (session.ReceiveSearch is { IsCompleted: false } running)
        {
            // A run that is still going is joined. One that has been told to stop (its page was left) only has its last
            // batch out: this caller waits for that and gets a search of its own, not the old run's "stopped" result.
            if (!session.ReceiveSearchToken.IsCancellationRequested) return running;
            session.ReceiveSearchToken = cancellationToken;
            return session.ReceiveSearch = RestartAsync(running);
        }

        session.ReceiveSearchToken = cancellationToken;
        return session.ReceiveSearch = RunReceiveSearchAsync(session, upTo, progress, cancellationToken);

        async Task<ReceiveSearchResult> RestartAsync(Task<ReceiveSearchResult> stopping)
        {
            await stopping; // Never faults.
            return await RunReceiveSearchAsync(session, upTo, progress, cancellationToken);
        }
    }

    /// <summary>Still the unlocked session the search started with: not locked, and not locked and unlocked again.</summary>
    private bool IsSameSession(UnlockedWalletSession session) =>
        _sessions.TryGetValue(session.Info.Id, out var current) && ReferenceEquals(current, session);

    private async Task<ReceiveSearchResult> RunReceiveSearchAsync(
        UnlockedWalletSession session, int upTo, IProgress<ReceiveSearchProgress>? progress, CancellationToken cancellationToken)
    {
        var hd = session.Hd!;
        var info = session.Info;
        string AddressAt(int index) => hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString();

        var added = new List<int>();
        var showNotice = false;

        // How every run that got as far as looking addresses up ends, whatever the outcome: what it added is reported and
        // kept, and only a finished run moves the furthest-search mark.
        async Task<ReceiveSearchResult> EndAsync(ReceiveSearchOutcome outcome, int reached, string? error = null)
        {
            var stillOpen = IsSameSession(session);
            if (outcome == ReceiveSearchOutcome.Finished && stillOpen)
            {
                info.ReceiveSearchedTo = Math.Max(info.ReceiveSearchedTo, upTo);
                await SaveAddressIndexesAsync(info);
            }

            long? satoshis = 0;
            if (added.Count > 0)
            {
                // Found and added; if this read fails only the amount is unknown.
                try { satoshis = await _blockchain.GetBalanceAsync(added.Select(AddressAt)); }
                catch (Exception) { satoshis = null; }
            }

            var checkedNow = ScanSetOf(info);
            return new ReceiveSearchResult(
                outcome, upTo, reached, added, satoshis,
                LeavesUnwatched: Enumerable.Range(0, reached).Any(i => !checkedNow.Contains(i)),
                SuggestSearchingFurther: outcome == ReceiveSearchOutcome.Finished && upTo < MaxReceiveSearch
                    && added.Any(i => i >= upTo - ReceiveScanSet.TailLength),
                ShowForeignIssuerNotice: showNotice,
                AlreadyCheckedUpTo: 0,
                Error: error);
        }

        static ReceiveSearchResult Nothing(ReceiveSearchOutcome outcome, int upTo, int reached, int alreadyChecked = 0, string? error = null) =>
            new(outcome, upTo, reached, [], outcome == ReceiveSearchOutcome.NothingToCheck ? 0 : null, false, false, false, alreadyChecked, error);

        // Fixed now: tail addresses and unpaid watched addresses are looked up too, so a payment sitting in the tail becomes
        // a watched address before a find further on moves the tail away from it.
        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
        var targets = ScanSetOf(info).SearchTargets(upTo);
        if (targets.Count == 0)
            return Nothing(ReceiveSearchOutcome.NothingToCheck, upTo, upTo, alreadyChecked: ScanSetOf(info).BaseCount);

        // The control: an address from a key made here and thrown away. Nobody has paid it.
        string NewControl() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, EffectiveNetwork).ToString();
        bool controlUsed;
        try
        {
            controlUsed = await _blockchain.HasAddressHistoryAsync(NewControl());
        }
        catch (OperationCanceledException)
        {
            return Nothing(ReceiveSearchOutcome.Cancelled, upTo, 0);
        }
        catch (Exception ex)
        {
            return Nothing(ReceiveSearchOutcome.Failed, upTo, 0, error: ex.Message);
        }
        if (controlUsed) return Nothing(ReceiveSearchOutcome.Untrusted, upTo, 0);

        // Every address below this count has been checked, or was already checked.
        var reached = targets[0];
        using var gate = new SemaphoreSlim(10);
        for (var done = 0; done < targets.Count; done += ReceiveSearchBatchSize)
        {
            // Between batches: stop when asked, and stop if the wallet was locked. Nothing is half-saved either way.
            if (cancellationToken.IsCancellationRequested || !IsSameSession(session))
                return await EndAsync(ReceiveSearchOutcome.Cancelled, reached);

            List<int> paid;
            try
            {
                // Straight to the chain, not through the history sync: these are not addresses the wallet watches, and their
                // answers must not fill its saved summaries (research R6).
                var lookups = targets.Skip(done).Take(ReceiveSearchBatchSize).Select(async index =>
                {
                    await gate.WaitAsync();
                    try { return (Index: index, Used: await _blockchain.HasAddressHistoryAsync(AddressAt(index))); }
                    finally { gate.Release(); }
                }).ToList();
                // All of them are awaited before any failure is acted on, so none is left running against the gate.
                var all = Task.WhenAll(lookups);
                try { await all; } catch { /* read below */ }
                if (all.IsCanceled) return await EndAsync(ReceiveSearchOutcome.Cancelled, reached);
                if (all.Exception is { } failure)
                    return await EndAsync(ReceiveSearchOutcome.Failed, reached, failure.InnerExceptions[0].Message);
                paid = all.Result.Where(r => r.Used).Select(r => r.Index).ToList();

                // The first control vouched for one answer, not for whichever source answers each later lookup: the chain of
                // explorers fails over per call, and a stand-in source may report every address as used. A paid mark cannot
                // be undone, so each find is asked for a second time, and then a fresh control is looked up. A find that does
                // not repeat is dropped; a control reported as used ends the search with this batch not applied.
                if (paid.Count > 0)
                {
                    var confirmed = new List<int>();
                    foreach (var index in paid)
                    {
                        // Asked to stop, or locked, while confirming: this batch is not applied.
                        if (cancellationToken.IsCancellationRequested || !IsSameSession(session))
                            return await EndAsync(ReceiveSearchOutcome.Cancelled, reached);
                        if (await _blockchain.HasAddressHistoryAsync(AddressAt(index))) confirmed.Add(index);
                    }
                    if (await _blockchain.HasAddressHistoryAsync(NewControl()))
                        return await EndAsync(ReceiveSearchOutcome.Failed, reached,
                            "The block explorer reported history for an address nobody has used, so its answers were not believed.");
                    paid = confirmed;
                }

                // Locked while the batch was out: its answers are not applied.
                if (!IsSameSession(session)) return await EndAsync(ReceiveSearchOutcome.Cancelled, reached);

                if (paid.Count > 0)
                {
                    // First, and before the wallet changes: these are old payments, and a history load that saw them on a
                    // wallet still recorded as empty would celebrate them as its first payment (FR-013a).
                    await MarkHasHistoryAsync(info.Id);

                    // Applied to the list the wallet has now, not one read before the lookups.
                    var paidBefore = info.WatchedReceiveIndexes.Where(w => w.Paid).Select(w => w.Index).ToHashSet();
                    info.WatchedReceiveIndexes = ReceiveScanSet.MarkPaid(ScanSetOf(info).BaseCount, info.WatchedReceiveIndexes, paid);
                    var newlyPaid = info.WatchedReceiveIndexes.Where(w => w.Paid && !paidBefore.Contains(w.Index) && paid.Contains(w.Index))
                        .Select(w => w.Index).ToList();
                    added.AddRange(newlyPaid);
                    if (newlyPaid.Count > 0 && !info.ReceiveSearchNoticeShown)
                    {
                        info.ReceiveSearchNoticeShown = true;
                        showNotice = true;
                    }
                    await PersistAddressIndexesAsync(info);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The finds could not be saved. They stay in this session; say the search did not finish.
                return await EndAsync(ReceiveSearchOutcome.Failed, reached, ex.Message);
            }
            catch (OperationCanceledException)
            {
                return await EndAsync(ReceiveSearchOutcome.Cancelled, reached);
            }

            var next = done + ReceiveSearchBatchSize;
            reached = next < targets.Count ? targets[next] : upTo;
            progress?.Report(new ReceiveSearchProgress(reached, upTo, added.Count));
        }

        return await EndAsync(ReceiveSearchOutcome.Finished, upTo);
    }

    /// <summary>
    /// Once per unlock, finds the receive and change addresses the chain shows as used. This brings back funds stranded past
    /// the old fixed scan ranges, and stops a wallet whose saved indexes are behind (older versions never saved them) from
    /// reusing addresses. Never throws and waits at most <paramref name="limit"/>: if the chain is slow or unreadable the wallet
    /// works from its saved indexes. A slow discovery keeps running and later reads pick up its result; a failed one tries again
    /// after <see cref="AddressDiscoveryRetryDelay"/>.
    /// </summary>
    private async Task WaitForAddressDiscoveryAsync(TimeSpan limit)
    {
        var session = ActiveSession;
        if (session?.Hd is null) return;

        if (session.AddressDiscovery is null)
        {
            if (DateTime.UtcNow < session.AddressDiscoveryRetryAfter) return;
            var search = RunAddressDiscoveryAsync(session);
            session.AddressDiscovery = search;
            _ = AnnounceAddressSearchAsync(session, search);
        }

        await Task.WhenAny(session.AddressDiscovery, Task.Delay(limit));
    }

    /// <summary>
    /// Raised with the wallet's id when its search for used addresses has finished and <see cref="AddressesSettled"/> is true
    /// for it. A balance or history read taken earlier covered only the addresses known then, so a page showing one reads
    /// again. Not raised when the search fails.
    /// </summary>
    public event Action<string>? AddressSearchFinished;

    /// <summary>
    /// Raised after a balance or history read finds transactions it has not reported before, or one that has since
    /// confirmed, with the wallet's own addresses those transactions touch (spec 036, T016). Payment requests use it to
    /// notice money reaching a request that is no longer being watched closely (FR-020). Never raised with an empty set.
    /// </summary>
    public event Action<IReadOnlyCollection<string>>? TransactionsChanged;

    private readonly object _reportedTxsLock = new();
    private readonly Dictionary<string, bool> _reportedTxs = new(StringComparer.Ordinal);
    private string? _reportedTxsWalletId;

    /// <summary>
    /// Compares <paramref name="txs"/> with what was reported for the active wallet and raises
    /// <see cref="TransactionsChanged"/> for the new or newly confirmed ones. Never throws.
    /// </summary>
    private void ReportTransactions(IEnumerable<TransactionInfo> txs, IEnumerable<string> walletAddresses)
    {
        HashSet<string> touched;
        lock (_reportedTxsLock)
        {
            if (_reportedTxsWalletId != _activeWalletId)
            {
                _reportedTxs.Clear();
                _reportedTxsWalletId = _activeWalletId;
            }
            var own = walletAddresses.ToHashSet(StringComparer.Ordinal);
            touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tx in txs)
            {
                var confirmed = tx.Confirmations >= 1;
                if (_reportedTxs.TryGetValue(tx.TxId, out var was) && was == confirmed) continue;
                _reportedTxs[tx.TxId] = confirmed;
                foreach (var o in tx.Outputs) if (own.Contains(o.Address)) touched.Add(o.Address);
                foreach (var i in tx.Inputs) if (own.Contains(i.Address)) touched.Add(i.Address);
            }
        }
        if (touched.Count == 0) return;
        try { TransactionsChanged?.Invoke(touched); }
        catch (Exception ex) { Console.WriteLine($"[Wallet] A TransactionsChanged handler failed: {ex.GetType().Name}"); }
    }

    /// <summary>
    /// Whether the active wallet has finished finding which of its addresses have been used. Until then a zero balance or an
    /// empty history does not prove the wallet is empty: on a restored wallet the funds may be on addresses the search has
    /// not reached yet.
    /// </summary>
    public bool AddressesSettled => IsSettled(ActiveSession);

    /// <summary>
    /// A wallet with no HD key (a private key) has fixed addresses and nothing to find. A failed search clears its task, so
    /// "completed successfully" is false for running, not started and failed alike.
    /// </summary>
    private static bool IsSettled(UnlockedWalletSession? session) =>
        session is null || session.Hd is null || session.AddressDiscovery is { IsCompletedSuccessfully: true };

    /// <summary>Raises <see cref="AddressSearchFinished"/> once <paramref name="search"/> has ended, unless it failed.</summary>
    private async Task AnnounceAddressSearchAsync(UnlockedWalletSession session, Task search)
    {
        await search; // Never faults.
        if (!ReferenceEquals(session.AddressDiscovery, search)) return; // It failed and was cleared, or the wallet was locked.

        try
        {
            AddressSearchFinished?.Invoke(session.Info.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wallet] A listener for the finished address search failed: {ex.GetType().Name}");
        }
    }

    private async Task RunAddressDiscoveryAsync(UnlockedWalletSession session)
    {
        // Return to the caller first, so that it has stored this task before a quick failure clears it.
        await Task.Yield();
        try
        {
            await Task.WhenAll(FindUsedChangeAsync(session), FindUsedReceiveAsync(session));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wallet] Address lookup failed: {ex.Message}");
            session.AddressDiscovery = null;
            session.AddressDiscoveryRetryAfter = DateTime.UtcNow + AddressDiscoveryRetryDelay;
        }
    }

    private async Task FindUsedChangeAsync(UnlockedWalletSession session)
    {
        var hd = session.Hd!;
        var info = session.Info;
        var next = await FindUsedCountAsync(
            i => hd.DeriveChangeAddress(i, ScriptPubKeyType.Segwit).ToString(),
            info.NextChangeIndex, ChangeGapLimit, 0, MaxAddressSearch);

        if (next is null)
        {
            // Keep the saved index: a value this high would be scanned on every balance refresh from now on.
            Console.WriteLine($"[Wallet] Ignoring change address history: no unused addresses in {MaxAddressSearch} lookups.");
            return;
        }

        if (next > info.NextChangeIndex)
        {
            info.NextChangeIndex = next.Value;
            await SaveAddressIndexesAsync(info);
        }
    }

    /// <summary>
    /// Updates <see cref="WalletInfo.UsedReceivingCount"/> and leaves <see cref="WalletInfo.NextReceivingIndex"/> alone, so the
    /// address the user sees does not change by itself. The search always covers a full gap past the always-scanned range,
    /// because older versions let users skip over those addresses without ever using them.
    /// </summary>
    private async Task FindUsedReceiveAsync(UnlockedWalletSession session)
    {
        var hd = session.Hd!;
        var info = session.Info;
        var used = await FindUsedCountAsync(
            i => hd.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString(),
            info.UsedReceivingCount, ReceiveGapLimit, ScanSetOf(info).BaseCount + ReceiveGapLimit, MaxAddressSearch);

        if (used is null)
        {
            // Keep the saved count: a value this high would be scanned on every balance refresh from now on.
            Console.WriteLine($"[Wallet] Ignoring receive address history: no unused addresses in {MaxAddressSearch} lookups.");
            return;
        }

        if (used > info.UsedReceivingCount)
        {
            info.UsedReceivingCount = used.Value;
            await SaveAddressIndexesAsync(info);
        }

        await FindPaidWatchedAsync(session);
    }

    /// <summary>
    /// The automatic search's second part, for a wallet that has watched receive addresses (spec 027, FR-014b). Looks up
    /// every watched address not yet seen paid, then walks on from the highest paid one until
    /// <see cref="ReceiveGapLimit"/> in a row are unused, so payments an outside issuer takes after a find show up at the next
    /// unlock with no manual search. Everything is applied once, at the end. A walk that has not ended after
    /// <see cref="MaxAddressSearch"/> lookups means the answers can't be believed, and then nothing from this pass is applied:
    /// a paid mark cannot be undone and it moves the limit on new addresses.
    /// </summary>
    private async Task FindPaidWatchedAsync(UnlockedWalletSession session)
    {
        var hd = session.Hd!;
        var info = session.Info;
        if (info.WatchedReceiveIndexes.Count == 0) return;

        var set = ScanSetOf(info);
        string AddressAt(int index) => hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString();

        var paid = await FindUsedAmongAsync(set.UnpaidWatched, AddressAt);

        // From the highest paid index held now, not one this pass is about to raise.
        if (set.MaxPaid is { } maxPaid)
        {
            var next = maxPaid + 1;
            var end = next + ReceiveGapLimit;
            var lookups = 0;
            while (next < end)
            {
                if (lookups >= MaxAddressSearch)
                {
                    Console.WriteLine($"[Wallet] Ignoring watched receive address history: no unused addresses in {MaxAddressSearch} lookups.");
                    return;
                }
                var batch = Enumerable.Range(next, Math.Min(end - next, MaxAddressSearch - lookups)).ToList();
                var used = await FindUsedAmongAsync(batch, AddressAt);
                lookups += batch.Count;
                next += batch.Count;
                if (used.Count > 0)
                {
                    paid.AddRange(used);
                    end = Math.Max(end, used.Max() + 1 + ReceiveGapLimit);
                }
            }
        }

        // To the list the wallet has now: an address handed out while the lookups ran must not be lost.
        var baseCount = ScanSetOf(info).BaseCount;
        var paidAlready = info.WatchedReceiveIndexes.Where(w => w.Paid).Select(w => w.Index).ToHashSet();
        if (!paid.Any(i => i >= baseCount && !paidAlready.Contains(i))) return;

        // Before the wallet changes, as in the wider search: these must not be celebrated as a first payment (FR-013a).
        await MarkHasHistoryAsync(info.Id);
        info.WatchedReceiveIndexes = ReceiveScanSet.MarkPaid(ScanSetOf(info).BaseCount, info.WatchedReceiveIndexes, paid);
        await SaveAddressIndexesAsync(info);
    }

    /// <summary>Which of <paramref name="indexes"/> the chain shows as used. For addresses the wallet watches.</summary>
    private async Task<List<int>> FindUsedAmongAsync(IReadOnlyList<int> indexes, Func<int, string> addressAt)
    {
        if (indexes.Count == 0) return [];
        using var semaphore = new SemaphoreSlim(10);
        var lookups = indexes.Select(async index =>
        {
            await semaphore.WaitAsync();
            try { return (Index: index, Used: await IsAddressUsedAsync(addressAt(index))); }
            finally { semaphore.Release(); }
        }).ToList();
        return (await Task.WhenAll(lookups)).Where(r => r.Used).Select(r => r.Index).ToList();
    }

    /// <summary>
    /// The receive indexes "Your addresses" lists (spec 027, FR-015b): the run from the first address and the watched
    /// addresses, up to the address on show. After a far-off find these are not an unbroken run.
    /// </summary>
    public IReadOnlyList<int> GetListedReceiveIndexes()
    {
        EnsureUnlocked();
        return ActiveSession is { Hd: not null, Info: { } active } ? ScanSetOf(active).ListedIndexes : [];
    }

    /// <summary>
    /// Walks an address chain from <paramref name="from"/> until <paramref name="gapLimit"/> addresses in a row are unused and
    /// <paramref name="searchAtLeastTo"/> is reached. Returns one past the last used index, or <paramref name="from"/> if none is
    /// used. Returns null if that has not happened after <paramref name="maxSearch"/> addresses: the answers can't be believed.
    /// </summary>
    private async Task<int?> FindUsedCountAsync(
        Func<int, string> addressAt, int from, int gapLimit, int searchAtLeastTo, int maxSearch, IProgress<int>? progress = null)
    {
        var usedCount = from;
        var searchedTo = from;
        var semaphore = new SemaphoreSlim(10);
        var searchLimit = from + maxSearch;
        int End() => Math.Max(usedCount + gapLimit, searchAtLeastTo);

        while (searchedTo < End())
        {
            if (searchedTo >= searchLimit) return null;

            var batch = Math.Min(LookupBatchSize, Math.Min(End(), searchLimit) - searchedTo);
            var lookups = Enumerable.Range(searchedTo, batch).Select(async index =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return (Index: index, Used: await IsAddressUsedAsync(addressAt(index)));
                }
                finally { semaphore.Release(); }
            }).ToList();

            searchedTo += batch;
            foreach (var (index, used) in await Task.WhenAll(lookups))
                if (used) usedCount = Math.Max(usedCount, index + 1);

            progress?.Report((int)((searchedTo - from) * 100L / Math.Max(1, End() - from)));
        }

        return usedCount;
    }

    /// <summary>A change address has been paid: use the next one from now on, including after a reload.</summary>
    private async Task AdvanceChangeIndexAsync()
    {
        var session = ActiveSession;
        if (session?.Hd is null) return;

        session.Info.NextChangeIndex++;
        await SaveAddressIndexesAsync(session.Info);
    }

    /// <summary>
    /// Saves the address indexes without ever throwing, for callers that have nothing to gain from failing: after a broadcast,
    /// reporting a send that went out as failed could make the user pay twice. The session keeps the new values, and a later
    /// discovery corrects the saved ones.
    /// </summary>
    private async Task SaveAddressIndexesAsync(WalletInfo info)
    {
        try
        {
            await PersistAddressIndexesAsync(info);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Wallet] Could not save the address indexes: {ex.Message}");
        }
    }

    /// <summary>Saves only the address indexes on top of what is stored, so other settings saved elsewhere are kept.</summary>
    private async Task PersistAddressIndexesAsync(WalletInfo info)
    {
        var stored = await GetWalletAsync(info.Id);
        if (stored is null) return; // Deleted while this was running: saving would bring back a wallet with no seed.

        CopyAddressState(info, stored);
        await _walletStore.SaveWalletInfoAsync(info.Id, JsonSerializer.Serialize(stored));
    }

    /// <summary>
    /// Puts the unlocked wallet's address state onto a record about to be saved. With no pause between reading and assigning,
    /// so a result applied while the record was loading is not lost. Watched entries the base run has grown over are dropped
    /// here, whichever operation grew it (spec 027).
    /// </summary>
    private static void CopyAddressState(WalletInfo info, WalletInfo stored)
    {
        stored.NextChangeIndex = info.NextChangeIndex;
        stored.NextReceivingIndex = info.NextReceivingIndex;
        stored.UsedReceivingCount = info.UsedReceivingCount;
        stored.ReceiveBaseFloor = info.ReceiveBaseFloor;
        stored.HandedOutTop = info.HandedOutTop;
        stored.ReceiveSearchedTo = info.ReceiveSearchedTo;
        stored.ReceiveSearchNoticeShown = info.ReceiveSearchNoticeShown;
        info.WatchedReceiveIndexes = ReceiveScanSet.Normalize(ScanSetOf(info).BaseCount, info.WatchedReceiveIndexes);
        stored.WatchedReceiveIndexes = [.. info.WatchedReceiveIndexes];
    }

    /// <summary>
    /// Saves a wallet record that was loaded, changed and is saved whole (a rename, a colour, a credential change). The
    /// record was read a moment ago: a search or the unlock search may have saved found addresses since, and saving the
    /// older copy would drop them from storage. While the wallet is unlocked its session holds the current address state,
    /// so that is what goes into the record.
    /// </summary>
    private Task SaveWalletRecordAsync(string walletId, WalletInfo record)
    {
        if (_sessions.TryGetValue(walletId, out var session) && !ReferenceEquals(session.Info, record))
            CopyAddressState(session.Info, record);
        return _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(record));
    }

    public async Task<string> SendAsync(string destinationAddress, decimal amountDgb, string? memo = null, int feeRateSatPerByte = 5)
    {
        EnsureUnlocked();

        if (ActiveWallet?.WalletType == "xpub")
            throw new InvalidOperationException("Watch-only wallets cannot send transactions.");

        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);

        var network = CurrentNetwork;
        var amountSatoshis = (long)(amountDgb * 100_000_000m);
        var amount = Money.Satoshis(amountSatoshis);

        List<DigiByte.Crypto.Transactions.Utxo> availableUtxos;
        BitcoinAddress changeAddress;

        if (_singleKey != null)
        {
            // WIF wallet — single key, both Legacy and SegWit addresses
            var legacyAddr = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network);
            var segwitAddr = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);

            // Query each address separately so we know the correct scriptPubKey per UTXO
            var legacyUtxos = await _blockchain.GetUtxosAsync(legacyAddr.ToString());
            var segwitUtxos = await _blockchain.GetUtxosAsync(segwitAddr.ToString());

            if (legacyUtxos.Count == 0 && segwitUtxos.Count == 0)
                throw new InvalidOperationException("No UTXOs available. Your wallet has no spendable funds.");

            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();

            foreach (var u in legacyUtxos)
            {
                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = legacyAddr.ScriptPubKey,
                    PrivateKey = _singleKey,
                    Confirmations = u.Confirmations,
                });
            }

            foreach (var u in segwitUtxos)
            {
                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = segwitAddr.ScriptPubKey,
                    PrivateKey = _singleKey,
                    Confirmations = u.Confirmations,
                });
            }

            // Change goes back to the SegWit address of the same key
            changeAddress = segwitAddr;
        }
        else
        {
            // HD wallet — use cached address-to-key mapping
            var addressKeyMap = GetAddressKeyMap();

            // Query addresses in parallel — we need to know which address each UTXO
            // belongs to for correct scriptPubKey assignment (Esplora doesn't return it).
            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();
            var utxoResults = await ReadCoinsAsync(addressKeyMap, (plan, found, _) => SendCoinPlanner.NeedsRest(plan, found, amountSatoshis));
            foreach (var (addr, key, utxos) in utxoResults)
            {
                var addrScript = BitcoinAddress.Create(addr, network).ScriptPubKey;
                foreach (var utxoInfo in utxos)
                {
                    availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                    {
                        TransactionId = uint256.Parse(utxoInfo.TxId),
                        OutputIndex = utxoInfo.OutputIndex,
                        Amount = Money.Satoshis(utxoInfo.AmountSatoshis),
                        ScriptPubKey = !string.IsNullOrEmpty(utxoInfo.ScriptPubKey)
                            ? Script.FromHex(utxoInfo.ScriptPubKey)
                            : addrScript,
                        PrivateKey = key.PrivateKey,
                        Confirmations = utxoInfo.Confirmations,
                    });
                }
            }

            if (availableUtxos.Count == 0)
                throw new InvalidOperationException("No UTXOs available. Your wallet has no spendable funds.");

            var changeKey = _hd!.DeriveChangeKey(ActiveWallet!.NextChangeIndex);
            changeAddress = _hd.GetAddress(changeKey);
        }

        // Use only confirmed UTXOs to avoid too-long-mempool-chain (node limit: 25 ancestors).
        // Fall back to all UTXOs only if confirmed ones can't cover the amount + estimated fee.
        var totalAvailable = availableUtxos.Sum(u => u.Amount.Satoshi);
        if (totalAvailable < amountSatoshis)
            throw new InvalidOperationException(
                $"Insufficient funds. Need {amountDgb:N8} DGB but only have {totalAvailable / 100_000_000m:N8} DGB.");

        var confirmedUtxos = availableUtxos.Where(u => u.Confirmations > 0).ToList();
        var confirmedTotal = confirmedUtxos.Sum(u => u.Amount.Satoshi);
        var utxosToUse = confirmedTotal >= amountSatoshis ? confirmedUtxos : availableUtxos;

        // Build and sign the transaction
        var destination = BitcoinAddress.Create(destinationAddress, network);
        // DigiByte min relay fee is 100000 sat/kB (100 sat/vB) — enforce floor
        var effectiveFeeRate = Math.Max(feeRateSatPerByte, 100);
        var feeRate = new FeeRate(Money.Satoshis(effectiveFeeRate * 1000));

        var txBuilder = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network);
        var tx = txBuilder.BuildSendTransaction(utxosToUse, destination, amount, changeAddress, feeRate, memo);

        // Broadcast
        var rawTx = tx.ToBytes();
        var txId = await BroadcastAsync(rawTx);
        await RecordOwnSendAsync(rawTx);

        // Track the transaction locally
        var feePaid = tx.GetFee(utxosToUse.Select(u => u.ToCoin()).ToArray());
        await _txTracker.RecordSendAsync(txId, destinationAddress, amountSatoshis,
            feePaid?.Satoshi ?? 0, memo);

        await AdvanceChangeIndexAsync();

        return txId;
    }

    /// <summary>
    /// Sends DGB to multiple recipients in a single transaction (batch send).
    /// </summary>
    public async Task<string> SendBatchAsync(
        List<(string Address, decimal AmountDgb)> recipients,
        string? memo = null,
        int feeRateSatPerByte = 5)
    {
        EnsureUnlocked();

        if (ActiveWallet?.WalletType == "xpub")
            throw new InvalidOperationException("Watch-only wallets cannot send transactions.");

        if (recipients == null || recipients.Count == 0)
            throw new ArgumentException("At least one recipient is required.");

        if (recipients.Count > 20)
            throw new ArgumentException("Maximum 20 recipients per batch transaction.");

        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);

        var network = CurrentNetwork;

        // Validate all addresses and convert amounts
        var outputs = new List<(BitcoinAddress Address, Money Amount)>();
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalSatoshis = 0;

        foreach (var (address, amountDgb) in recipients)
        {
            if (!DigiByte.Crypto.Addresses.AddressValidator.IsValid(address))
                throw new ArgumentException($"Invalid address: {address}");

            if (!seenAddresses.Add(address))
                throw new ArgumentException($"Duplicate address in batch: {address}");

            if (amountDgb < 0.0001m)
                throw new ArgumentException($"Amount below minimum (0.0001 DGB) for {address}");

            var satoshis = (long)(amountDgb * 100_000_000m);
            totalSatoshis += satoshis;

            var dest = BitcoinAddress.Create(address, network);
            outputs.Add((dest, Money.Satoshis(satoshis)));
        }

        // Fetch UTXOs (same logic as SendAsync)
        List<DigiByte.Crypto.Transactions.Utxo> availableUtxos;
        BitcoinAddress changeAddress;

        if (_singleKey != null)
        {
            var legacyAddr = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network);
            var segwitAddr = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);

            var legacyUtxos = await _blockchain.GetUtxosAsync(legacyAddr.ToString());
            var segwitUtxos = await _blockchain.GetUtxosAsync(segwitAddr.ToString());

            if (legacyUtxos.Count == 0 && segwitUtxos.Count == 0)
                throw new InvalidOperationException("No UTXOs available. Your wallet has no spendable funds.");

            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();

            foreach (var u in legacyUtxos)
            {
                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = legacyAddr.ScriptPubKey,
                    PrivateKey = _singleKey,
                    Confirmations = u.Confirmations,
                });
            }

            foreach (var u in segwitUtxos)
            {
                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = segwitAddr.ScriptPubKey,
                    PrivateKey = _singleKey,
                    Confirmations = u.Confirmations,
                });
            }

            changeAddress = segwitAddr;
        }
        else
        {
            var addressKeyMap = GetAddressKeyMap();

            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();
            var batchUtxoResults = await ReadCoinsAsync(addressKeyMap, (plan, found, _) => SendCoinPlanner.NeedsRest(plan, found, totalSatoshis));
            foreach (var (addr, key, utxos) in batchUtxoResults)
            {
                var addrScript = BitcoinAddress.Create(addr, network).ScriptPubKey;
                foreach (var utxoInfo in utxos)
                {
                    availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                    {
                        TransactionId = uint256.Parse(utxoInfo.TxId),
                        OutputIndex = utxoInfo.OutputIndex,
                        Amount = Money.Satoshis(utxoInfo.AmountSatoshis),
                        ScriptPubKey = !string.IsNullOrEmpty(utxoInfo.ScriptPubKey)
                            ? Script.FromHex(utxoInfo.ScriptPubKey)
                            : addrScript,
                        PrivateKey = key.PrivateKey,
                        Confirmations = utxoInfo.Confirmations,
                    });
                }
            }

            if (availableUtxos.Count == 0)
                throw new InvalidOperationException("No UTXOs available. Your wallet has no spendable funds.");

            var changeKey = _hd!.DeriveChangeKey(ActiveWallet!.NextChangeIndex);
            changeAddress = _hd.GetAddress(changeKey);
        }

        // Check sufficient funds
        var totalAvailable = availableUtxos.Sum(u => u.Amount.Satoshi);
        var totalDgb = totalSatoshis / 100_000_000m;
        if (totalAvailable < totalSatoshis)
            throw new InvalidOperationException(
                $"Insufficient funds. Need {totalDgb:N8} DGB but only have {totalAvailable / 100_000_000m:N8} DGB.");

        // Prefer confirmed UTXOs
        var confirmedUtxos = availableUtxos.Where(u => u.Confirmations > 0).ToList();
        var confirmedTotal = confirmedUtxos.Sum(u => u.Amount.Satoshi);
        var utxosToUse = confirmedTotal >= totalSatoshis ? confirmedUtxos : availableUtxos;

        // Build and sign
        var effectiveFeeRate = Math.Max(feeRateSatPerByte, 100);
        var feeRate = new FeeRate(Money.Satoshis(effectiveFeeRate * 1000));

        var txBuilder = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network);
        var tx = txBuilder.BuildMultiOutputTransaction(utxosToUse, outputs, changeAddress, feeRate, memo);

        // Broadcast
        var rawTx = tx.ToBytes();
        var txId = await BroadcastAsync(rawTx);
        await RecordOwnSendAsync(rawTx);

        // History is keyed by TxId, so the whole batch is one record: the full amount, and the
        // first recipient as counterparty (the same one an explorer-built record would show).
        var feePaid = tx.GetFee(utxosToUse.Select(u => u.ToCoin()).ToArray());
        await _txTracker.RecordSendAsync(txId, recipients[0].Address, totalSatoshis, feePaid?.Satoshi ?? 0, memo);

        await AdvanceChangeIndexAsync();

        return txId;
    }

    /// <summary>
    /// True when the last <see cref="GetTransactionHistoryAsync"/> could not read the chain and returned only the locally tracked
    /// transactions. A page showing saved history keeps it in that case instead of replacing it with a partial list (spec 023, FR-024).
    /// </summary>
    public bool LastHistoryIncomplete { get; private set; }

    /// <summary>
    /// One history load with what it proves (spec 025, FR-011, FR-015). <paramref name="Complete"/>: the chain was read for
    /// this call. <paramref name="AddressesSettled"/>: the wallet had finished finding which of its addresses have been used
    /// when this call read them; until then an empty list does not prove the wallet is empty.
    /// </summary>
    public sealed record HistoryLoad(List<TransactionRecord> Transactions, bool Complete, bool AddressesSettled);

    /// <summary>
    /// <see cref="GetTransactionHistoryAsync"/> together with that call's own completeness. Use this, not
    /// <see cref="LastHistoryIncomplete"/>, when two loads can overlap: the property is shared by every call.
    /// </summary>
    public async Task<List<TransactionRecord>> GetTransactionHistoryAsync(int skip = 0, int take = 50)
    {
        var load = await LoadTransactionHistoryAsync(skip, take);
        LastHistoryIncomplete = !load.Complete;
        return load.Transactions;
    }

    /// <summary>
    /// The wallet's complete history for the CSV export (spec 031, research R1, FR-003/FR-006), newest first. Unlike
    /// <see cref="LoadTransactionHistoryAsync"/> it has no per-address cap and never settles for less: any address that can't be read,
    /// even partway through, throws <see cref="Export.HistoryIncompleteException"/>. It never uses the saved copy Activity shows
    /// offline, and writes nothing. Records are built exactly as for Activity, so the two never disagree.
    /// </summary>
    public async Task<List<TransactionRecord>> LoadCompleteHistoryAsync(
        IProgress<Export.HistoryExportProgress>? progress = null, CancellationToken ct = default)
    {
        EnsureUnlocked();
        ct.ThrowIfCancellationRequested();
        await WaitForAddressDiscoveryAsync(AddressDiscoveryWait);
        if (!AddressesSettled)
            throw new Export.HistoryIncompleteException("The wallet is still finding which of its addresses are used.");

        var walletId = _activeWalletId;
        var localTxs = await _txTracker.GetAllAsync();

        // Regtest has no explorer: the locally tracked transactions are the whole history, as on Activity.
        var effectiveMode = ActiveWallet?.WifNetwork ?? _networkMode;
        if (effectiveMode == "regtest")
        {
            progress?.Report(new(1, 1));
            return localTxs.OrderByDescending(t => t.Timestamp).ToList();
        }

        var rawAddresses = GetAllAddresses();
        var ourNormalized = new HashSet<string>(rawAddresses.Select(NormalizeAddress));
        bool isOurs(string? addr) => addr != null && ourNormalized.Contains(NormalizeAddress(addr));
        var allTxs = new Dictionary<string, TransactionInfo>();

        try
        {
            var onePass = false;
            if (_blockchain is FallbackBlockchainService fallback && fallback.TryGetWalletHistorySource() is { } walletHistory)
            {
                try
                {
                    foreach (var tx in await walletHistory.GetWalletTransactionsAsync(rawAddresses))
                        allTxs.TryAdd(tx.TxId, tx);
                    onePass = true;
                    progress?.Report(new(rawAddresses.Count, rawAddresses.Count));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"[Export] Gateway history unavailable: {ex.GetType().Name}");
                    allTxs.Clear();
                }
            }

            if (!onePass)
            {
                var done = 0;
                progress?.Report(new(0, rawAddresses.Count));
                using var semaphore = new SemaphoreSlim(10);
                var reads = rawAddresses.Select(async addr =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var txs = await _blockchain.GetCompleteAddressHistoryAsync(addr, ct);
                        progress?.Report(new(Interlocked.Increment(ref done), rawAddresses.Count));
                        return txs;
                    }
                    finally { semaphore.Release(); }
                });
                foreach (var list in await Task.WhenAll(reads))
                    foreach (var tx in list)
                        allTxs.TryAdd(tx.TxId, tx);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Export.HistoryIncompleteException) { throw; }
        catch (Exception ex)
        {
            throw new Export.HistoryIncompleteException("The complete history could not be read.", ex);
        }

        // A wallet switch while reading would mix two wallets' records.
        if (_activeWalletId != walletId)
            throw new Export.HistoryIncompleteException("The active wallet changed while its history was read.");

        var records = BuildExplorerRecords(allTxs.Values, rawAddresses, isOurs);
        return MergeHistory(records, localTxs).Values.OrderByDescending(t => t.Timestamp).ToList();
    }

    public async Task<HistoryLoad> LoadTransactionHistoryAsync(int skip = 0, int take = 50)
    {
        EnsureUnlocked();
        await WaitForAddressDiscoveryAsync(AddressDiscoveryWait);

        // Completeness belongs to this call (kept in locals), so overlapping loads cannot be mistaken for each other.
        var incomplete = false;
        var addressesSettled = AddressesSettled;

        // Always return locally tracked transactions (works on all networks)
        var localTxs = await _txTracker.GetAllAsync();

        // On mainnet/testnet, also try the Esplora explorer for full history
        var effectiveMode = ActiveWallet?.WifNetwork ?? _networkMode;
        if (effectiveMode != "regtest")
        {
            try
            {
                // Build a set of normalized addresses for comparison.
                // Esplora may return bech32 addresses with a different checksum variant
                // than NBitcoin generates; stripping the 6-char checksum from dgb1/dgbt1
                // addresses makes comparison encoding-independent.
                var rawAddresses = GetAllAddresses();
                // The wallet these addresses belong to. The user can switch wallets while the reads below are awaited, so
                // everything saved from here on is saved for this wallet, never for whichever one is active by then.
                var walletId = _activeWalletId;
                var network = HistoryNetwork;
                bool stillActive() => _activeWalletId == walletId;
                var ourNormalized = new HashSet<string>(rawAddresses.Select(NormalizeAddress));
                bool isOurs(string? addr) => addr != null && ourNormalized.Contains(NormalizeAddress(addr));

                // For the gateway record (FR-009a): the explorer tip is read before the snapshot is taken. Only when the feature is on.
                var tipBeforeSnapshot = _gatewayBootstrap is { IsEnabled: true } ? await _gatewayBootstrap.ReadTipAsync() : null;

                var allTxs = new Dictionary<string, TransactionInfo>();
                var changeOnly = false;
                var onePass = false;

                // 1. The SPV gateway's own record, in one pass for all addresses, while it answers history (spec 023, FR-020).
                if (_blockchain is FallbackBlockchainService fallback && fallback.TryGetWalletHistorySource() is { } walletHistory)
                {
                    try
                    {
                        foreach (var tx in await walletHistory.GetWalletTransactionsAsync(rawAddresses))
                            allTxs.TryAdd(tx.TxId, tx);
                        onePass = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TxHistory] Gateway history unavailable: {ex.GetType().Name}");
                    }
                }

                // 2. Change-only sync, then 3. every address in full.
                if (!onePass && ChangeOnlySync && walletId is not null)
                {
                    try
                    {
                        // Change-only sync (spec 023): one summary per address, history pages only where something changed.
                        var synced = await _historySync!.RefreshAsync(walletId, network, rawAddresses, BuildHotHints());
                        foreach (var tx in synced.Transactions)
                            allTxs.TryAdd(tx.TxId, tx);
                        changeOnly = true;
                    }
                    catch (SummaryUnavailableException ex)
                    {
                        // Without a cause, summaries do not exist on this route (regtest, the gateway answering first): every
                        // address is read in full below, as before. With one, the explorers were asked and could not answer.
                        // The read below still runs, so the page shows what it can, but it answers "no transactions" for an
                        // address it could not reach, so its result cannot show that the chain was read (spec 025, FR-015).
                        if (ex.InnerException is not null) incomplete = true;
                    }
                }

                if (!onePass && !changeOnly)
                {
                    var semaphore = new SemaphoreSlim(10);
                    var txTasks = rawAddresses.Select(async addr =>
                    {
                        await semaphore.WaitAsync();
                        try { return await _blockchain.GetAddressTransactionsAsync(addr, 0, take); }
                        finally { semaphore.Release(); }
                    });
                    var txResults = await Task.WhenAll(txTasks);
                    foreach (var txInfos in txResults)
                        foreach (var tx in txInfos)
                            allTxs.TryAdd(tx.TxId, tx);
                }

                if (stillActive()) ReportTransactions(allTxs.Values, rawAddresses);

                // Offer what the explorer returned to the gateway record. Runs in the background and never changes this result.
                _gatewayBootstrap?.Offer(walletId ?? string.Empty, rawAddresses, tipBeforeSnapshot, allTxs.Values.ToList(),
                    servedByGateway: onePass || _blockchain is FallbackBlockchainService { ActiveBackend: "spv-gateway" });

                var explorerRecords = BuildExplorerRecords(allTxs.Values, rawAddresses, isOurs);

                var merged = MergeHistory(explorerRecords, localTxs);
                var localToCheck = localTxs.ToList();

                // A local record replaces the explorer's version above, so give it the addresses the explorer found
                // and save them: they then still show after a reload when the explorer is unreachable.
                var explorerById = explorerRecords.ToDictionary(r => r.TxId);
                var filled = TransactionAddressResolver.CopyMissing(localToCheck, explorerById);
                if (filled.Count > 0 && stillActive())
                    await _txTracker.SetWalletAddressesAsync(filled.ToDictionary(
                        r => r.TxId, r => (r.WalletAddresses, r.WalletReceivedAddresses)));

                localTxs = merged.Values.ToList();

                // The tracker follows the active wallet: after a switch, a write would land in the other wallet's records.
                if (stillActive())
                    await RepairUnderRecordedBatchesAsync(localToCheck, allTxs, isOurs);

                // Once per unlock, after the change-only result is built: re-read every address in full in the background (FR-009).
                if (changeOnly)
                    _historySync!.EnsureFullResync(walletId!, network, rawAddresses);
            }
            catch (Exception ex)
            {
                incomplete = true;
                Console.WriteLine($"[TxHistory] Explorer failed: {ex.Message}");
            }
        }

        // Label rows the gateway record knows (checked, unchecked, pending). Nothing changes for rows it does not know, or when the feature is off.
        if (_syncStates is not null && _gatewayBootstrap is { IsEnabled: true } && _activeWalletId is not null)
        {
            try
            {
                if (await _syncStates.GetAsync(_activeWalletId) is { } record)
                    GatewayVerificationLabeler.Apply(localTxs, record, GatewayCapabilities.WalletPowVerified);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Could not label history: {ex.GetType().Name}");
            }
        }

        // Update confirmation counts in background
        _ = _txTracker.UpdateConfirmationsAsync(_blockchain);

        var page = localTxs
            .OrderByDescending(t => t.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToList();
        return new HistoryLoad(page, Complete: !incomplete, AddressesSettled: addressesSettled);
    }

    /// <summary>
    /// Turns explorer transactions into this wallet's records: direction, amount, counterparty, note, DigiDollar summary and the
    /// wallet's own addresses. Shared by the Activity load and the complete export read (spec 031, research R1), so they never disagree.
    /// </summary>
    private List<TransactionRecord> BuildExplorerRecords(
        IEnumerable<TransactionInfo> txs, IReadOnlyCollection<string> rawAddresses, Func<string?, bool> isOurs)
    {
        var ownByKey = new Dictionary<string, string>();
        foreach (var a in rawAddresses) ownByKey.TryAdd(NormalizeAddress(a), a);
        var changeKeys = GetChangeAddressKeys();
        var singleKey = _singleKey != null;

        // A transaction that neither spends from nor pays any of this wallet's addresses is not this wallet's. Saved
        // history can hold such rows (another wallet's, saved here by an older version when the wallet was switched
        // mid-load); shown, each would read as "Received 0".
        return txs
            .Where(tx => tx.Inputs.Any(i => isOurs(i.Address)) || tx.Outputs.Any(o => isOurs(o.Address)))
            .Select(tx =>
            {
                var isSent = tx.Inputs.Any(i => isOurs(i.Address));
                var (walletAddresses, receivedTo) = TransactionAddressResolver.Resolve(tx, isSent, ownByKey, changeKeys, singleKey);
                var digiDollar = DigiDollarTxSummary.TryRead(tx.Outputs);
                var matchingOutputs = tx.Outputs.Where(o => isOurs(o.Address)).ToList();
                var amount = isSent
                    ? tx.Outputs.Where(o => !isOurs(o.Address)).Sum(o => o.AmountSatoshis)
                    : matchingOutputs.Sum(o => o.AmountSatoshis);

                return new TransactionRecord
                {
                    TxId = tx.TxId,
                    Direction = isSent ? TransactionDirection.Sent : TransactionDirection.Received,
                    AmountSatoshis = amount,
                    FeeSatoshis = tx.FeeSatoshis,
                    Timestamp = tx.Timestamp,
                    Confirmations = tx.Confirmations,
                    CounterpartyAddress = digiDollar is not null ? null : isSent
                        ? tx.Outputs.FirstOrDefault(o => !isOurs(o.Address))?.Address
                        : tx.Inputs.FirstOrDefault(i => !isOurs(i.Address))?.Address,
                    // The "DD" marker inside a DigiDollar OP_RETURN is not a note the sender wrote.
                    Memo = digiDollar is null ? ExtractOpReturnMemo(tx.Outputs) : null,
                    DigiDollar = digiDollar,
                    WalletAddresses = walletAddresses,
                    WalletReceivedAddresses = receivedTo,
                };
            }).ToList();
    }

    /// <summary>Explorer records plus locally tracked ones; a local record wins on the same transaction id (it is fresher).</summary>
    private static Dictionary<string, TransactionRecord> MergeHistory(
        IEnumerable<TransactionRecord> explorerRecords, IEnumerable<TransactionRecord> localTxs)
    {
        var merged = new Dictionary<string, TransactionRecord>();
        foreach (var tx in explorerRecords) merged.TryAdd(tx.TxId, tx);
        foreach (var tx in localTxs) merged[tx.TxId] = tx; // local overwrites
        return merged;
    }

    /// <summary>
    /// Corrects batch sends recorded before SendBatchAsync stored the full amount: those records hold
    /// only the first recipient's amount. A record is changed only if the tx pays at least two external
    /// outputs and the record matches one of them exactly, by both address and amount. Anything else is
    /// left alone, because raising an amount wrongly would use up the user's spending limit.
    /// </summary>
    private async Task RepairUnderRecordedBatchesAsync(
        List<TransactionRecord> localTxs,
        Dictionary<string, TransactionInfo> explorerTxs,
        Func<string?, bool> isOurs)
    {
        var repairs = new Dictionary<string, long>();
        foreach (var record in localTxs.Where(t => t.Direction == TransactionDirection.Sent))
        {
            if (record.CounterpartyAddress == null || !explorerTxs.TryGetValue(record.TxId, out var tx))
                continue;

            var external = tx.Outputs.Where(o => o.AmountSatoshis > 0 && !isOurs(o.Address)).ToList();
            if (external.Count < 2 || external.Sum(o => o.AmountSatoshis) <= record.AmountSatoshis)
                continue;

            var recipient = NormalizeAddress(record.CounterpartyAddress);
            if (!external.Any(o => o.AmountSatoshis == record.AmountSatoshis && NormalizeAddress(o.Address) == recipient))
                continue;

            // A single send whose change lies past the scanned range looks the same at this point.
            // Deriving more addresses is slow in WASM, so it waits until a record gets this far.
            var unscannedChange = GetUnscannedChangeAddresses();
            external = external.Where(o => !unscannedChange.Contains(NormalizeAddress(o.Address))).ToList();
            var total = external.Sum(o => o.AmountSatoshis);
            if (external.Count >= 2 && total > record.AmountSatoshis)
                repairs[record.TxId] = total;
        }

        if (repairs.Count > 0)
            await _txTracker.RaiseSendAmountsAsync(repairs);
    }

    /// <summary>
    /// Normalized change addresses past the ones always scanned. GetAllAddresses only reaches as far as
    /// NextChangeIndex, which an older send's change can lie beyond, so the repair checks a wider range.
    /// Empty for WIF wallets, whose change is always scanned.
    /// </summary>
    private HashSet<string> GetUnscannedChangeAddresses()
    {
        if (_unscannedChangeAddresses != null && _unscannedChangeWalletId == _activeWalletId)
            return _unscannedChangeAddresses;

        var addresses = new HashSet<string>();
        if (_singleKey == null && _hd != null)
        {
            var end = Math.Max(UnscannedChangeLimit, (ActiveWallet?.NextChangeIndex ?? 0) + 10);
            for (int i = MinScannedChange; i < end; i++)
            {
                addresses.Add(NormalizeAddress(_hd.DeriveChangeAddress(i, ScriptPubKeyType.Segwit).ToString()));
                addresses.Add(NormalizeAddress(_hd.DeriveChangeAddress(i, ScriptPubKeyType.Legacy).ToString()));
            }
        }

        _unscannedChangeAddresses = addresses;
        _unscannedChangeWalletId = _activeWalletId;
        return addresses;
    }

    public Task<List<Contact>> GetContactsAsync() => Task.FromResult(new List<Contact>());
    public Task AddContactAsync(Contact contact) => Task.CompletedTask;
    public Task RemoveContactAsync(string contactId) => Task.CompletedTask;

    /// <summary>
    /// Delete the specified wallet and all associated data.
    /// If deleting the active wallet, switches to another if available.
    /// </summary>
    public async Task DeleteWalletAsync(string walletId)
    {
        _sessions.Remove(walletId);
        await _walletStore.DeleteWalletAsync(walletId);
        // The gateway sync record goes with the wallet (FR-009a); it is rebuilt by the bootstrap rule if the wallet is restored.
        if (_syncStates is not null)
            await _syncStates.DeleteAsync(walletId);
        if (_gatewayRestore is not null)
        {
            await _gatewayRestore.Birth.DeleteAsync(walletId);
            await _gatewayRestore.Progress.DeleteAsync(walletId);
        }

        // Saved chain data goes with the wallet too (spec 023, FR-025): summaries and chain history on every network, the tracked
        // sends and receives, and the figures the pages show at once.
        if (_historySync is not null)
            await _historySync.ForgetAsync(walletId);
        else
            await new AddressSummaryStore(_walletStore.Storage).DeleteAsync(walletId);
        await _walletStore.Storage.RemoveAsync(TransactionTracker.StorageKey(walletId));
        foreach (var key in WalletSnapshotKeys(walletId))
            await _walletStore.Storage.RemoveAsync(key);
        // Whether the wallet started empty and whether its first payment was celebrated (spec 025, FR-028). The per-device
        // "first session ended" marker stays.
        await _walletStore.Storage.RemoveAsync(FirstReceiveStore.StateKey(walletId));
        // How the wallet came to be here and whether its "search further" offer was answered (spec 027).
        await _walletStore.Storage.RemoveAsync(SearchOfferStore.OriginKey(walletId));
        await _walletStore.Storage.RemoveAsync(SearchOfferStore.AnswerKey(walletId));

        if (_activeWalletId == walletId)
        {
            // Switch to another unlocked wallet, or the first available
            var remaining = await _walletStore.GetAllWalletInfosAsync();
            if (remaining.Count > 0)
            {
                var next = remaining.First();
                _activeWalletId = next.Id;
                _txTracker.SetActiveWallet(next.Id);
                await _walletStore.SetActiveWalletIdAsync(next.Id);
            }
            else
            {
                _activeWalletId = null;
            }
        }
    }

    /// <summary>
    /// The page-snapshot keys the web app keeps per wallet (balance, history and when they were read). The shared price key
    /// (<c>cache:dgbPrice</c>) belongs to no wallet, so it is not listed.
    /// </summary>
    public static IReadOnlyList<string> WalletSnapshotKeys(string walletId) =>
        [$"cache:balance:{walletId}", $"cache:transactions:{walletId}", $"cache:lastFetch:{walletId}"];

    /// <summary>
    /// Delete the active wallet (legacy overload for backward compat).
    /// </summary>
    public async Task DeleteWalletAsync()
    {
        var walletId = await _walletStore.GetActiveWalletIdAsync();
        if (walletId != null)
            await DeleteWalletAsync(walletId);
    }

    /// <summary>
    /// Wipe everything — all wallets, contacts, tx history, preferences.
    /// </summary>
    public async Task WipeAllDataAsync()
    {
        await _walletStore.ClearAllAsync();
        if (_syncStates is not null)
            await _syncStates.DeleteAllAsync();
        _sessions.Clear();
        _activeWalletId = null;
    }

    /// <summary>
    /// Scans old BIP44 addresses (m/44'/20'/...) for any remaining balance.
    /// Returns total satoshis found and the list of addresses with funds.
    /// Used to detect funds stranded after the BIP44→BIP84 migration.
    /// </summary>
    public async Task<(long TotalSatoshis, List<string> Addresses)> ScanLegacyBip44BalanceAsync()
    {
        EnsureUnlocked();
        if (_hd == null || _hd.IsWatchOnly) return (0, []);

        var legacyMap = _hd.GetLegacyBip44AddressMap();
        var addressesWithFunds = new List<string>();
        long total = 0;

        foreach (var addr in legacyMap.Keys)
        {
            try
            {
                var bal = await _blockchain.GetBalanceAsync(addr);
                if (bal > 0)
                {
                    total += bal;
                    addressesWithFunds.Add(addr);
                }
            }
            catch { }
        }

        return (total, addressesWithFunds);
    }

    /// <summary>
    /// Gets all addresses the current wallet has used (receiving + change), for UTXO scanning.
    /// </summary>
    public IEnumerable<string> GetAllUsedAddresses()
    {
        EnsureUnlocked();
        var network = CurrentNetwork;

        if (_singleKey != null)
        {
            yield return _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network).ToString();
            yield return _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString();
        }
        else
        {
            var map = GetAddressKeyMap();
            foreach (var addr in map.Keys)
                yield return addr;
        }
    }

    /// <summary>
    /// Consolidates all UTXOs into a single UTXO by sending everything to the wallet's own receive address.
    /// </summary>
    public async Task<string> ConsolidateUtxosAsync(int feeRateSatPerByte = 100)
    {
        EnsureUnlocked();
        if (ActiveWallet?.WalletType == "xpub")
            throw new InvalidOperationException("Watch-only wallets cannot consolidate UTXOs.");

        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);

        var network = CurrentNetwork;
        List<DigiByte.Crypto.Transactions.Utxo> availableUtxos;
        BitcoinAddress destination;

        if (_singleKey != null)
        {
            var legacyAddr = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, network);
            var segwitAddr = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();

            foreach (var u in await _blockchain.GetUtxosAsync(legacyAddr.ToString()))
            {
                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = legacyAddr.ScriptPubKey,
                    PrivateKey = _singleKey,
                    Confirmations = u.Confirmations,
                });
            }
            foreach (var u in await _blockchain.GetUtxosAsync(segwitAddr.ToString()))
            {
                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = segwitAddr.ScriptPubKey,
                    PrivateKey = _singleKey,
                    Confirmations = u.Confirmations,
                });
            }

            destination = segwitAddr;
        }
        else
        {
            var addressKeyMap = GetAddressKeyMap();
            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();
            var utxoResults = await ReadCoinsAsync(addressKeyMap, (plan, found, summariesConfirmed) => SendCoinPlanner.NeedsRestForConsolidate(plan, found, summariesConfirmed));
            foreach (var (addr, key, utxos) in utxoResults)
            {
                var addrScript = BitcoinAddress.Create(addr, network).ScriptPubKey;
                foreach (var utxoInfo in utxos)
                {
                    availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                    {
                        TransactionId = uint256.Parse(utxoInfo.TxId),
                        OutputIndex = utxoInfo.OutputIndex,
                        Amount = Money.Satoshis(utxoInfo.AmountSatoshis),
                        ScriptPubKey = !string.IsNullOrEmpty(utxoInfo.ScriptPubKey)
                            ? Script.FromHex(utxoInfo.ScriptPubKey)
                            : addrScript,
                        PrivateKey = key.PrivateKey,
                        Confirmations = utxoInfo.Confirmations,
                    });
                }
            }

            destination = _hd!.DeriveReceivingAddress(0);
        }

        if (availableUtxos.Count <= 1)
            throw new InvalidOperationException("Only one or zero UTXOs — no consolidation needed.");

        var totalSatoshis = availableUtxos.Sum(u => u.Amount.Satoshi);
        var effectiveFeeRate = Math.Max(feeRateSatPerByte, 100);
        var feeRate = new FeeRate(Money.Satoshis(effectiveFeeRate * 1000));

        var txBuilder = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network);
        var tx = txBuilder.BuildSendAllTransaction(availableUtxos, destination, feeRate);

        var rawTx = tx.ToBytes();
        var txId = await BroadcastAsync(rawTx);
        await RecordOwnSendAsync(rawTx);

        var feePaid = tx.GetFee(availableUtxos.Select(u => u.ToCoin()).ToArray());
        var amountAfterFee = totalSatoshis - (feePaid?.Satoshi ?? 0);
        await _txTracker.RecordSendAsync(txId, destination.ToString(), amountAfterFee,
            feePaid?.Satoshi ?? 0, $"UTXO consolidation ({availableUtxos.Count} → 1)");

        return txId;
    }

    /// <summary>
    /// Sweeps all funds from old BIP44 addresses to the current BIP84 receiving address.
    /// Builds a transaction spending all BIP44 UTXOs to the BIP84 address at index 0.
    /// </summary>
    public async Task<string> SweepLegacyBip44FundsAsync(int feeRateSatPerByte = 100)
    {
        EnsureUnlocked();
        if (_hd == null || _hd.IsWatchOnly)
            throw new InvalidOperationException("Sweep requires an HD wallet with private keys.");

        var network = CurrentNetwork;
        var legacyMap = _hd.GetLegacyBip44AddressMap();

        // Collect UTXOs from all legacy BIP44 addresses
        var utxos = new List<DigiByte.Crypto.Transactions.Utxo>();
        foreach (var (addr, key) in legacyMap)
        {
            var addrUtxos = await _blockchain.GetUtxosAsync(addr);
            var addrScript = BitcoinAddress.Create(addr, network).ScriptPubKey;
            foreach (var u in addrUtxos)
            {
                utxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = !string.IsNullOrEmpty(u.ScriptPubKey)
                        ? Script.FromHex(u.ScriptPubKey)
                        : addrScript,
                    PrivateKey = key.PrivateKey,
                    Confirmations = u.Confirmations,
                });
            }
        }

        if (utxos.Count == 0)
            throw new InvalidOperationException("No funds found on legacy BIP44 addresses.");

        // Send everything to current BIP84 receiving address (index 0)
        var destination = _hd.DeriveReceivingAddress(0);
        var totalSatoshis = utxos.Sum(u => u.Amount.Satoshi);

        // Build a send-all transaction (amount = total minus fee)
        var effectiveFeeRate = Math.Max(feeRateSatPerByte, 100);
        var feeRate = new FeeRate(Money.Satoshis(effectiveFeeRate * 1000));

        var txBuilder = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network);
        var tx = txBuilder.BuildSendAllTransaction(utxos, destination, feeRate);

        var rawTx = tx.ToBytes();
        var txId = await BroadcastAsync(rawTx);

        // Track the sweep transaction
        var feePaid = tx.GetFee(utxos.Select(u => u.ToCoin()).ToArray());
        var amountAfterFee = totalSatoshis - (feePaid?.Satoshi ?? 0);
        await _txTracker.RecordSendAsync(txId, destination.ToString(), amountAfterFee,
            feePaid?.Satoshi ?? 0, "BIP44→BIP84 migration sweep");

        return txId;
    }

    /// <summary>
    /// Scans the current wallet's old derivation paths (BIP44 + BIP49) for any funds.
    /// Unlike ScanLegacyBip44BalanceAsync (BIP44 only), this checks all non-active paths.
    /// Returns discovered addresses with balances and path labels.
    /// </summary>
    public async Task<List<RecoveryAddressInfo>> ScanCurrentWalletOldPathsAsync(IProgress<int>? progress = null, Action<string>? onLabel = null)
    {
        EnsureUnlocked();
        if (_hd == null || _hd.IsWatchOnly) return [];

        var oldAddresses = _hd.DeriveOldPathAddresses();
        var results = new List<RecoveryAddressInfo>();
        int total = oldAddresses.Count;
        int done = 0;
        string? lastLabel = null;

        foreach (var (address, privateKey, pathLabel) in oldAddresses)
        {
            if (pathLabel != lastLabel) { lastLabel = pathLabel; onLabel?.Invoke(pathLabel); }
            try
            {
                var bal = await _blockchain.GetBalanceAsync(address);
                if (bal > 0)
                    results.Add(new RecoveryAddressInfo(address, privateKey, pathLabel, bal));
            }
            catch { }
            done++;
            progress?.Report(total > 0 ? (int)(done * 100.0 / total) : 0);
        }
        return results;
    }

    /// <summary>
    /// Sweeps funds from old derivation paths (BIP44 + BIP49) into the current BIP84 address.
    /// </summary>
    public async Task<string> SweepCurrentWalletOldPathsAsync(List<RecoveryAddressInfo> sources, int feeRateSatPerByte = 100)
    {
        EnsureUnlocked();
        if (_hd == null)
            throw new InvalidOperationException("Sweep requires an HD wallet.");

        // Reuse the external sweep logic — destination is the current wallet's BIP84 address
        return await SweepExternalFundsAsync(sources, feeRateSatPerByte);
    }

    /// <summary>
    /// Scans an external mnemonic across BIP44/49/84 derivation paths for funds.
    /// Returns discovered addresses with balances grouped by path type.
    /// </summary>
    public async Task<List<RecoveryAddressInfo>> ScanExternalMnemonicAsync(string mnemonicWords, IProgress<int>? progress = null, Action<string>? onLabel = null)
    {
        var mnemonic = MnemonicGenerator.FromWords(mnemonicWords);
        var network = CurrentNetwork;
        var allAddresses = HdKeyDerivation.DeriveMultiPathAddresses(mnemonic, network);

        var results = new List<RecoveryAddressInfo>();
        int total = allAddresses.Count;
        int done = 0;
        string? lastLabel = null;

        foreach (var (address, privateKey, pathLabel) in allAddresses)
        {
            if (pathLabel != lastLabel) { lastLabel = pathLabel; onLabel?.Invoke(pathLabel); }
            try
            {
                var bal = await _blockchain.GetBalanceAsync(address);
                if (bal > 0)
                    results.Add(new RecoveryAddressInfo(address, privateKey, pathLabel, bal));
            }
            catch { }
            done++;
            progress?.Report(total > 0 ? (int)(done * 100.0 / total) : 0);
        }
        return results;
    }

    /// <summary>
    /// Scans an external WIF private key for funds on all address types.
    /// </summary>
    public async Task<List<RecoveryAddressInfo>> ScanExternalWifAsync(string wif, IProgress<int>? progress = null, Action<string>? onLabel = null)
    {
        var network = PrivateKeyImporter.DetectNetwork(wif) ?? CurrentNetwork;
        var key = PrivateKeyImporter.ParseWif(wif, network);
        var allAddresses = HdKeyDerivation.DeriveWifAddresses(key, network);

        var results = new List<RecoveryAddressInfo>();
        int total = allAddresses.Count;
        int done = 0;
        string? lastLabel = null;

        foreach (var (address, privateKey, pathLabel) in allAddresses)
        {
            if (pathLabel != lastLabel) { lastLabel = pathLabel; onLabel?.Invoke(pathLabel); }
            try
            {
                var bal = await _blockchain.GetBalanceAsync(address);
                if (bal > 0)
                    results.Add(new RecoveryAddressInfo(address, privateKey, pathLabel, bal));
            }
            catch { }
            done++;
            progress?.Report(total > 0 ? (int)(done * 100.0 / total) : 0);
        }
        return results;
    }

    /// <summary>
    /// Sweeps funds from externally-scanned addresses into the current wallet's receiving address.
    /// </summary>
    public async Task<string> SweepExternalFundsAsync(List<RecoveryAddressInfo> sources, int feeRateSatPerByte = 100)
    {
        EnsureUnlocked();
        var network = CurrentNetwork;

        // Collect UTXOs from all source addresses
        var utxos = new List<DigiByte.Crypto.Transactions.Utxo>();
        foreach (var src in sources)
        {
            var addrUtxos = await _blockchain.GetUtxosAsync(src.Address);
            var addrScript = BitcoinAddress.Create(src.Address, network).ScriptPubKey;
            foreach (var u in addrUtxos)
            {
                utxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(u.TxId),
                    OutputIndex = u.OutputIndex,
                    Amount = Money.Satoshis(u.AmountSatoshis),
                    ScriptPubKey = !string.IsNullOrEmpty(u.ScriptPubKey)
                        ? Script.FromHex(u.ScriptPubKey)
                        : addrScript,
                    PrivateKey = src.PrivateKey,
                    Confirmations = u.Confirmations,
                });
            }
        }

        if (utxos.Count == 0)
            throw new InvalidOperationException("No spendable UTXOs found on the scanned addresses.");

        // Determine destination: current wallet's receiving address
        BitcoinAddress destination;
        if (_hd != null)
            destination = _hd.DeriveReceivingAddress(0);
        else if (_singleKey != null)
            destination = _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network);
        else
            throw new InvalidOperationException("No active wallet to receive swept funds.");

        var totalSatoshis = utxos.Sum(u => u.Amount.Satoshi);
        var effectiveFeeRate = Math.Max(feeRateSatPerByte, 100);
        var feeRate = new FeeRate(Money.Satoshis(effectiveFeeRate * 1000));

        var txBuilder = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network);
        var tx = txBuilder.BuildSendAllTransaction(utxos, destination, feeRate);

        var rawTx = tx.ToBytes();
        var txId = await BroadcastAsync(rawTx);

        var feePaid = tx.GetFee(utxos.Select(u => u.ToCoin()).ToArray());
        var amountAfterFee = totalSatoshis - (feePaid?.Satoshi ?? 0);
        await _txTracker.RecordSendAsync(txId, destination.ToString(), amountAfterFee,
            feePaid?.Satoshi ?? 0, "Recovery sweep");

        return txId;
    }

    /// <summary>
    /// Mirrors the protection fields onto an unlocked session's cached wallet info, so pages that read
    /// <see cref="ActiveWallet"/> see a credential change straight away without a reload.
    /// </summary>
    private void SyncSessionProtection(WalletInfo saved)
    {
        if (_sessions.TryGetValue(saved.Id, out var session))
        {
            session.Info.CredentialType = saved.CredentialType;
            session.Info.ProtectionLevel = saved.ProtectionLevel;
            session.Info.PassphraseNudgeDismissed = saved.PassphraseNudgeDismissed;
        }
    }

    private static string CredentialTypeName(CredentialKind kind) =>
        kind == CredentialKind.Passphrase ? "passphrase" : "pin";

    /// <summary>
    /// Refuses a passphrase that breaks the rules (FR-002) before anything is stored. The UI shows the reason first;
    /// this is the backstop.
    /// </summary>
    private void RequireAcceptableCredential(string credential, CredentialKind kind)
    {
        if (kind == CredentialKind.Passphrase)
        {
            var check = CredentialPolicy.Check(credential, _passwordList);
            if (!check.Accepted)
                throw new ArgumentException($"Passphrase not accepted: {check.Reason}.", nameof(credential));
        }
    }

    /// <summary>Stores a brand-new wallet secret and returns its protection level ("standard" or "legacy").</summary>
    private async Task<string> StoreNewSecretAsync(string walletId, string secret, string credential, CredentialKind kind, bool allowLegacy)
    {
        var outcome = await _vault.SealAsync(WalletKeyStore.SeedSlot(walletId),
            System.Text.Encoding.UTF8.GetBytes(secret), credential, kind, allowLegacy);
        return outcome switch
        {
            ProtectedSecretStore.SealOutcome.V2 => "standard",
            ProtectedSecretStore.SealOutcome.Legacy => "legacy",
            ProtectedSecretStore.SealOutcome.NeedsLegacyConsent =>
                throw new LegacyProtectionConsentRequiredException(_vault.LastSealFailure ?? CryptoFailure.Unsupported),
            _ => throw new InvalidOperationException("The wallet could not be protected on this device."),
        };
    }

    /// <summary>
    /// Changes how a wallet is protected: PIN to passphrase, passphrase to PIN, or a new value of the same kind. The
    /// current credential is required; nothing is written unless it is right and the new value verifies; the old
    /// copies (including any legacy copy) are removed at once.
    /// </summary>
    public async Task<ChangeCredentialOutcome> ChangeCredentialAsync(string walletId, string currentCredential,
        string newCredential, CredentialKind newKind)
    {
        var wallet = await GetWalletAsync(walletId);
        if (wallet == null) return ChangeCredentialOutcome.Failed;

        if (newKind == CredentialKind.Passphrase)
        {
            if (!CredentialPolicy.Check(newCredential, _passwordList).Accepted)
                return ChangeCredentialOutcome.WeakPassphrase;
        }
        else if (!CredentialPolicy.IsValidPin(newCredential))
        {
            return ChangeCredentialOutcome.Failed;
        }

        var currentKind = wallet.CredentialType == "passphrase" ? CredentialKind.Passphrase : CredentialKind.Pin;
        var opened = await _vault.OpenAsync(WalletKeyStore.SeedSlot(walletId), currentCredential, currentKind, migrate: false);
        if (!opened.Success)
            return opened.CountsAsWrongCredential ? ChangeCredentialOutcome.WrongCredential : ChangeCredentialOutcome.Failed;

        var plaintext = opened.Value!;
        try
        {
            var outcome = await _vault.SealAsync(WalletKeyStore.SeedSlot(walletId), plaintext, newCredential, newKind,
                allowLegacy: wallet.ProtectionLevel == "legacy");
            if (outcome is ProtectedSecretStore.SealOutcome.Failed or ProtectedSecretStore.SealOutcome.NeedsLegacyConsent)
                return ChangeCredentialOutcome.Failed;

            wallet.CredentialType = CredentialTypeName(newKind);
            wallet.ProtectionLevel = outcome == ProtectedSecretStore.SealOutcome.Legacy ? "legacy" : "standard";
            await SaveWalletRecordAsync(walletId, wallet);
            SyncSessionProtection(wallet);
            return ChangeCredentialOutcome.Changed;
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    /// <summary>
    /// Whether settings should show the dismissible "use a passphrase" card: only for PIN wallets that have not
    /// dismissed it. There is no balance-based or recurring trigger (clarification: gentle nudge, SC-010).
    /// </summary>
    public async Task<bool> ShouldShowPassphraseNudgeAsync(string walletId)
    {
        var wallet = await GetWalletAsync(walletId);
        return wallet is { CredentialType: "pin", PassphraseNudgeDismissed: false };
    }

    public async Task SetPassphraseNudgeDismissedAsync(string walletId, bool dismissed)
    {
        var wallet = await GetWalletAsync(walletId);
        if (wallet == null || wallet.PassphraseNudgeDismissed == dismissed) return;
        wallet.PassphraseNudgeDismissed = dismissed;
        await SaveWalletRecordAsync(walletId, wallet);
        SyncSessionProtection(wallet);
    }

    /// <summary>
    /// Recovery: re-protects the wallet under a new PIN or passphrase after the user supplies their recovery phrase
    /// (HD), key (imported) or xpub (watch-only). Nothing is written until the secret has been validated and the new
    /// value verified; a failure changes nothing. The old credential stops working and the legacy copy is deleted.
    /// </summary>
    public async Task<bool> ResetPinAsync(string walletId, string seedPhrase, string newCredential, CredentialKind kind = CredentialKind.Pin)
    {
        var wallet = await GetWalletAsync(walletId);
        if (wallet == null) return false;

        string secret;
        if (wallet.WalletType == "privatekey")
        {
            // The user re-imports their WIF key.
            secret = seedPhrase.Trim();
            if (!PrivateKeyImporter.IsValidWif(secret)) return false;
        }
        else if (wallet.WalletType == "xpub")
        {
            // Watch-only: the user re-imports the xpub.
            secret = seedPhrase.Trim();
            if (HdKeyDerivation.ParseXpub(secret) == null) return false;
        }
        else
        {
            secret = string.Join(' ', seedPhrase.Trim().ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (!MnemonicGenerator.IsValid(secret)) return false;
        }

        var outcome = await _vault.SealAsync(WalletKeyStore.SeedSlot(walletId),
            System.Text.Encoding.UTF8.GetBytes(secret), newCredential, kind, allowLegacy: wallet.ProtectionLevel == "legacy");
        if (outcome is ProtectedSecretStore.SealOutcome.Failed or ProtectedSecretStore.SealOutcome.NeedsLegacyConsent) return false;

        var credentialType = kind == CredentialKind.Passphrase ? "passphrase" : "pin";
        var protection = outcome == ProtectedSecretStore.SealOutcome.Legacy ? "legacy" : "standard";
        if (wallet.CredentialType != credentialType || wallet.ProtectionLevel != protection)
        {
            wallet.CredentialType = credentialType;
            wallet.ProtectionLevel = protection;
            await SaveWalletRecordAsync(walletId, wallet);
            SyncSessionProtection(wallet);
        }
        return true;
    }

    public Task<DigiIdResponse> SignDigiIdAsync(DigiIdRequest request)
    {
        EnsureUnlocked();

        if (_hd == null || _hd.IsWatchOnly)
            throw new InvalidOperationException("Digi-ID requires an HD wallet with private keys.");

        var siteIndex = DigiIdService.DeriveSiteIndex(request.Domain);
        var siteKey = _hd.DeriveDigiIdKey(siteIndex);
        var response = DigiIdService.Sign(request, siteKey.PrivateKey);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Returns the site-specific address that <see cref="SignDigiIdAsync"/> would use for
    /// <paramref name="domain"/>, without signing anything. Lets the UI show the user which
    /// address will identify them before they approve.
    /// </summary>
    public string GetDigiIdAddress(string domain)
    {
        EnsureUnlocked();

        if (_hd == null || _hd.IsWatchOnly)
            throw new InvalidOperationException("Digi-ID requires an HD wallet with private keys.");

        var siteKey = _hd.DeriveDigiIdKey(DigiIdService.DeriveSiteIndex(domain));
        return DigiIdService.GetAddress(siteKey.PrivateKey);
    }

    // ─── Digi-ID funds recovery (spec 009) ───
    // A Digi-ID address is a real DigiByte address, but the wallet never watches it. These methods find funds that were sent
    // to one anyway. Every key stays inside this class: results carry addresses and amounts only (FR-015).

    /// <summary>How many Digi-ID address lookups run at once. Each one is still its own request (FR-004b).</summary>
    private const int DigiIdLookupConcurrency = 4;

    private const string DigiIdNotAValidSiteName = "not a valid site name";
    private const string DigiIdCouldNotReachNetwork = "Could not reach the network. Try again.";

    private sealed record DigiIdSite(string Domain, string? Address, string? ScriptHex);

    /// <summary>
    /// The unlocked HD session, or an <see cref="InvalidOperationException"/> that says why recovery cannot run: locked,
    /// watch-only or single-key (no seed, so no Digi-ID addresses), or not set up with a sweep store.
    /// </summary>
    private (UnlockedWalletSession Session, HdKeyDerivation Hd, DigiIdSweepStore Store) RequireDigiIdRecovery()
    {
        EnsureUnlocked();
        var session = ActiveSession!;

        if (session.Hd is null || session.Hd.IsWatchOnly)
            throw new InvalidOperationException("Digi-ID requires an HD wallet with private keys.");
        if (_digiIdSweeps is null)
            throw new InvalidOperationException("Digi-ID recovery is not configured.");

        return (session, session.Hd, _digiIdSweeps);
    }

    /// <summary>
    /// Checks the Digi-ID address of each site for funds, one request per address (FR-004b). The address is derived from the
    /// site with this wallet and never read from the login history. A site that cannot be looked up is reported as
    /// <see cref="DigiIdFundOutcome.CouldNotCheck"/>, never as empty (FR-007). Cancelling returns the findings that finished.
    /// Throws <see cref="InvalidOperationException"/> if the wallet is locked or has no seed; never throws for a network failure.
    /// </summary>
    public async Task<IReadOnlyList<DigiIdFundFinding>> ScanDigiIdFundsAsync(
        IEnumerable<string> domains, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var (session, hd, store) = RequireDigiIdRecovery();

        // The distinct sites first (cheap, no network): a valid site once however it was typed, an invalid entry once as typed.
        var sites = new List<DigiIdSite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in domains ?? [])
        {
            var domain = DigiIdDomain.Normalize(input);
            if (domain is null)
            {
                var typed = (input ?? "").Trim();
                if (seen.Add("invalid:" + typed)) sites.Add(new DigiIdSite(typed, null, null));
                continue;
            }

            if (!seen.Add("site:" + domain)) continue;
            sites.Add(DeriveDigiIdSite(hd, domain));
        }

        if (sites.Count == 0) return [];

        var inFlight = await store.GetInFlightAddressesAsync(session.Info.Id);
        var results = new DigiIdFundFinding?[sites.Count];
        var finished = 0;
        void ReportOne() => progress?.Report((int)(Interlocked.Increment(ref finished) * 100L / sites.Count));

        using var gate = new SemaphoreSlim(DigiIdLookupConcurrency);

        async Task CheckAsync(int index, DigiIdSite site)
        {
            if (site.Address is null)
            {
                results[index] = new DigiIdFundFinding
                {
                    Domain = site.Domain, Address = "", Outcome = DigiIdFundOutcome.CouldNotCheck, Error = DigiIdNotAValidSiteName,
                };
                ReportOne();
                return;
            }

            try { await gate.WaitAsync(ct); }
            catch (OperationCanceledException) { return; }

            try
            {
                results[index] = await LookUpDigiIdSiteAsync(site, inFlight.Contains(site.Address), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                gate.Release();
            }

            ReportOne();
        }

        await Task.WhenAll(sites.Select((site, index) => CheckAsync(index, site)));
        return results.Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>The Digi-ID address a site gets in this wallet (Legacy P2PKH, always mainnet), and its standard script.</summary>
    private static DigiIdSite DeriveDigiIdSite(HdKeyDerivation hd, string normalizedDomain)
    {
        var address = DigiIdService.GetAddress(hd.DeriveDigiIdKey(DigiIdService.DeriveSiteIndex(normalizedDomain)).PrivateKey);
        return new DigiIdSite(normalizedDomain, address, BitcoinAddress.Create(address, DigiByteNetwork.Mainnet).ScriptPubKey.ToHex());
    }

    private readonly record struct DigiIdPlainOutputs(long Confirmed, int ConfirmedCount, long Pending, int OtherCount, List<UtxoInfo> ConfirmedUtxos);

    /// <summary>
    /// Splits an address's outputs into plain DGB (confirmed and pending) and others. An output the address's own standard
    /// script does not describe is not plain DGB and is left alone (FR-016). With no script returned there is nothing to
    /// compare, and the lookup was by this address, so it counts as plain.
    /// </summary>
    private static DigiIdPlainOutputs SplitDigiIdOutputs(IEnumerable<UtxoInfo> utxos, string? ownScriptHex)
    {
        long confirmed = 0, pending = 0;
        var other = 0;
        var confirmedUtxos = new List<UtxoInfo>();
        foreach (var u in utxos)
        {
            if (!string.IsNullOrEmpty(u.ScriptPubKey) && !string.Equals(u.ScriptPubKey, ownScriptHex, StringComparison.OrdinalIgnoreCase))
            {
                other++;
                continue;
            }

            if (u.Confirmations >= 1)
            {
                confirmed += u.AmountSatoshis;
                confirmedUtxos.Add(u);
            }
            else
            {
                pending += u.AmountSatoshis;
            }
        }

        return new DigiIdPlainOutputs(confirmed, confirmedUtxos.Count, pending, other, confirmedUtxos);
    }

    private async Task<DigiIdFundFinding> LookUpDigiIdSiteAsync(DigiIdSite site, bool inFlight, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var plain = SplitDigiIdOutputs(await _blockchain.GetVerifiedUtxosAsync(site.Address!), site.ScriptHex);

            return new DigiIdFundFinding
            {
                Domain = site.Domain,
                Address = site.Address!,
                ConfirmedSatoshis = plain.Confirmed,
                PendingSatoshis = plain.Pending,
                Outcome = plain.Confirmed + plain.Pending > 0 ? DigiIdFundOutcome.Funds : DigiIdFundOutcome.Empty,
                InFlight = inFlight,
                OtherOutputsCount = plain.OtherCount,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The reason shown is fixed on purpose: a raw exception could carry the address or a server's reply.
            return new DigiIdFundFinding
            {
                Domain = site.Domain,
                Address = site.Address!,
                Outcome = DigiIdFundOutcome.CouldNotCheck,
                Error = DigiIdCouldNotReachNetwork,
                InFlight = inFlight,
            };
        }
    }

    /// <summary>
    /// How long a sweep the network does not know yet may stay Pending before it counts as dropped. An explorer can lag behind
    /// the node that took the transaction, so a missing transaction is not failed at once.
    /// </summary>
    public TimeSpan DigiIdSweepDropGrace { get; set; } = TimeSpan.FromMinutes(10);

    private readonly HashSet<string> _digiIdSweepsRunning = new(StringComparer.Ordinal);
    private readonly object _digiIdRunLock = new();

    private const string DigiIdFundsStayMessage = " Your funds are still on the Digi-ID address.";

    /// <summary>
    /// Re-checks the addresses the user selected and works out one sweep per address: its amount, estimated fee, what would
    /// arrive, and a fresh unused receive address to pay to (FR-008, FR-008a, FR-009, FR-010). Saves nothing and sends nothing,
    /// so cancelling leaves the wallet as it was. At most <see cref="DigiIdSweepPlanner.MaxItemsPerRound"/> addresses are
    /// planned, fewer if unused addresses already handed out leave less room; the rest are counted in the plan.
    /// Throws <see cref="InvalidOperationException"/> if the wallet is locked or has no seed, or has too many unused addresses
    /// in a row to hand out more.
    /// </summary>
    public async Task<DigiIdSweepPlan> PlanDigiIdSweepAsync(
        IEnumerable<DigiIdFundFinding> selected, int feeRateSatPerVByte = DigiIdSweepPlanner.MinFeeRateSatPerVByte, CancellationToken ct = default)
    {
        var (session, hd, store) = RequireDigiIdRecovery();
        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);

        var info = session.Info;
        var scanSet = ScanSetOf(info);
        var firstIndex = scanSet.NextNewIndex;

        // The highest destination plus one is saved as the next receive index, and must stay inside the window a restore scans
        // past the last used address. That is what limits how many addresses one confirmation can sweep.
        var room = Math.Min(DigiIdSweepPlanner.MaxItemsPerRound, scanSet.Anchor + ReceiveGapLimit - 1 - firstIndex);
        if (room <= 0)
            throw new InvalidOperationException(TooManyUnusedMessage(scanSet, "Use one of them first, then sweep again"));

        var inFlight = await store.GetInFlightAddressesAsync(info.Id);

        // Each site's address is derived again here, from its name: a finding handed in by the caller is only a pointer.
        var sites = new List<DigiIdSite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in selected ?? [])
        {
            var domain = DigiIdDomain.Normalize(finding.Domain);
            if (domain is null || !seen.Add(domain)) continue;
            sites.Add(DeriveDigiIdSite(hd, domain));
        }

        var knownFunds = (selected ?? []).Where(f => f.Outcome == DigiIdFundOutcome.Funds && f.ConfirmedSatoshis > 0)
            .Select(f => DigiIdDomain.Normalize(f.Domain)).Where(d => d != null).ToHashSet(StringComparer.Ordinal);

        var candidates = new DigiIdSweepCandidate?[sites.Count];
        using var gate = new SemaphoreSlim(DigiIdLookupConcurrency);

        async Task CheckAsync(int index, DigiIdSite site)
        {
            if (inFlight.Contains(site.Address!))
            {
                candidates[index] = new DigiIdSweepCandidate(site.Domain, site.Address!, 0, 0, InFlight: true);
                return;
            }

            // Nothing confirmed was found before, so there is nothing to re-check.
            if (!knownFunds.Contains(site.Domain))
            {
                candidates[index] = new DigiIdSweepCandidate(site.Domain, site.Address!, 0, 0);
                return;
            }

            await gate.WaitAsync(ct);
            try
            {
                var plain = SplitDigiIdOutputs(await _blockchain.GetVerifiedUtxosAsync(site.Address!), site.ScriptHex);
                candidates[index] = new DigiIdSweepCandidate(site.Domain, site.Address!, plain.Confirmed, plain.ConfirmedCount);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                candidates[index] = new DigiIdSweepCandidate(site.Domain, site.Address!, 0, 0, LookupFailed: true);
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(sites.Select((site, index) => CheckAsync(index, site)));

        var destinations = Enumerable.Range(0, room)
            .Select(i => (Index: firstIndex + i, Address: hd.DeriveReceivingAddress(firstIndex + i, ScriptPubKeyType.Segwit).ToString()))
            .ToList();

        return DigiIdSweepPlanner.Build(candidates.Where(c => c is not null).Select(c => c!).ToList(), feeRateSatPerVByte, room, destinations);
    }

    /// <summary>
    /// Sweeps each planned address in its own transaction to its own destination (FR-008a). The new receive index is saved
    /// first, and if that fails nothing is sent. An address that already has a sweep in flight is skipped and its existing
    /// record returned (FR-012). One address failing does not stop the others, and a failed address keeps its funds. If the
    /// wallet locks part way, nothing more is sent and the unsent addresses are recorded as failed with "wallet locked".
    /// Throws <see cref="InvalidOperationException"/> if the wallet is locked or has no seed before it starts.
    /// </summary>
    public async Task<IReadOnlyList<DigiIdSweepRecord>> ExecuteDigiIdSweepAsync(
        DigiIdSweepPlan plan, IProgress<DigiIdSweepRecord>? progress = null, CancellationToken ct = default)
    {
        var (session, hd, store) = RequireDigiIdRecovery();
        if (plan.Items.Count == 0) return [];

        var info = session.Info;
        var walletId = info.Id;

        // Save the highest destination plus one as the next receive index, before anything is sent: the swept funds then land on
        // addresses the wallet scans, and the Receive page shows a fresh address and never one of the destinations.
        var wanted = plan.Items.Max(i => i.DestinationIndex) + 1;
        await session.AddressOnShowGate.WaitAsync();
        try
        {
            if (wanted > info.NextReceivingIndex)
            {
                // Through the same rule as a new address: destinations past the base run become watched entries (spec 027).
                var undo = MoveAddressOnShow(info, plan.Items.Min(i => i.DestinationIndex), wanted);
                try
                {
                    await PersistAddressIndexesAsync(info);
                }
                catch (Exception ex)
                {
                    undo();
                    throw new InvalidOperationException("Could not save the new receive addresses, so nothing was sent. Try again.", ex);
                }
            }
        }
        finally { session.AddressOnShowGate.Release(); }

        var results = new List<DigiIdSweepRecord>(plan.Items.Count);
        foreach (var item in plan.Items)
        {
            if (ct.IsCancellationRequested) break;

            var record = IsStillUnlocked(walletId)
                ? await SweepDigiIdAddressAsync(hd, store, walletId, item, plan.FeeRateSatPerVByte)
                : await FailDigiIdItemAsync(store, walletId, item, "wallet locked");

            results.Add(record);
            progress?.Report(record);
        }

        return results;
    }

    private bool IsStillUnlocked(string walletId) => _activeWalletId == walletId && _sessions.ContainsKey(walletId);

    /// <summary>Records an address as not swept, with a reason, unless a sweep of it is already in flight (then that record is returned).</summary>
    private static async Task<DigiIdSweepRecord> FailDigiIdItemAsync(DigiIdSweepStore store, string walletId, DigiIdSweepPlanItem item, string reason)
    {
        var (created, record) = await store.TryBeginAsync(walletId, NewDigiIdSweepRecord(item));
        return created ? await store.TransitionAsync(walletId, record.Id, DigiIdSweepStatus.Failed, failureReason: reason) : record;
    }

    private static DigiIdSweepRecord NewDigiIdSweepRecord(DigiIdSweepPlanItem item)
    {
        var now = DateTime.UtcNow;
        return new DigiIdSweepRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Domain = item.Domain,
            SourceAddress = item.SourceAddress,
            DestinationAddress = item.DestinationAddress,
            AmountSatoshis = item.AmountSatoshis,
            FeeSatoshis = item.EstimatedFeeSatoshis,
            Status = DigiIdSweepStatus.Sending,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
    }

    private async Task<DigiIdSweepRecord> SweepDigiIdAddressAsync(
        HdKeyDerivation hd, DigiIdSweepStore store, string walletId, DigiIdSweepPlanItem item, int feeRateSatPerVByte)
    {
        // The record is written before anything is sent, under the store's lock: a second sweep of this address cannot start.
        var (created, record) = await store.TryBeginAsync(walletId, NewDigiIdSweepRecord(item));
        if (!created) return record;

        lock (_digiIdRunLock) _digiIdSweepsRunning.Add(item.SourceAddress);
        try
        {
            return await SendDigiIdSweepAsync(hd, store, walletId, record, item, feeRateSatPerVByte);
        }
        finally
        {
            lock (_digiIdRunLock) _digiIdSweepsRunning.Remove(item.SourceAddress);
        }
    }

    private async Task<DigiIdSweepRecord> SendDigiIdSweepAsync(
        HdKeyDerivation hd, DigiIdSweepStore store, string walletId, DigiIdSweepRecord record, DigiIdSweepPlanItem item, int feeRateSatPerVByte)
    {
        Task<DigiIdSweepRecord> Fail(string reason) =>
            store.TransitionAsync(walletId, record.Id, DigiIdSweepStatus.Failed, failureReason: reason);

        var broadcastTried = false;
        try
        {
            // The key is the one this wallet derives for the site, and it must be the address that was planned: a plan that
            // names any other address is not signed for.
            var domain = DigiIdDomain.Normalize(item.Domain);
            if (domain is null) return await Fail("Not a valid site name.");

            var key = hd.DeriveDigiIdKey(DigiIdService.DeriveSiteIndex(domain)).PrivateKey;
            if (!string.Equals(DigiIdService.GetAddress(key), item.SourceAddress, StringComparison.Ordinal))
                return await Fail("This is not the Digi-ID address of that site." + DigiIdFundsStayMessage);

            var source = BitcoinAddress.Create(item.SourceAddress, DigiByteNetwork.Mainnet);

            // Look again now: the plan may be minutes old. Only confirmed plain outputs are spent.
            var plain = SplitDigiIdOutputs(await _blockchain.GetVerifiedUtxosAsync(item.SourceAddress), source.ScriptPubKey.ToHex());
            if (plain.ConfirmedUtxos.Count == 0) return await Fail("Nothing confirmed was left to sweep.");

            var network = CurrentNetwork;
            var coins = plain.ConfirmedUtxos.Select(u => new DigiByte.Crypto.Transactions.Utxo
            {
                TransactionId = uint256.Parse(u.TxId),
                OutputIndex = u.OutputIndex,
                Amount = Money.Satoshis(u.AmountSatoshis),
                ScriptPubKey = source.ScriptPubKey,
                PrivateKey = key,
                Confirmations = u.Confirmations,
            }).ToList();

            var destination = BitcoinAddress.Create(item.DestinationAddress, network);
            var feeRate = new FeeRate(Money.Satoshis(DigiIdSweepPlanner.EffectiveFeeRate(feeRateSatPerVByte) * 1000L));
            var tx = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network).BuildSendAllTransaction(coins, destination, feeRate);

            var total = plain.Confirmed;
            var fee = tx.GetFee(coins.Select(c => c.ToCoin()).ToArray())?.Satoshi ?? 0;
            var net = total - fee;
            if (net < DigiIdSweepPlanner.MinNetSatoshis) return await Fail("Too small to sweep: after the network fee almost nothing would arrive." + DigiIdFundsStayMessage);

            broadcastTried = true;
            var txId = await BroadcastAsync(tx.ToBytes());

            DigiIdSweepRecord pending;
            try
            {
                pending = await store.TransitionAsync(walletId, record.Id, DigiIdSweepStatus.Pending, txId: txId, feeSatoshis: fee, amountSatoshis: total);
            }
            catch
            {
                // It was sent but could not be saved. Do not call it failed: the funds are moving. The record stays as it is, and
                // a later refresh leaves it for the user to check in the transaction history.
                return record;
            }

            try { await _txTracker.RecordSendAsync(txId, destination.ToString(), net, fee, "Digi-ID recovery sweep"); }
            catch { /* the history entry is a convenience; the sweep is sent and recorded */ }

            return pending;
        }
        catch (Exception)
        {
            // The reason is fixed on purpose: a raw exception could carry a server's reply or part of a transaction.
            return await Fail(broadcastTried
                ? "The network did not accept the transaction." + DigiIdFundsStayMessage
                : "Could not send." + DigiIdFundsStayMessage);
        }
    }

    /// <summary>
    /// The saved sweeps of the active wallet, newest first, read from storage only: no network lookup, so it is instant. Show
    /// these at once when the section opens, then bring them up to date with <see cref="RefreshDigiIdSweepsAsync"/>. Needs no
    /// key. Throws <see cref="InvalidOperationException"/> if the wallet is locked.
    /// </summary>
    public async Task<IReadOnlyList<DigiIdSweepRecord>> GetDigiIdSweepsAsync()
    {
        EnsureUnlocked();
        var store = _digiIdSweeps ?? throw new InvalidOperationException("Digi-ID recovery is not configured.");
        return await store.GetAllAsync(ActiveSession!.Info.Id);
    }

    /// <summary>
    /// Brings the saved sweeps of the active wallet up to date and returns them, newest first. A Pending sweep is looked up on
    /// its own: Confirmed at one confirmation, Failed if the network has not known it for <see cref="DigiIdSweepDropGrace"/>.
    /// A lookup that fails leaves the record as it was. A sweep that was interrupted before it was sent, whose funds are still
    /// on the address, becomes Failed so the address is offered again. Needs no key. Throws
    /// <see cref="InvalidOperationException"/> if the wallet is locked.
    /// </summary>
    public async Task<IReadOnlyList<DigiIdSweepRecord>> RefreshDigiIdSweepsAsync(CancellationToken ct = default)
    {
        EnsureUnlocked();
        var store = _digiIdSweeps ?? throw new InvalidOperationException("Digi-ID recovery is not configured.");
        var walletId = ActiveSession!.Info.Id;

        foreach (var record in (await store.GetAllAsync(walletId)).Where(r => r.IsInFlight))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (record.Status == DigiIdSweepStatus.Pending && !string.IsNullOrEmpty(record.TxId))
                    await RefreshPendingDigiIdSweepAsync(store, walletId, record, ct);
                else if (record.Status == DigiIdSweepStatus.Sending && record.TxId is null)
                    await RefreshInterruptedDigiIdSweepAsync(store, walletId, record, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                // The record changed under us (for example finished in another call). The next read shows how it ended.
            }
        }

        return await store.GetAllAsync(walletId);
    }

    private async Task RefreshPendingDigiIdSweepAsync(DigiIdSweepStore store, string walletId, DigiIdSweepRecord record, CancellationToken ct)
    {
        TransactionInfo? tx;
        try
        {
            tx = await _blockchain.GetTransactionAsync(record.TxId!);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return; // Could not look it up: leave it Pending.
        }

        if (tx is { Confirmations: >= 1 })
        {
            await store.TransitionAsync(walletId, record.Id, DigiIdSweepStatus.Confirmed);
        }
        else if (tx is null && DateTime.UtcNow - record.UpdatedUtc >= DigiIdSweepDropGrace)
        {
            await store.TransitionAsync(walletId, record.Id, DigiIdSweepStatus.Failed,
                failureReason: "The network no longer knows this transaction. Check your transaction history before sweeping again.");
        }
    }

    private async Task RefreshInterruptedDigiIdSweepAsync(DigiIdSweepStore store, string walletId, DigiIdSweepRecord record, CancellationToken ct)
    {
        // A sweep that is being sent right now by this wallet is not interrupted.
        lock (_digiIdRunLock)
        {
            if (_digiIdSweepsRunning.Contains(record.SourceAddress)) return;
        }

        try
        {
            var source = BitcoinAddress.Create(record.SourceAddress, DigiByteNetwork.Mainnet);
            var plain = SplitDigiIdOutputs(await _blockchain.GetVerifiedUtxosAsync(record.SourceAddress), source.ScriptPubKey.ToHex());

            // Funds still there: it never went out, so offer the address again. Funds gone: it may have been sent, so leave
            // the record and let the user check the transaction history.
            if (plain.Confirmed + plain.Pending > 0)
                await store.TransitionAsync(walletId, record.Id, DigiIdSweepStatus.Failed,
                    failureReason: "Interrupted before it was sent." + DigiIdFundsStayMessage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // Could not look it up: leave it as it is.
        }
    }

    private void EnsureUnlocked()
    {
        if (ActiveSession == null)
            throw new InvalidOperationException("Wallet is locked. Unlock it first.");
    }

    /// <summary>
    /// Returns all wallets stored in IndexedDB.
    /// </summary>
    public async Task<List<WalletInfo>> GetAllWalletsAsync()
    {
        return await _walletStore.GetAllWalletInfosAsync();
    }

    /// <summary>
    /// Switch to a different wallet that is already unlocked in this session.
    /// If the wallet is not unlocked, throws — caller should redirect to unlock page.
    /// </summary>
    public async Task SwitchWalletAsync(string walletId)
    {
        if (!_sessions.ContainsKey(walletId))
            throw new InvalidOperationException("Wallet is not unlocked. Enter PIN to unlock.");

        if (_activeWalletId is { } previous && previous != walletId)
            _historySync?.CancelFullResync(previous);
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);
        await _walletStore.SetActiveWalletIdAsync(walletId);
    }

    /// <summary>
    /// Returns true if the given wallet has been unlocked during this session.
    /// </summary>
    public bool IsWalletUnlocked(string walletId)
    {
        return _sessions.ContainsKey(walletId);
    }

    /// <summary>Alias for backward compatibility.</summary>
    public bool IsWalletUnlockedInSession(string walletId) => IsWalletUnlocked(walletId);

    /// <summary>
    /// Gets the first receiving address of another unlocked wallet (without switching).
    /// Used for auto-generating wallet contacts.
    /// </summary>
    public string? GetReceivingAddressForWallet(string walletId)
    {
        if (!_sessions.TryGetValue(walletId, out var session)) return null;
        var net = session.Info.WifNetwork switch
        {
            "testnet" => DigiByte.Crypto.Networks.DigiByteNetwork.Testnet,
            "regtest" => DigiByte.Crypto.Networks.DigiByteNetwork.Regtest,
            _ => DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet,
        };
        if (session.SingleKey != null)
            return session.SingleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, net).ToString();
        if (session.Hd != null)
            return session.Hd.DeriveReceivingAddress(0, ScriptPubKeyType.Segwit).ToString();
        return null;
    }

    /// <summary>
    /// Rename a wallet and persist the change.
    /// </summary>
    public async Task RenameWalletAsync(string walletId, string newName)
    {
        var info = await GetWalletAsync(walletId);
        if (info == null) return;

        info.Name = newName;
        await SaveWalletRecordAsync(walletId, info);

        // Update in-memory session if unlocked
        if (_sessions.TryGetValue(walletId, out var session))
            session.Info.Name = newName;
    }

    public async Task ChangeWalletColorAsync(string walletId, string color)
    {
        var info = await GetWalletAsync(walletId);
        if (info == null) return;

        info.Color = color;
        await SaveWalletRecordAsync(walletId, info);

        if (_sessions.TryGetValue(walletId, out var session))
            session.Info.Color = color;
    }

    /// <summary>
    /// Change the wallet card's surface finish and persist it. An unknown finish falls back to the default.
    /// </summary>
    public async Task ChangeWalletTextureAsync(string walletId, string texture)
    {
        var info = await GetWalletAsync(walletId);
        if (info == null) return;

        texture = WalletInfo.NormalizeTexture(texture);
        info.Texture = texture;
        await SaveWalletRecordAsync(walletId, info);

        if (_sessions.TryGetValue(walletId, out var session))
            session.Info.Texture = texture;
    }

    /// <summary>
    /// Strips the 6-char bech32 checksum from DigiByte SegWit addresses so that
    /// addresses with different checksum variants (bech32 vs bech32m) compare equal
    /// when they encode the same witness program.
    /// Legacy/P2SH addresses are returned as-is (lowercased).
    /// </summary>
    private static string NormalizeAddress(string address) => AddressKey.Normalize(address);

    /// <summary>
    /// Extract a UTF-8 memo from the first OP_RETURN output, if any.
    /// </summary>
    internal static string? ExtractOpReturnMemo(List<TxOutput> outputs)
    {
        // A DigiDollar OP_RETURN starts with the marker "DD"; it is data for the wallet, never a text note.
        if (DigiDollarTxSummary.TryRead(outputs) is not null)
            return null;

        var hex = outputs.FirstOrDefault(o =>
            o.ScriptHex != null && o.ScriptHex.StartsWith("6a", StringComparison.OrdinalIgnoreCase))?.ScriptHex;
        if (hex is not { Length: > 4 }) return null;

        try
        {
            var bytes = Convert.FromHexString(hex[2..]); // skip 0x6a (OP_RETURN)
            if (bytes.Length == 0) return null;

            byte[] data;
            if (bytes[0] < 0x4c) // direct push
            {
                int len = bytes[0];
                if (bytes.Length < 1 + len) return null;
                data = bytes[1..(1 + len)];
            }
            else if (bytes[0] == 0x4c && bytes.Length > 2) // OP_PUSHDATA1
            {
                int len = bytes[1];
                if (bytes.Length < 2 + len) return null;
                data = bytes[2..(2 + len)];
            }
            else return null;

            var text = System.Text.Encoding.UTF8.GetString(data);
            return text.All(c => !char.IsControl(c) || c == '\n' || c == '\r') ? text : null;
        }
        catch { return null; }
    }
}

public enum ChangeCredentialOutcome
{
    Changed,
    WrongCredential,
    WeakPassphrase,
    Failed,
}
