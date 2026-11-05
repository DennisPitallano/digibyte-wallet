using System.Text.Json;
using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Services;

public class WalletService : IWalletService, IDdSessionProvider, IDdFeeSource
{
    private readonly IKeyStore _keyStore;
    private readonly WalletKeyStore _walletStore;
    private readonly ICryptoService _crypto;
    private readonly IBlockchainService _blockchain;
    private readonly TransactionTracker _txTracker;

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
    }

    private readonly Dictionary<string, UnlockedWalletSession> _sessions = new();
    private string? _activeWalletId;
    private string _networkMode = "testnet";

    // Address derivation cache — avoid repeated elliptic curve math. Each cache remembers
    // which wallet and how many addresses of each kind it was built for.
    private (string? WalletId, int ReceiveCount, int LegacyReceiveCount, int ChangeCount)? _cachedAddressesFor;
    private List<string>? _cachedAddresses;
    private (string? WalletId, int ReceiveCount, int LegacyReceiveCount, int ChangeCount)? _cachedKeyMapFor;
    private Dictionary<string, ExtKey>? _cachedAddressKeyMap;
    private const int UnscannedChangeLimit = 100;
    private string? _unscannedChangeWalletId;
    private HashSet<string>? _unscannedChangeAddresses;

    private const int MaxWallets = 10;

    /// <summary>Change indexes always scanned. Only these are scanned in Legacy format too: the wallet's own change is SegWit.</summary>
    private const int MinScannedChange = 10;

    /// <summary>Consecutive unused change addresses that end the search for used ones (the usual BIP44 gap limit).</summary>
    private const int ChangeGapLimit = 20;

    /// <summary>Receive indexes always scanned, in both formats. Past these, Legacy is scanned only as far as the address on show: see <see cref="ScannedLegacyReceiveCount"/>.</summary>
    private const int MinScannedReceive = 20;

    /// <summary>
    /// Consecutive unused receive addresses that end the search for used ones. It is also the most unused addresses
    /// the wallet will hand out in a row, so that a wallet restored from its seed can find every address that was paid.
    /// </summary>
    public const int ReceiveGapLimit = 20;

    /// <summary>How far past the last used address <see cref="ScanForMissingReceiveFundsAsync"/> looks.</summary>
    public const int DeepScanGapLimit = 500;

    /// <summary>
    /// How far one automatic search may go past its starting index. A source that shows history for every address (a mock, a
    /// faulty explorer) would otherwise keep it going forever. Older wallets with a longer unbroken run can use the deep scan.
    /// </summary>
    private const int MaxAddressSearch = 500;

    /// <summary>The same limit for <see cref="ScanForMissingReceiveFundsAsync"/>, which the user starts and can wait for.</summary>
    private const int MaxDeepScanSearch = 2000;

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

    public WalletService(WalletKeyStore walletStore, ICryptoService crypto, IBlockchainService blockchain, TransactionTracker txTracker)
    {
        _keyStore = walletStore;
        _walletStore = walletStore;
        _crypto = crypto;
        _blockchain = blockchain;
        _txTracker = txTracker;
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

    public async Task<WalletInfo> CreateWalletAsync(string name, string mnemonic, string pin, string? color = null)
    {
        var count = await _walletStore.GetWalletCountAsync();
        if (count >= MaxWallets)
            throw new InvalidOperationException($"Maximum of {MaxWallets} wallets reached. Delete an existing wallet first.");

        var walletId = Guid.NewGuid().ToString("N");

        var encrypted = await _crypto.EncryptAsync(
            System.Text.Encoding.UTF8.GetBytes(mnemonic), pin);
        await _keyStore.StoreSeedAsync(walletId, encrypted);

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
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
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);

        return wallet;
    }

    /// <summary>
    /// Create a wallet from a single WIF private key (paper wallet / legacy import).
    /// </summary>
    public async Task<WalletInfo> CreateWalletFromWifAsync(string name, string wif, string pin, string? color = null)
    {
        var count = await _walletStore.GetWalletCountAsync();
        if (count >= MaxWallets)
            throw new InvalidOperationException($"Maximum of {MaxWallets} wallets reached. Delete an existing wallet first.");

        var walletId = Guid.NewGuid().ToString("N");

        // Encrypt the WIF with PIN (same pattern as mnemonic)
        var encrypted = await _crypto.EncryptAsync(
            System.Text.Encoding.UTF8.GetBytes(wif), pin);
        await _keyStore.StoreSeedAsync(walletId, encrypted);

        // Auto-detect network from the key prefix
        var detectedNetwork = PrivateKeyImporter.DetectNetwork(wif);
        if (detectedNetwork == null)
            throw new InvalidOperationException("Could not detect network from private key.");

        var networkName = detectedNetwork == DigiByteNetwork.Mainnet ? "mainnet"
            : detectedNetwork == DigiByteNetwork.Testnet ? "testnet" : "regtest";

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            WalletType = "privatekey",
            WifNetwork = networkName,
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
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);

        return wallet;
    }

    /// <summary>
    /// Create a watch-only wallet from an extended public key (xpub).
    /// Can view balances and receive, but cannot sign or send transactions.
    /// </summary>
    public async Task<WalletInfo> CreateWatchOnlyWalletAsync(string name, string xpub, string pin, string? color = null)
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

        // Encrypt the xpub with PIN (same pattern as mnemonic/WIF)
        var encrypted = await _crypto.EncryptAsync(
            System.Text.Encoding.UTF8.GetBytes(xpub.Trim()), pin);
        await _keyStore.StoreSeedAsync(walletId, encrypted);

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            WalletType = "xpub",
            WifNetwork = networkName,
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
        var encryptedSeed = await _keyStore.GetSeedAsync(walletId);
        if (encryptedSeed == null)
            return false;

        var decrypted = await _crypto.DecryptAsync(encryptedSeed, pin);
        if (decrypted == null)
            return false;

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
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);
        await _walletStore.SetActiveWalletIdAsync(walletId);
        return true;
    }

    public async Task<string[]?> GetSeedPhraseAsync(string walletId, string pin)
    {
        var encryptedSeed = await _keyStore.GetSeedAsync(walletId);
        if (encryptedSeed == null)
            return null;

        var decrypted = await _crypto.DecryptAsync(encryptedSeed, pin);
        if (decrypted == null)
            return null;

        var mnemonicWords = System.Text.Encoding.UTF8.GetString(decrypted);
        return mnemonicWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Decrypt the raw seed bytes for a wallet. Used by biometric enrollment.
    /// </summary>
    public async Task<byte[]?> GetDecryptedSeedAsync(string walletId, string pin)
    {
        var encryptedSeed = await _keyStore.GetSeedAsync(walletId);
        if (encryptedSeed == null) return null;
        return await _crypto.DecryptAsync(encryptedSeed, pin);
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
        _activeWalletId = walletId;
        _txTracker.SetActiveWallet(walletId);
        await _walletStore.SetActiveWalletIdAsync(walletId);
        return true;
    }

    public Task LockWalletAsync()
    {
        _sessions.Clear();
        _cachedAddressKeyMap = null;
        _activeWalletId = null;
        return Task.CompletedTask;
    }

    public async Task<WalletBalance> GetBalanceAsync()
    {
        EnsureUnlocked();
        await WaitForAddressDiscoveryAsync(AddressDiscoveryWait);

        var addresses = GetAllAddresses();
        var totalSatoshis = await _blockchain.GetBalanceAsync(addresses);

        return new WalletBalance
        {
            ConfirmedSatoshis = totalSatoshis,
            FiatCurrency = ActiveWallet!.FiatCurrency,
        };
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
    /// Get all addresses for this wallet. For WIF wallets, returns both Legacy and SegWit.
    /// Results are cached per wallet — HD derivation (elliptic curve math) is only done once.
    /// </summary>
    public List<string> GetAllAddresses()
    {
        EnsureUnlocked();

        // Return cached if nothing it depends on has changed
        var cacheKey = (_activeWalletId, ScannedReceiveCount, ScannedLegacyReceiveCount, ScannedChangeCount);
        if (_cachedAddresses != null && _cachedAddressesFor == cacheKey)
            return _cachedAddresses;

        List<string> addresses;
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
            for (int i = 0; i < ScannedReceiveCount; i++)
            {
                addresses.Add(_hd!.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString());
                if (i < ScannedLegacyReceiveCount)
                    addresses.Add(_hd.DeriveReceivingAddress(i, ScriptPubKeyType.Legacy).ToString());
            }
            for (int i = 0; i < ScannedChangeCount; i++)
            {
                addresses.Add(_hd!.DeriveChangeAddress(i, ScriptPubKeyType.Segwit).ToString());
                if (i < MinScannedChange)
                    addresses.Add(_hd.DeriveChangeAddress(i, ScriptPubKeyType.Legacy).ToString());
            }
        }

        _cachedAddresses = addresses;
        _cachedAddressesFor = cacheKey;
        return addresses;
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

        var cacheKey = (_activeWalletId, ScannedReceiveCount, ScannedLegacyReceiveCount, ScannedChangeCount);
        if (_cachedAddressKeyMap != null && _cachedKeyMapFor == cacheKey)
            return _cachedAddressKeyMap;

        var map = new Dictionary<string, ExtKey>();
        for (int i = 0; i < ScannedReceiveCount; i++)
        {
            var key = _hd!.DeriveReceivingKey(i);
            map[_hd.GetAddress(key, ScriptPubKeyType.Segwit).ToString()] = key;
            if (i < ScannedLegacyReceiveCount)
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
    /// How many receive indexes to scan. Always reaches the address on show and every address the chain has shown as used.
    /// </summary>
    private int ScannedReceiveCount => ActiveWallet is { } info ? ScannedReceiveCountOf(info) : MinScannedReceive;

    private static int ScannedReceiveCountOf(WalletInfo info) =>
        Math.Max(MinScannedReceive, Math.Max(info.NextReceivingIndex + 1, info.UsedReceivingCount));

    /// <summary>
    /// How many receive indexes to scan in Legacy format. The Receive page can show the Legacy form of the address on show, so
    /// the scan always reaches it. Indexes that were never on show can't have been handed out in Legacy format.
    /// </summary>
    private int ScannedLegacyReceiveCount => Math.Max(MinScannedReceive, (ActiveWallet?.NextReceivingIndex ?? 0) + 1);

    /// <summary>
    /// Moves to a new receive address, saved before it is returned so that it stays scanned after a reload. Skips addresses
    /// the chain shows as used. Throws <see cref="InvalidOperationException"/>, with a message for the user, if it cannot be
    /// saved or if <see cref="ReceiveGapLimit"/> unused addresses are already out.
    /// </summary>
    public async Task<(int Index, string Address)> GenerateNewReceivingAddressAsync()
    {
        EnsureUnlocked();
        var session = ActiveSession!;
        if (session.Hd is null)
            throw new InvalidOperationException("This wallet has a single address.");
        var info = session.Info;

        await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
        if (NextNewReceivingIndex(info) >= info.UsedReceivingCount + ReceiveGapLimit)
        {
            // Addresses handed out in this session may have been paid since the last lookup.
            session.AddressDiscovery = null;
            session.AddressDiscoveryRetryAfter = default;
            await WaitForAddressDiscoveryAsync(AddressDiscoverySendWait);
            if (NextNewReceivingIndex(info) >= info.UsedReceivingCount + ReceiveGapLimit)
                throw new InvalidOperationException(
                    $"You have {ReceiveGapLimit} unused addresses in a row. Use one of them first: restoring from your seed " +
                    $"only looks {ReceiveGapLimit} addresses past the last one used.");
        }

        var previous = info.NextReceivingIndex;
        var index = NextNewReceivingIndex(info);
        info.NextReceivingIndex = index;
        try
        {
            await PersistAddressIndexesAsync(info);
        }
        catch (Exception ex)
        {
            // Nothing has been shown yet, so fail: an address lost on reload could be paid and then never scanned.
            info.NextReceivingIndex = previous;
            throw new InvalidOperationException("Could not save the new address. Your current address is still good to use.", ex);
        }

        return (index, session.Hd.DeriveReceivingAddress(index, ScriptPubKeyType.Segwit).ToString());
    }

    private static int NextNewReceivingIndex(WalletInfo info) =>
        Math.Max(info.NextReceivingIndex + 1, info.UsedReceivingCount);

    /// <summary>
    /// Looks much further than the automatic search for paid receive addresses, for wallets that handed out many addresses
    /// before they were tracked. Whatever it finds joins the normal scan, so the funds appear in the balance and can be spent
    /// with nothing to sweep. Throws if the chain cannot be read: a failed search must not look like an empty one.
    /// </summary>
    public async Task<ReceiveRecoveryResult> ScanForMissingReceiveFundsAsync(int gapLimit = DeepScanGapLimit, IProgress<int>? progress = null)
    {
        EnsureUnlocked();
        var session = ActiveSession!;
        var hd = session.Hd;
        if (hd is null) return new ReceiveRecoveryResult(0, 0);
        var info = session.Info;

        var scannedBefore = ScannedReceiveCountOf(info);
        var used = await FindUsedCountAsync(
            i => hd.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString(),
            info.UsedReceivingCount, gapLimit, scannedBefore + gapLimit, Math.Max(MaxDeepScanSearch, gapLimit), progress)
            ?? throw new InvalidOperationException("The block explorer is reporting history for every address, so its answers can't be trusted.");

        if (used > info.UsedReceivingCount)
        {
            info.UsedReceivingCount = used;
            await PersistAddressIndexesAsync(info);
        }

        var added = Enumerable.Range(scannedBefore, Math.Max(0, used - scannedBefore))
            .Select(i => hd.DeriveReceivingAddress(i, ScriptPubKeyType.Segwit).ToString())
            .ToList();
        return new ReceiveRecoveryResult(added.Count, added.Count > 0 ? await _blockchain.GetBalanceAsync(added) : 0);
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
            session.AddressDiscovery = RunAddressDiscoveryAsync(session);
        }

        await Task.WhenAny(session.AddressDiscovery, Task.Delay(limit));
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
            info.UsedReceivingCount, ReceiveGapLimit, ScannedReceiveCountOf(info) + ReceiveGapLimit, MaxAddressSearch);

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
                    return (Index: index, Used: await _blockchain.HasAddressHistoryAsync(addressAt(index)));
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

        stored.NextChangeIndex = info.NextChangeIndex;
        stored.NextReceivingIndex = info.NextReceivingIndex;
        stored.UsedReceivingCount = info.UsedReceivingCount;
        await _walletStore.SaveWalletInfoAsync(info.Id, JsonSerializer.Serialize(stored));
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
            var semaphore = new SemaphoreSlim(10);
            var utxoTasks = addressKeyMap.Select(async kvp =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var utxos = await _blockchain.GetUtxosAsync(kvp.Key);
                    return (Address: kvp.Key, Key: kvp.Value, Utxos: utxos);
                }
                finally { semaphore.Release(); }
            });
            var utxoResults = await Task.WhenAll(utxoTasks);
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
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);

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
            var batchSemaphore = new SemaphoreSlim(10);
            var batchUtxoTasks = addressKeyMap.Select(async kvp =>
            {
                await batchSemaphore.WaitAsync();
                try
                {
                    var utxos = await _blockchain.GetUtxosAsync(kvp.Key);
                    return (Address: kvp.Key, Key: kvp.Value, Utxos: utxos);
                }
                finally { batchSemaphore.Release(); }
            });
            var batchUtxoResults = await Task.WhenAll(batchUtxoTasks);
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
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);

        // History is keyed by TxId, so the whole batch is one record: the full amount, and the
        // first recipient as counterparty (the same one an explorer-built record would show).
        var feePaid = tx.GetFee(utxosToUse.Select(u => u.ToCoin()).ToArray());
        await _txTracker.RecordSendAsync(txId, recipients[0].Address, totalSatoshis, feePaid?.Satoshi ?? 0, memo);

        await AdvanceChangeIndexAsync();

        return txId;
    }

    public async Task<List<TransactionRecord>> GetTransactionHistoryAsync(int skip = 0, int take = 50)
    {
        EnsureUnlocked();
        await WaitForAddressDiscoveryAsync(AddressDiscoveryWait);

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
                var ourNormalized = new HashSet<string>(rawAddresses.Select(NormalizeAddress));
                bool isOurs(string? addr) => addr != null && ourNormalized.Contains(NormalizeAddress(addr));

                var allTxs = new Dictionary<string, TransactionInfo>();
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

                var explorerRecords = allTxs.Values.Select(tx =>
                {
                    var isSent = tx.Inputs.Any(i => isOurs(i.Address));
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
                        CounterpartyAddress = isSent
                            ? tx.Outputs.FirstOrDefault(o => !isOurs(o.Address))?.Address
                            : tx.Inputs.FirstOrDefault(i => !isOurs(i.Address))?.Address,
                        Memo = ExtractOpReturnMemo(tx.Outputs),
                    };
                }).ToList();

                // Merge: explorer txs + local txs (local wins on duplicates for freshness)
                var merged = new Dictionary<string, TransactionRecord>();
                foreach (var tx in explorerRecords) merged.TryAdd(tx.TxId, tx);
                foreach (var tx in localTxs) merged[tx.TxId] = tx; // local overwrites
                var localToCheck = localTxs.ToList();
                localTxs = merged.Values.ToList();

                await RepairUnderRecordedBatchesAsync(localToCheck, allTxs, isOurs);
            }
            catch (Exception ex) { Console.WriteLine($"[TxHistory] Explorer failed: {ex.Message}"); }
        }

        // Update confirmation counts in background
        _ = _txTracker.UpdateConfirmationsAsync(_blockchain);

        return localTxs
            .OrderByDescending(t => t.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToList();
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
            var semaphore = new SemaphoreSlim(10);
            var utxoTasks = addressKeyMap.Select(async kvp =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var utxos = await _blockchain.GetUtxosAsync(kvp.Key);
                    return (Address: kvp.Key, Key: kvp.Value, Utxos: utxos);
                }
                finally { semaphore.Release(); }
            });
            var utxoResults = await Task.WhenAll(utxoTasks);
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
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);

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
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);

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
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);

        var feePaid = tx.GetFee(utxos.Select(u => u.ToCoin()).ToArray());
        var amountAfterFee = totalSatoshis - (feePaid?.Satoshi ?? 0);
        await _txTracker.RecordSendAsync(txId, destination.ToString(), amountAfterFee,
            feePaid?.Satoshi ?? 0, "Recovery sweep");

        return txId;
    }

    /// <summary>
    /// Reset PIN by verifying the seed phrase and re-encrypting with a new PIN.
    /// Works for HD wallets only (mnemonic-based).
    /// </summary>
    public async Task<bool> ResetPinAsync(string walletId, string seedPhrase, string newPin)
    {
        // Load wallet info to check type
        var wallet = await GetWalletAsync(walletId);
        if (wallet == null) return false;

        if (wallet.WalletType == "privatekey")
        {
            // For WIF wallets, verify the WIF matches
            // We can't verify without the old PIN, so just re-encrypt
            // The user provides their WIF key as "seed phrase"
            if (!PrivateKeyImporter.IsValidWif(seedPhrase.Trim()))
                return false;

            var encrypted = await _crypto.EncryptAsync(
                System.Text.Encoding.UTF8.GetBytes(seedPhrase.Trim()), newPin);
            await _keyStore.StoreSeedAsync(walletId, encrypted);
            return true;
        }
        else if (wallet.WalletType == "xpub")
        {
            // Watch-only wallet — verify xpub is valid
            if (HdKeyDerivation.ParseXpub(seedPhrase.Trim()) == null)
                return false;

            var encrypted = await _crypto.EncryptAsync(
                System.Text.Encoding.UTF8.GetBytes(seedPhrase.Trim()), newPin);
            await _keyStore.StoreSeedAsync(walletId, encrypted);
            return true;
        }
        else
        {
            // HD wallet — verify mnemonic is valid
            var cleaned = string.Join(' ', seedPhrase.Trim().ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (!MnemonicGenerator.IsValid(cleaned))
                return false;

            // Re-encrypt the mnemonic with the new PIN
            var encrypted = await _crypto.EncryptAsync(
                System.Text.Encoding.UTF8.GetBytes(cleaned), newPin);
            await _keyStore.StoreSeedAsync(walletId, encrypted);
            return true;
        }
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
        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(info));

        // Update in-memory session if unlocked
        if (_sessions.TryGetValue(walletId, out var session))
            session.Info.Name = newName;
    }

    public async Task ChangeWalletColorAsync(string walletId, string color)
    {
        var info = await GetWalletAsync(walletId);
        if (info == null) return;

        info.Color = color;
        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(info));

        if (_sessions.TryGetValue(walletId, out var session))
            session.Info.Color = color;
    }

    /// <summary>
    /// Strips the 6-char bech32 checksum from DigiByte SegWit addresses so that
    /// addresses with different checksum variants (bech32 vs bech32m) compare equal
    /// when they encode the same witness program.
    /// Legacy/P2SH addresses are returned as-is (lowercased).
    /// </summary>
    private static string NormalizeAddress(string address)
    {
        if (address != null && address.Length > 10 &&
            (address.StartsWith("dgb1", StringComparison.OrdinalIgnoreCase) ||
             address.StartsWith("dgbt1", StringComparison.OrdinalIgnoreCase)))
            return address[..^6].ToLowerInvariant();
        return address?.ToLowerInvariant() ?? "";
    }

    /// <summary>
    /// Extract a UTF-8 memo from the first OP_RETURN output, if any.
    /// </summary>
    private static string? ExtractOpReturnMemo(List<TxOutput> outputs)
    {
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
