using System.Text.Json;
using DigiByte.Crypto.DigiId;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Services;

public class WalletService : IWalletService
{
    private readonly IKeyStore _keyStore;
    private readonly WalletKeyStore _walletStore;
    private readonly ICryptoService _crypto;
    private readonly IBlockchainService _blockchain;
    private readonly TransactionTracker _txTracker;

    private HdKeyDerivation? _hd;
    private Key? _singleKey; // For WIF-imported wallets (no HD derivation)
    private WalletInfo? _activeWallet;
    private string _networkMode = "testnet";

    public bool IsUnlocked => _hd != null || _singleKey != null;
    public WalletInfo? ActiveWallet => _activeWallet;

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
    /// </summary>
    private Network EffectiveNetwork =>
        _activeWallet?.WalletType == "privatekey" && _activeWallet.WifNetwork != null
            ? _activeWallet.WifNetwork switch
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

    public async Task<WalletInfo> CreateWalletAsync(string name, string mnemonic, string pin)
    {
        var walletId = Guid.NewGuid().ToString("N");

        var encrypted = await _crypto.EncryptAsync(
            System.Text.Encoding.UTF8.GetBytes(mnemonic), pin);
        await _keyStore.StoreSeedAsync(walletId, encrypted);

        var wallet = new WalletInfo
        {
            Id = walletId,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(wallet));
        await _walletStore.SetActiveWalletIdAsync(walletId);

        var parsedMnemonic = MnemonicGenerator.FromWords(mnemonic);
        _hd = new HdKeyDerivation(parsedMnemonic, network: CurrentNetwork);
        _activeWallet = wallet;

        return wallet;
    }

    /// <summary>
    /// Create a wallet from a single WIF private key (paper wallet / legacy import).
    /// </summary>
    public async Task<WalletInfo> CreateWalletFromWifAsync(string name, string wif, string pin)
    {
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
        };

        await _walletStore.SaveWalletInfoAsync(walletId, JsonSerializer.Serialize(wallet));
        await _walletStore.SetActiveWalletIdAsync(walletId);

        _singleKey = PrivateKeyImporter.ParseWif(wif, detectedNetwork);
        _hd = null;
        _activeWallet = wallet;

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

        _activeWallet = await GetWalletAsync(walletId);
        var seedString = System.Text.Encoding.UTF8.GetString(decrypted);

        if (_activeWallet?.WalletType == "privatekey")
        {
            // WIF-imported wallet — single key, no HD derivation
            var wifNetwork = PrivateKeyImporter.DetectNetwork(seedString) ?? CurrentNetwork;
            _singleKey = PrivateKeyImporter.ParseWif(seedString, wifNetwork);
            _hd = null;
        }
        else
        {
            // Standard HD wallet from mnemonic
            var mnemonic = MnemonicGenerator.FromWords(seedString);
            _hd = new HdKeyDerivation(mnemonic, network: CurrentNetwork);
            _singleKey = null;
        }

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

    public Task LockWalletAsync()
    {
        _hd = null;
        _singleKey = null;
        _activeWallet = null;
        return Task.CompletedTask;
    }

