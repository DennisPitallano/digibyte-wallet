using System.Text.Json;
using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Manages multisig wallet creation, PSBT signing workflow, and pending transaction state.
/// </summary>
public class MultisigWalletService
{
    private readonly ISecureStorage _storage;
    private readonly IBlockchainService _blockchain;
    private readonly ICryptoService _crypto;
    private string _networkMode = "mainnet";

    private const string MultisigConfigPrefix = "multisig_config_";
    private const string MultisigPendingPrefix = "multisig_pending_";
    private const string MultisigListKey = "multisig_wallet_list";

    public MultisigWalletService(ISecureStorage storage, IBlockchainService blockchain, ICryptoService crypto)
    {
        _storage = storage;
        _blockchain = blockchain;
        _crypto = crypto;
    }

    public void SetNetworkMode(string mode) => _networkMode = mode;

    private Network CurrentNetwork => _networkMode switch
    {
        "regtest" => DigiByteNetwork.Regtest,
        "testnet" => DigiByteNetwork.Testnet,
        _ => DigiByteNetwork.Mainnet,
    };

    /// <summary>
    /// Creates a new multisig wallet configuration from public keys.
    /// </summary>
    public async Task<MultisigWalletConfig> CreateMultisigWalletAsync(
        string name,
        int requiredSignatures,
        List<CoSigner> coSigners,
        string addressType = "p2sh-p2wsh")
    {
        var service = new MultisigService(CurrentNetwork);

        var pubKeys = coSigners
            .Select(cs => new PubKey(cs.PublicKeyHex))
            .ToArray();

        var redeemScript = service.CreateRedeemScript(requiredSignatures, pubKeys);
        var address = addressType == "p2wsh"
            ? service.GetP2WSH_Address(redeemScript)
            : service.GetP2SH_P2WSH_Address(redeemScript);

        var walletId = Guid.NewGuid().ToString("N");
        var config = new MultisigWalletConfig
        {
            WalletId = walletId,
            RequiredSignatures = requiredSignatures,
            TotalSigners = coSigners.Count,
            CoSigners = coSigners,
            RedeemScriptHex = redeemScript.ToHex(),
            Address = address.ToString(),
            AddressType = addressType,
            OwnKeyIndex = coSigners.FindIndex(cs => cs.IsLocal),
            CreatedAt = DateTime.UtcNow,
        };

        // Persist
        await SaveConfigAsync(config);
        await AddToWalletListAsync(walletId, name);

        return config;
    }

    /// <summary>
    /// Imports a multisig wallet from a redeem script hex (shared by another co-signer).
    /// </summary>
    public async Task<MultisigWalletConfig> ImportMultisigWalletAsync(
        string name,
        string redeemScriptHex,
        int ownKeyIndex = -1,
        string addressType = "p2sh-p2wsh")
    {
        var redeemScript = new Script(Convert.FromHexString(redeemScriptHex));
        var service = new MultisigService(CurrentNetwork);

        // Extract M and pubkeys from the redeem script
        var payToMultiSig = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript);
        if (payToMultiSig == null)
            throw new ArgumentException("Invalid multisig redeem script.");

        var address = addressType == "p2wsh"
            ? service.GetP2WSH_Address(redeemScript)
            : service.GetP2SH_P2WSH_Address(redeemScript);

        var coSigners = payToMultiSig.PubKeys.Select((pk, i) => new CoSigner
        {
            Name = $"Signer {i + 1}",
            PublicKeyHex = pk.ToHex(),
            IsLocal = i == ownKeyIndex,
        }).ToList();

        var walletId = Guid.NewGuid().ToString("N");
        var config = new MultisigWalletConfig
        {
            WalletId = walletId,
            RequiredSignatures = payToMultiSig.SignatureCount,
            TotalSigners = payToMultiSig.PubKeys.Length,
            CoSigners = coSigners,
            RedeemScriptHex = redeemScriptHex,
            Address = address.ToString(),
            AddressType = addressType,
            OwnKeyIndex = ownKeyIndex,
        };

