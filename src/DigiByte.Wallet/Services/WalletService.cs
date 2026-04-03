using System.Text.Json;
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

    private HdKeyDerivation? _hd;
    private WalletInfo? _activeWallet;
    private bool _isTestnet;

    public bool IsUnlocked => _hd != null;
    public WalletInfo? ActiveWallet => _activeWallet;

    public WalletService(WalletKeyStore walletStore, ICryptoService crypto, IBlockchainService blockchain)
    {
        _keyStore = walletStore;
        _walletStore = walletStore;
        _crypto = crypto;
        _blockchain = blockchain;
    }

    public void SetNetwork(bool isTestnet)
    {
        _isTestnet = isTestnet;
        if (_blockchain is BlockchainApiService apiService)
            apiService.SetNetwork(isTestnet);
        else if (_blockchain is FallbackBlockchainService fallbackService)
            fallbackService.SetNetwork(isTestnet);

        // Re-derive keys on the new network if unlocked
        if (_hd != null && _activeWallet != null)
        {
            // HD derivation is network-agnostic for key material,
            // but address encoding changes per network
        }
    }

    private Network CurrentNetwork => _isTestnet ? DigiByteNetwork.Testnet : DigiByteNetwork.Mainnet;

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

        var mnemonicWords = System.Text.Encoding.UTF8.GetString(decrypted);
        var mnemonic = MnemonicGenerator.FromWords(mnemonicWords);

        _hd = new HdKeyDerivation(mnemonic, network: CurrentNetwork);
        _activeWallet = await GetWalletAsync(walletId);

        return true;
    }

    public Task LockWalletAsync()
    {
        _hd = null;
        _activeWallet = null;
        return Task.CompletedTask;
    }

    public async Task<WalletBalance> GetBalanceAsync()
    {
        EnsureUnlocked();

        // Scan first 20 receiving + 10 change addresses for balance
        var addresses = new List<string>();
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

    public Task<string> GetReceivingAddressAsync()
    {
        EnsureUnlocked();
        var index = _activeWallet!.NextReceivingIndex;
        var key = _hd!.DeriveReceivingKey(index);
        var address = _hd.GetAddress(key);
        return Task.FromResult(address.ToString());
    }

    public Task<List<(int Index, string Address)>> GetReceivingAddressesAsync(int count, int startIndex = 0)
    {
        EnsureUnlocked();
        var addresses = _hd!.DeriveReceivingAddresses(count, startIndex)
            .Select(a => (a.Index, a.Address.ToString()))
            .ToList();
        return Task.FromResult(addresses);
    }

    public Task<string> SendAsync(string destinationAddress, decimal amountDgb, string? memo = null)
    {
        EnsureUnlocked();
        throw new NotImplementedException("Send will be implemented with blockchain service integration");
    }

    public async Task<List<TransactionRecord>> GetTransactionHistoryAsync(int skip = 0, int take = 50)
    {
        EnsureUnlocked();

        // Get the primary receiving address's transactions
        var address = await GetReceivingAddressAsync();
        var txInfos = await _blockchain.GetAddressTransactionsAsync(address, skip, take);

        return txInfos.Select(tx =>
        {
            // Determine if sent or received by checking if our address is in inputs or outputs
            var isSent = tx.Inputs.Any(i => i.Address == address);
            var relevantOutputs = isSent
                ? tx.Outputs.Where(o => o.Address != address)
                : tx.Outputs.Where(o => o.Address == address);

            var amount = relevantOutputs.Sum(o => o.AmountSatoshis);

            return new TransactionRecord
            {
                TxId = tx.TxId,
                Direction = isSent ? TransactionDirection.Sent : TransactionDirection.Received,
                AmountSatoshis = amount,
                FeeSatoshis = tx.FeeSatoshis,
                Timestamp = tx.Timestamp,
                Confirmations = tx.Confirmations,
                CounterpartyAddress = isSent
                    ? tx.Outputs.FirstOrDefault(o => o.Address != address)?.Address
                    : tx.Inputs.FirstOrDefault()?.Address,
            };
        }).ToList();
    }

    public Task<List<Contact>> GetContactsAsync() => Task.FromResult(new List<Contact>());
    public Task AddContactAsync(Contact contact) => Task.CompletedTask;
    public Task RemoveContactAsync(string contactId) => Task.CompletedTask;

    private void EnsureUnlocked()
    {
        if (_hd == null || _activeWallet == null)
            throw new InvalidOperationException("Wallet is locked. Unlock it first.");
    }
}