    public async Task<WalletBalance> GetBalanceAsync()
    {
        EnsureUnlocked();

        var addresses = new List<string>();

        if (_singleKey != null)
        {
            // Single-key wallet — check both address formats on the detected network
            var net = EffectiveNetwork;
            addresses.Add(_singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, net).ToString());
            addresses.Add(_singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, net).ToString());
        }
        else
        {
            // HD wallet — scan 20 receiving + 10 change
            for (int i = 0; i < 20; i++)
            {
                var key = _hd!.DeriveReceivingKey(i);
                addresses.Add(_hd.GetAddress(key).ToString());
            }
            for (int i = 0; i < 10; i++)
            {
                var key = _hd!.DeriveChangeKey(i);
                addresses.Add(_hd.GetAddress(key).ToString());
            }
        }

        var totalSatoshis = await _blockchain.GetBalanceAsync(addresses);
        var dgbPrice = await _blockchain.GetDgbPriceAsync(_activeWallet!.FiatCurrency);

        var balance = new WalletBalance
        {
            ConfirmedSatoshis = totalSatoshis,
            FiatCurrency = _activeWallet.FiatCurrency,
        };
        balance.FiatValue = balance.ConfirmedDgb * dgbPrice;

        return balance;
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
        var index = _activeWallet!.NextReceivingIndex;
        var key = _hd!.DeriveReceivingKey(index);
        var address = _hd.GetAddress(key, format == "default" ? ScriptPubKeyType.Segwit : type);
        return Task.FromResult(address.ToString());
    }

    /// <summary>
    /// Get all addresses for this wallet. For WIF wallets, returns both Legacy and SegWit.
    /// </summary>
    public List<string> GetAllAddresses()
    {
        EnsureUnlocked();
        if (_singleKey != null)
        {
            var net = EffectiveNetwork;
            return [
                _singleKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, net).ToString(),
                _singleKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, net).ToString(),
            ];
        }
        var addresses = new List<string>();
        for (int i = 0; i < 20; i++)
            addresses.Add(_hd!.GetAddress(_hd.DeriveReceivingKey(i)).ToString());
        for (int i = 0; i < 10; i++)
            addresses.Add(_hd!.GetAddress(_hd.DeriveChangeKey(i)).ToString());
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
        var addresses = _hd!.DeriveReceivingAddresses(count, startIndex)
            .Select(a => (a.Index, a.Address.ToString()))
            .ToList();
        return Task.FromResult(addresses);
    }

    public async Task<string> SendAsync(string destinationAddress, decimal amountDgb, string? memo = null)
    {
        EnsureUnlocked();

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
                });
            }

            // Change goes back to the SegWit address of the same key
            changeAddress = segwitAddr;
        }
        else
        {
            // HD wallet — derive multiple receiving + change addresses
            var addressKeyMap = new Dictionary<string, ExtKey>();
            for (int i = 0; i < 20; i++)
            {
                var key = _hd!.DeriveReceivingKey(i);
                var addr = _hd.GetAddress(key).ToString();
                addressKeyMap[addr] = key;
            }
            for (int i = 0; i < 10; i++)
            {
                var key = _hd!.DeriveChangeKey(i);
                var addr = _hd.GetAddress(key).ToString();
                addressKeyMap[addr] = key;
            }

            var utxoInfos = await _blockchain.GetUtxosAsync(addressKeyMap.Keys);
            if (utxoInfos.Count == 0)
                throw new InvalidOperationException("No UTXOs available. Your wallet has no spendable funds.");

            availableUtxos = new List<DigiByte.Crypto.Transactions.Utxo>();
            foreach (var utxoInfo in utxoInfos)
            {
                ExtKey? matchedKey = null;
                foreach (var (addr, key) in addressKeyMap)
                {
                    var addrScript = BitcoinAddress.Create(addr, network).ScriptPubKey;
                    if (!string.IsNullOrEmpty(utxoInfo.ScriptPubKey))
                    {
                        var utxoScript = Script.FromHex(utxoInfo.ScriptPubKey);
                        if (addrScript == utxoScript)
                        {
                            matchedKey = key;
                            break;
                        }
                    }
                }

                if (matchedKey == null)
                    matchedKey = _hd!.DeriveReceivingKey(0);

                availableUtxos.Add(new DigiByte.Crypto.Transactions.Utxo
                {
                    TransactionId = uint256.Parse(utxoInfo.TxId),
                    OutputIndex = utxoInfo.OutputIndex,
                    Amount = Money.Satoshis(utxoInfo.AmountSatoshis),
                    ScriptPubKey = !string.IsNullOrEmpty(utxoInfo.ScriptPubKey)
                        ? Script.FromHex(utxoInfo.ScriptPubKey)
                        : matchedKey.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ScriptPubKey,
                    PrivateKey = matchedKey.PrivateKey,
                });
            }

            var changeKey = _hd!.DeriveChangeKey(_activeWallet!.NextChangeIndex);
            changeAddress = _hd.GetAddress(changeKey);
        }

        // Check we have enough funds
        var totalAvailable = availableUtxos.Sum(u => u.Amount.Satoshi);
        if (totalAvailable < amountSatoshis)
            throw new InvalidOperationException(
                $"Insufficient funds. Need {amountDgb:N8} DGB but only have {totalAvailable / 100_000_000m:N8} DGB.");

        // Build and sign the transaction
        var destination = BitcoinAddress.Create(destinationAddress, network);
        var feeRate = new FeeRate(Money.Satoshis(150_000));

        var txBuilder = new DigiByte.Crypto.Transactions.DigiByteTransactionBuilder(network);
        var tx = txBuilder.BuildSendTransaction(availableUtxos, destination, amount, changeAddress, feeRate);

        // Broadcast
        var rawTx = tx.ToBytes();
        var txId = await _blockchain.BroadcastTransactionAsync(rawTx);

        // Track the transaction locally
        var feePaid = tx.GetFee(availableUtxos.Select(u => u.ToCoin()).ToArray());
        await _txTracker.RecordSendAsync(txId, destinationAddress, amountSatoshis,
            feePaid?.Satoshi ?? 0);

        // Increment change index (HD wallets only)
        if (_hd != null && _activeWallet != null)
            _activeWallet.NextChangeIndex++;

        return txId;
    }

    public async Task<List<TransactionRecord>> GetTransactionHistoryAsync(int skip = 0, int take = 50)
    {
        EnsureUnlocked();

        // Always return locally tracked transactions (works on all networks)
        var localTxs = await _txTracker.GetAllAsync();

        // On mainnet/testnet, also try the Esplora explorer for full history
        var effectiveMode = _activeWallet?.WifNetwork ?? _networkMode;
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
                foreach (var addr in rawAddresses)
                {
                    var txInfos = await _blockchain.GetAddressTransactionsAsync(addr, 0, take);
                    foreach (var tx in txInfos)
                        allTxs.TryAdd(tx.TxId, tx);
                }

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
                    };
                }).ToList();

                // Merge: explorer txs + local txs (local wins on duplicates for freshness)
                var merged = new Dictionary<string, TransactionRecord>();
                foreach (var tx in explorerRecords) merged.TryAdd(tx.TxId, tx);
                foreach (var tx in localTxs) merged[tx.TxId] = tx; // local overwrites
                localTxs = merged.Values.ToList();
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

    public Task<List<Contact>> GetContactsAsync() => Task.FromResult(new List<Contact>());
    public Task AddContactAsync(Contact contact) => Task.CompletedTask;
    public Task RemoveContactAsync(string contactId) => Task.CompletedTask;

    /// <summary>
    /// Delete the active wallet and all associated data.
    /// </summary>
    public async Task DeleteWalletAsync()
    {
        var walletId = await _walletStore.GetActiveWalletIdAsync();
        if (walletId != null)
        {
            await _walletStore.DeleteWalletAsync(walletId);
        }
        _hd = null;
        _singleKey = null;
        _activeWallet = null;
    }

    /// <summary>
    /// Wipe everything — all wallets, contacts, tx history, preferences.
    /// </summary>
    public async Task WipeAllDataAsync()
    {
        await _walletStore.ClearAllAsync();
        _hd = null;
        _singleKey = null;
        _activeWallet = null;
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

        if (_hd == null)
            throw new InvalidOperationException("Digi-ID requires an HD wallet (not WIF import).");

        var siteIndex = DigiIdService.DeriveSiteIndex(request.Domain);
        var siteKey = _hd.DeriveDigiIdKey(siteIndex);
        var response = DigiIdService.Sign(request, siteKey.PrivateKey);

        return Task.FromResult(response);
    }

    private void EnsureUnlocked()
    {
        if ((_hd == null && _singleKey == null) || _activeWallet == null)
            throw new InvalidOperationException("Wallet is locked. Unlock it first.");
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
}