        await SaveConfigAsync(config);
        await AddToWalletListAsync(walletId, name);

        return config;
    }

    /// <summary>
    /// Gets the balance of a multisig wallet address.
    /// </summary>
    public async Task<long> GetBalanceAsync(MultisigWalletConfig config)
    {
        return await _blockchain.GetBalanceAsync(config.Address);
    }

    /// <summary>
    /// Creates a new spending PSBT from the multisig wallet.
    /// </summary>
    public async Task<PendingMultisigTransaction> CreateSpendingPSBTAsync(
        MultisigWalletConfig config,
        string destinationAddress,
        decimal amountDgb,
        int feeRateSatPerByte = 5,
        string? memo = null)
    {
        var service = new MultisigService(CurrentNetwork);
        var redeemScript = new Script(Convert.FromHexString(config.RedeemScriptHex));
        var destination = BitcoinAddress.Create(destinationAddress, CurrentNetwork);
        var changeAddress = BitcoinAddress.Create(config.Address, CurrentNetwork);
        var amount = Money.Coins(amountDgb);
        var feeRate = new FeeRate(Money.Satoshis(feeRateSatPerByte), 1);

        // Get UTXOs for the multisig address
        var utxoInfos = await _blockchain.GetUtxosAsync(config.Address);
        if (utxoInfos.Count == 0)
            throw new InvalidOperationException("No UTXOs available for spending.");

        var coins = utxoInfos.Select(u => new Coin(
            uint256.Parse(u.TxId),
            u.OutputIndex,
            Money.Satoshis(u.AmountSatoshis),
            new Script(u.ScriptPubKey)
        )).ToList();

        var psbt = service.CreateMultisigPSBT(
            redeemScript, coins, destination, amount, changeAddress, feeRate, memo);

        var pending = new PendingMultisigTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletId = config.WalletId,
            PsbtBase64 = MultisigService.SerializePSBT(psbt),
            Description = $"Send {amountDgb:F8} DGB to {destinationAddress[..12]}...",
            DestinationAddress = destinationAddress,
            AmountSatoshis = amount.Satoshi,
            FeeSatoshis = feeRate.GetFee(250).Satoshi, // Estimated fee for typical multisig tx
            RequiredSignatures = config.RequiredSignatures,
        };

        await SavePendingTransactionAsync(pending);
        return pending;
    }

    /// <summary>
    /// Signs a pending transaction with the local private key.
    /// </summary>
    public async Task<PendingMultisigTransaction> SignPendingTransactionAsync(
        PendingMultisigTransaction pending,
        Key privateKey)
    {
        var service = new MultisigService(CurrentNetwork);
        var psbt = service.DeserializePSBT(pending.PsbtBase64);

        service.SignPSBT(psbt, privateKey);

        var pubKeyHex = privateKey.PubKey.ToHex();
        if (!pending.SignedBy.Contains(pubKeyHex))
            pending.SignedBy.Add(pubKeyHex);

        pending.PsbtBase64 = MultisigService.SerializePSBT(psbt);

        if (pending.HasEnoughSignatures)
            pending.Status = PendingTxStatus.ReadyToFinalize;

        await SavePendingTransactionAsync(pending);
        return pending;
    }

    /// <summary>
    /// Imports a partially-signed PSBT from another co-signer and merges signatures.
    /// </summary>
    public async Task<PendingMultisigTransaction> ImportSignedPSBTAsync(
        PendingMultisigTransaction pending,
        string importedPsbtBase64)
    {
        var service = new MultisigService(CurrentNetwork);
        var existing = service.DeserializePSBT(pending.PsbtBase64);
        var imported = service.DeserializePSBT(importedPsbtBase64);

        var combined = service.CombinePSBTs([existing, imported]);
        pending.PsbtBase64 = MultisigService.SerializePSBT(combined);

        // Update signer list
        foreach (var input in combined.Inputs)
        {
            foreach (var signer in input.PartialSigs.Keys)
            {
                var hex = signer.ToHex();
                if (!pending.SignedBy.Contains(hex))
                    pending.SignedBy.Add(hex);
            }
        }

        if (pending.HasEnoughSignatures)
            pending.Status = PendingTxStatus.ReadyToFinalize;

        await SavePendingTransactionAsync(pending);
        return pending;
    }

    /// <summary>
    /// Finalizes a fully-signed PSBT and broadcasts the transaction.
    /// </summary>
    public async Task<string> FinalizeAndBroadcastAsync(PendingMultisigTransaction pending)
    {
        var service = new MultisigService(CurrentNetwork);
        var psbt = service.DeserializePSBT(pending.PsbtBase64);

        var tx = service.FinalizePSBT(psbt);
        var txId = await _blockchain.BroadcastTransactionAsync(tx.ToBytes());

        pending.Status = PendingTxStatus.Broadcast;
        pending.BroadcastTxId = txId;
        await SavePendingTransactionAsync(pending);

        return txId;
    }

    /// <summary>
    /// Lists all multisig wallet configs.
    /// </summary>
    public async Task<List<(string Id, string Name)>> ListWalletsAsync()
    {
        var list = await ListWalletsRawAsync();
        return list.Select(e => (e.Id, e.Name)).ToList();
    }

    /// <summary>
    /// Loads a multisig wallet config by ID.
    /// </summary>
    public async Task<MultisigWalletConfig?> GetConfigAsync(string walletId)
    {
        var json = await _storage.GetAsync(MultisigConfigPrefix + walletId);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<MultisigWalletConfig>(json);
    }

    /// <summary>
    /// Lists all pending transactions for a multisig wallet.
    /// </summary>
    public async Task<List<PendingMultisigTransaction>> GetPendingTransactionsAsync(string walletId)
    {
        var json = await _storage.GetAsync(MultisigPendingPrefix + walletId);
        if (string.IsNullOrEmpty(json)) return [];
        return JsonSerializer.Deserialize<List<PendingMultisigTransaction>>(json) ?? [];
    }

    /// <summary>
    /// Deletes a multisig wallet and its pending transactions.
    /// </summary>
    public async Task DeleteWalletAsync(string walletId)
    {
        await _storage.RemoveAsync(MultisigConfigPrefix + walletId);
        await _storage.RemoveAsync(MultisigPendingPrefix + walletId);
        await RemoveFromWalletListAsync(walletId);
    }

    // ─── Private persistence helpers ───

    private async Task SaveConfigAsync(MultisigWalletConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        await _storage.SetAsync(MultisigConfigPrefix + config.WalletId, json);
    }

    private async Task SavePendingTransactionAsync(PendingMultisigTransaction pending)
    {
        var all = await GetPendingTransactionsAsync(pending.WalletId);
        var existing = all.FindIndex(p => p.Id == pending.Id);
        if (existing >= 0)
            all[existing] = pending;
        else
            all.Add(pending);

        var json = JsonSerializer.Serialize(all);
        await _storage.SetAsync(MultisigPendingPrefix + pending.WalletId, json);
    }

    private async Task AddToWalletListAsync(string walletId, string name)
    {
        var list = await ListWalletsRawAsync();
        list.Add(new MultisigWalletEntry { Id = walletId, Name = name });
        await _storage.SetAsync(MultisigListKey, JsonSerializer.Serialize(list));
    }

    private async Task RemoveFromWalletListAsync(string walletId)
    {
        var list = await ListWalletsRawAsync();
        list.RemoveAll(e => e.Id == walletId);
        await _storage.SetAsync(MultisigListKey, JsonSerializer.Serialize(list));
    }

    private async Task<List<MultisigWalletEntry>> ListWalletsRawAsync()
    {
        var json = await _storage.GetAsync(MultisigListKey);
        if (string.IsNullOrEmpty(json)) return [];
        return JsonSerializer.Deserialize<List<MultisigWalletEntry>>(json) ?? [];
    }

    private class MultisigWalletEntry
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
    }
}
