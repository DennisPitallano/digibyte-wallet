using System.Collections.Concurrent;
using System.Text.Json;
using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>Whether the active wallet may open a given multisig.</summary>
public enum MultisigAccessState
{
    /// <summary>The active wallet owns it.</summary>
    Owned,

    /// <summary>It exists but belongs to another wallet on this device.</summary>
    OtherWallet,

    /// <summary>It exists, but no wallet has claimed it yet (saved before wallet scoping).</summary>
    Unassigned,

    /// <summary>No multisig has this id.</summary>
    NotFound,
}

/// <summary>Outcome of renaming a multisig, a draft, or a co-signer label.</summary>
public sealed record RenameResult(bool Ok, string? Error, bool DuplicateWarning, string? Name);

/// <summary>An imported signature copy was refused; <see cref="Exception.Message"/> says why in plain words.</summary>
public sealed class PsbtImportException(string message) : Exception(message);

/// <summary>
/// Manages multisig wallet creation, PSBT signing workflow, and pending transaction state.
/// Every multisig belongs to the wallet(s) that own it; lists and direct opens are scoped to the active wallet.
/// </summary>
public class MultisigWalletService
{
    private readonly ISecureStorage _storage;
    private readonly IBlockchainService _blockchain;
    private readonly ICryptoService _crypto;
    private string _networkMode = "mainnet";

    private readonly HashSet<string> _broadcastsInFlight = [];
    private readonly ConcurrentDictionary<string, Lazy<Task<PendingMultisigTransaction>>> _submissions = new();

    private const string MultisigConfigPrefix = "multisig_config_";
    private const string MultisigPendingPrefix = "multisig_pending_";
    private const string MultisigListKey = "multisig_wallet_list";
    private const string MultisigDraftsKey = "multisig_drafts";

    /// <summary>Longest allowed multisig name or co-signer label.</summary>
    public const int MaxNameLength = 40;

    /// <summary>Same default fee rate the regular Send page starts from (sat per virtual byte).</summary>
    public const int DefaultFeeRateSatPerVByte = 200;

    public MultisigWalletService(ISecureStorage storage, IBlockchainService blockchain, ICryptoService crypto)
    {
        _storage = storage;
        _blockchain = blockchain;
        _crypto = crypto;
    }

    public void SetNetworkMode(string mode) => _networkMode = mode;

    /// <summary>
    /// Raised with a multisig's id after its saved proposals changed (one saved, cancelled ones removed, or the
    /// multisig deleted), so counts shown elsewhere can follow (specs/026-multisig-wallet-experience, FR-010a).
    /// Not raised by reading. A handler that throws does not undo or fail the change.
    /// </summary>
    public event Action<string>? PendingChanged;

    private void RaisePendingChanged(string walletId)
    {
        try { PendingChanged?.Invoke(walletId); }
        catch { /* a listener's failure must never fail a save */ }
    }

    /// <summary>
    /// When set, the network mode is read from here on every use (the app's current setting), so a switch to testnet
    /// or regtest in Settings applies to multisig addresses and PSBTs too. Otherwise <see cref="SetNetworkMode"/> decides.
    /// </summary>
    public Func<string?>? NetworkModeSource { get; set; }

    private Network CurrentNetwork => (NetworkModeSource?.Invoke() ?? _networkMode) switch
    {
        "regtest" => DigiByteNetwork.Regtest,
        "testnet" => DigiByteNetwork.Testnet,
        _ => DigiByteNetwork.Mainnet,
    };

    /// <summary>The network addresses and PSBTs are checked against.</summary>
    public Network ActiveNetwork => CurrentNetwork;

    // ─── Creating and importing ───

    /// <summary>
    /// Creates a new multisig wallet configuration from public keys.
    /// </summary>
    public async Task<MultisigWalletConfig> CreateMultisigWalletAsync(
        string name,
        int requiredSignatures,
        List<CoSigner> coSigners,
        string addressType = "p2sh-p2wsh",
        string? ownerWalletId = null)
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
            Name = name,
            OwnerWalletIds = string.IsNullOrEmpty(ownerWalletId) ? [] : [ownerWalletId],
        };

        // Persist
        await SaveConfigAsync(config);
        await AddToWalletListAsync(walletId, name, ownerWalletId);

        return config;
    }

    /// <summary>File name and text for a multisig backup file (the format the Import page reads back).</summary>
    public static (string FileName, string Content) BuildBackup(MultisigWalletConfig config)
    {
        var label = config.Label.Replace(" ", "-").ToLowerInvariant();
        var nameLine = string.IsNullOrWhiteSpace(config.Name) ? "" : $"Name: {config.Name}\n";
        var content = $"DigiByte Multisig Wallet Backup\n" +
                      $"==============================\n" +
                      nameLine +
                      $"Label: {config.Label}\n" +
                      $"Threshold: {config.RequiredSignatures}-of-{config.TotalSigners}\n" +
                      $"Address: {config.Address}\n" +
                      $"Address Type: {config.AddressType}\n\n" +
                      $"Redeem Script (hex):\n{config.RedeemScriptHex}\n\n" +
                      $"Co-Signers:\n" +
                      string.Join("\n", config.CoSigners.Select(cs => $"  - {cs.Name}: {cs.PublicKeyHex}")) +
                      $"\n\nIMPORTANT: Keep this file safe. You need the redeem script to recover this multisig wallet.\n" +
                      $"Your seed phrase alone CANNOT restore multisig wallets.\n" +
                      $"To restore: Multisig → Import → paste the redeem script above.\n" +
                      $"No backup file? Each signer restores their own seed, then Multisig → Create with the same threshold and all public keys above.\n";
        return ($"multisig-{label}-redeem-script.txt", content);
    }

    /// <summary>
    /// Finds which key in a redeem script belongs to <paramref name="ownPubKeyHex"/> (keys are BIP67-sorted, so the
    /// position isn't something a person can know). Returns -1 when the key isn't in the script (watch-only) or the
    /// script is not a valid multisig script.
    /// </summary>
    public static int FindOwnKeyIndex(string redeemScriptHex, string? ownPubKeyHex)
    {
        if (string.IsNullOrWhiteSpace(ownPubKeyHex) || string.IsNullOrWhiteSpace(redeemScriptHex))
            return -1;
        try
        {
            var script = new Script(Convert.FromHexString(redeemScriptHex.Trim()));
            var parsed = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);
            if (parsed == null) return -1;
            return Array.FindIndex(parsed.PubKeys,
                pk => string.Equals(pk.ToHex(), ownPubKeyHex.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        catch { return -1; }
    }

    /// <summary>
    /// Imports a multisig wallet from a redeem script hex (shared by another co-signer).
    /// </summary>
    public async Task<MultisigWalletConfig> ImportMultisigWalletAsync(
        string name,
        string redeemScriptHex,
        int ownKeyIndex = -1,
        string addressType = "p2sh-p2wsh",
        string? ownerWalletId = null)
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
            Name = name,
            OwnerWalletIds = string.IsNullOrEmpty(ownerWalletId) ? [] : [ownerWalletId],
        };

        await SaveConfigAsync(config);
        await AddToWalletListAsync(walletId, name, ownerWalletId);

        return config;
    }

    /// <summary>
    /// Gets the balance of a multisig wallet address.
    /// </summary>
    public async Task<long> GetBalanceAsync(MultisigWalletConfig config)
    {
        return await _blockchain.GetBalanceAsync(config.Address);
    }

    // ─── Spending proposals ───

    /// <summary>The multisig's unspent outputs, for showing available balance and fee before a proposal is made.</summary>
    public Task<List<UtxoInfo>> GetSpendableUtxosAsync(MultisigWalletConfig config) =>
        _blockchain.GetUtxosAsync(config.Address);

    /// <summary>
    /// Creates a new spending PSBT from the multisig wallet. The coins, fee and change are the ones
    /// <see cref="MultisigSendHelper.EstimateSend"/> shows the user. Repeating a call with the same
    /// <paramref name="submissionToken"/> returns the proposal created the first time instead of making a second one.
    /// </summary>
    public Task<PendingMultisigTransaction> CreateSpendingPSBTAsync(
        MultisigWalletConfig config,
        string destinationAddress,
        decimal amountDgb,
        int feeRateSatPerVByte = DefaultFeeRateSatPerVByte,
        string? memo = null,
        string? submissionToken = null) =>
        CreateSpendingPSBTAsync(config, [(destinationAddress, amountDgb)], feeRateSatPerVByte, memo, submissionToken);

    /// <summary>
    /// Creates one spending PSBT that pays every one of <paramref name="recipients"/>, in the order given
    /// (specs/029-multisig-multiple-recipients). The coins, fee and change are the ones
    /// <see cref="MultisigSendHelper.EstimateSend(IReadOnlyList{UtxoInfo}, int, IReadOnlyList{long}, int, int, string, int)"/>
    /// shows the user. Refuses an empty list, more than <see cref="MultisigSendHelper.MaxRecipients"/> recipients, and any
    /// row <see cref="MultisigSendHelper.ValidateRecipients"/> flags. Repeating a call with the same
    /// <paramref name="submissionToken"/> returns the proposal created the first time instead of making a second one.
    /// </summary>
    public Task<PendingMultisigTransaction> CreateSpendingPSBTAsync(
        MultisigWalletConfig config,
        IReadOnlyList<(string Address, decimal AmountDgb)> recipients,
        int feeRateSatPerVByte = DefaultFeeRateSatPerVByte,
        string? memo = null,
        string? submissionToken = null)
    {
        if (string.IsNullOrEmpty(submissionToken))
            return CreateProposalCoreAsync(config, recipients, feeRateSatPerVByte, memo);

        return _submissions.GetOrAdd(
            config.WalletId + ":" + submissionToken,
            _ => new Lazy<Task<PendingMultisigTransaction>>(
                () => CreateProposalCoreAsync(config, recipients, feeRateSatPerVByte, memo))).Value;
    }

    private async Task<PendingMultisigTransaction> CreateProposalCoreAsync(
        MultisigWalletConfig config, IReadOnlyList<(string Address, decimal AmountDgb)> recipients, int feeRate, string? memo)
    {
        if (recipients.Count == 0)
            throw new ArgumentException("Add at least one recipient.", nameof(recipients));
        if (recipients.Count > MultisigSendHelper.MaxRecipients)
            throw new ArgumentException($"A payment can have at most {MultisigSendHelper.MaxRecipients} recipients.", nameof(recipients));

        var network = CurrentNetwork;
        var lines = new List<ProposalRecipient>(recipients.Count);
        foreach (var (address, amountDgb) in recipients)
        {
            if (decimal.Round(amountDgb, 8) != amountDgb)
                throw new ArgumentException("An amount can have at most 8 decimal places.", nameof(recipients));
            lines.Add(new ProposalRecipient((address ?? "").Trim(), (long)(amountDgb * 100_000_000m)));
        }

        var problems = MultisigSendHelper.ValidateRecipients(
            lines.Select(l => (l.Address, l.AmountSatoshis)).ToList(), network, config.Address);
        for (var i = 0; i < problems.Count; i++)
        {
            if (problems[i] is not { } problem) continue;
            if (recipients.Count == 1 && problem is RecipientProblem.AddressMissing or RecipientProblem.WrongNetwork or RecipientProblem.InvalidAddress)
                throw new ArgumentException(MultisigSendHelper.ValidateDestination(lines[i].Address, network, config.Address).Error, "destinationAddress");
            if (problem is RecipientProblem.NoAmount or RecipientProblem.BelowMinimum)
                continue;   // the estimate below refuses these with its usual message
            if (recipients.Count == 1 && problem is RecipientProblem.OwnAddress)
                continue;   // a single payment back to the multisig stays allowed, as before (the page warns about it)
            throw new ArgumentException(RecipientMessage(problem, i), nameof(recipients));
        }

        var memoBytes = string.IsNullOrWhiteSpace(memo) ? 0 : System.Text.Encoding.UTF8.GetByteCount(memo);
        var utxos = await _blockchain.GetUtxosAsync(config.Address);
        var estimate = MultisigSendHelper.EstimateSend(
            utxos, feeRate, lines.Select(l => l.AmountSatoshis).ToList(),
            config.RequiredSignatures, config.TotalSigners, config.AddressType, memoBytes);
        if (!estimate.Ok)
        {
            if (recipients.Count > 1 && estimate.Problem is SendProblem.NoAmount or SendProblem.BelowMinimum)
                throw new ArgumentException(estimate.Error, nameof(recipients));
            throw new InvalidOperationException(estimate.Error);
        }

        var service = new MultisigService(network);
        var redeemScript = new Script(Convert.FromHexString(config.RedeemScriptHex));
        var multisigAddress = BitcoinAddress.Create(config.Address, network);
        var payments = lines.Select(l => (BitcoinAddress.Create(l.Address, network), Money.Satoshis(l.AmountSatoshis))).ToList();

        // Every coin sits at the multisig address, so its script is the address's script (explorers often omit it).
        var coins = estimate.Inputs.Select(u => new Coin(
            uint256.Parse(u.TxId),
            u.OutputIndex,
            Money.Satoshis(u.AmountSatoshis),
            multisigAddress.ScriptPubKey)).ToList();

        var psbt = service.CreateMultisigPSBTWithFee(
            redeemScript, coins, payments, multisigAddress,
            estimate.FeeSatoshis, MultisigSendHelper.DustThresholdSatoshis, memo);

        var pending = new PendingMultisigTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletId = config.WalletId,
            PsbtBase64 = MultisigService.SerializePSBT(psbt),
            Description = DescribePayment(lines),
            DestinationAddress = lines[0].Address,
            AmountSatoshis = estimate.AmountSatoshis,
            FeeSatoshis = estimate.FeeSatoshis,
            RequiredSignatures = config.RequiredSignatures,
            UnsignedTxId = psbt.GetGlobalTransaction().GetHash().ToString(),
            Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
            Recipients = lines,
        };

        await SavePendingTransactionAsync(pending);
        return pending;
    }

    /// <summary>The stored English description of a payment: the short address for one recipient, the count for several.</summary>
    private static string DescribePayment(IReadOnlyList<ProposalRecipient> lines)
    {
        var total = lines.Sum(l => l.AmountSatoshis) / 100_000_000m;
        return lines.Count == 1
            ? $"Send {total:F8} DGB to {Shorten(lines[0].Address)}"
            : $"Send {total:F8} DGB to {lines.Count} recipients";
    }

    private static string RecipientMessage(RecipientProblem problem, int index) => problem switch
    {
        RecipientProblem.AddressMissing => $"Recipient {index + 1}: enter a destination address.",
        RecipientProblem.WrongNetwork => $"Recipient {index + 1}: this address is for a different network.",
        RecipientProblem.InvalidAddress => $"Recipient {index + 1}: this is not a valid DigiByte address.",
        RecipientProblem.Repeated => $"Recipient {index + 1}: this address is already a recipient of this payment.",
        RecipientProblem.OwnAddress => $"Recipient {index + 1}: this is the shared wallet's own address.",
        _ => $"Recipient {index + 1} can't be paid.",
    };

    private static string Shorten(string address) => address.Length <= 12 ? address : address[..12] + "...";

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
    /// Imports a partially-signed PSBT from another co-signer and merges signatures. The copy must be for this very
    /// transaction and signed only by keys of this multisig; anything else is refused with a plain-language reason.
    /// </summary>
    public async Task<PendingMultisigTransaction> ImportSignedPSBTAsync(
        PendingMultisigTransaction pending,
        string importedPsbtBase64)
    {
        if (pending.Status is PendingTxStatus.Cancelled or PendingTxStatus.Broadcast)
            throw new PsbtImportException("This proposal is closed, so it can't take more signatures.");

        var service = new MultisigService(CurrentNetwork);

        PSBT imported;
        try { imported = service.DeserializePSBT((importedPsbtBase64 ?? "").Trim()); }
        catch (Exception) { throw new PsbtImportException("That is not a valid signed transaction (PSBT)."); }

        PSBT existing;
        try { existing = service.DeserializePSBT(pending.PsbtBase64); }
        catch (Exception) { throw new PsbtImportException("This proposal's stored transaction could not be read."); }

        var expected = string.IsNullOrEmpty(pending.UnsignedTxId)
            ? existing.GetGlobalTransaction().GetHash().ToString()
            : pending.UnsignedTxId;
        if (!string.Equals(imported.GetGlobalTransaction().GetHash().ToString(), expected, StringComparison.OrdinalIgnoreCase))
            throw new PsbtImportException("This signature copy is for a different transaction, not this proposal.");

        var config = await LoadConfigAsync(pending.WalletId)
            ?? throw new PsbtImportException("The multisig for this proposal could not be found.");
        var allowedKeys = GetRedeemKeys(config);
        foreach (var input in imported.Inputs)
        {
            foreach (var signer in input.PartialSigs.Keys)
            {
                if (!allowedKeys.Contains(signer.ToHex()))
                    throw new PsbtImportException("This copy is signed by a key that is not a co-signer of this multisig.");
            }
        }

        var combined = service.CombinePSBTs([existing, imported]);
        pending.PsbtBase64 = MultisigService.SerializePSBT(combined);
        pending.UnsignedTxId = expected;

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

        if (pending.HasEnoughSignatures && pending.Status == PendingTxStatus.AwaitingSignatures)
            pending.Status = PendingTxStatus.ReadyToFinalize;

        await SavePendingTransactionAsync(pending);
        return pending;
    }

    private static HashSet<string> GetRedeemKeys(MultisigWalletConfig config)
    {
        try
        {
            var script = new Script(Convert.FromHexString(config.RedeemScriptHex));
            var parsed = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);
            if (parsed != null)
                return parsed.PubKeys.Select(k => k.ToHex()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch { /* fall back to the stored co-signer list */ }
        return config.CoSigners.Select(c => c.PublicKeyHex).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finalizes a fully-signed PSBT and broadcasts the transaction. A cancelled proposal is never broadcast, and
    /// concurrent calls for the same proposal broadcast once. A failed broadcast keeps the proposal recoverable.
    /// </summary>
    public async Task<string> FinalizeAndBroadcastAsync(PendingMultisigTransaction pending)
    {
        if (pending.Status == PendingTxStatus.Cancelled)
            throw new InvalidOperationException("This proposal was cancelled and can no longer be broadcast from this device.");
        if (pending.Status == PendingTxStatus.Broadcast && !string.IsNullOrEmpty(pending.BroadcastTxId))
            return pending.BroadcastTxId;

        lock (_broadcastsInFlight)
        {
            if (!_broadcastsInFlight.Add(pending.Id))
                throw new InvalidOperationException("A broadcast of this proposal is already in progress.");
        }

        try
        {
            var service = new MultisigService(CurrentNetwork);
            var psbt = service.DeserializePSBT(pending.PsbtBase64);
            var tx = service.FinalizePSBT(psbt);

            string txId;
            try
            {
                txId = await _blockchain.BroadcastTransactionAsync(tx.ToBytes());
            }
            catch (Exception ex) when (IsAlreadyKnown(ex))
            {
                // Another co-signer broadcast the same signed transaction first (online signing rooms, research R7).
                txId = tx.GetHash().ToString();
            }
            catch (Exception ex)
            {
                pending.Status = PendingTxStatus.Failed;
                pending.FailureReason = string.IsNullOrWhiteSpace(ex.Message) ? "The network did not accept the transaction." : ex.Message;
                pending.UpdatedAt = DateTime.UtcNow;
                await SavePendingTransactionAsync(pending);
                throw;
            }

            pending.Status = PendingTxStatus.Broadcast;
            pending.BroadcastTxId = txId;
            pending.FailureReason = null;
            await SavePendingTransactionAsync(pending);
            return txId;
        }
        finally
        {
            lock (_broadcastsInFlight) _broadcastsInFlight.Remove(pending.Id);
        }
    }

    /// <summary>True when the node refused the transaction only because it already has it (mempool or chain).</summary>
    private static bool IsAlreadyKnown(Exception ex)
    {
        var message = ex.Message ?? "";
        return message.Contains("already-in-mempool", StringComparison.OrdinalIgnoreCase)
               || message.Contains("already in block chain", StringComparison.OrdinalIgnoreCase)
               || message.Contains("already-known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("already known", StringComparison.OrdinalIgnoreCase)
               || message.Contains("txn-already", StringComparison.OrdinalIgnoreCase);
    }

    // ─── Online signing rooms (specs/017-multisig-online-signing) ───

    /// <summary>Signatures a room copy brought: keys whose signatures were merged, and keys whose signatures were discarded.</summary>
    public sealed record RoomMergeResult(IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected);

    /// <summary>
    /// The multisig <paramref name="ownerWalletId"/> owns whose redeem script is the M-of-N made from
    /// <paramref name="coSignerKeysHex"/> (any order). Null when none matches. <c>OwnKeyIndex</c> is -1 when
    /// <paramref name="ownPublicKeyHex"/> is not one of its keys (watch-only for this wallet).
    /// </summary>
    public async Task<MultisigWalletConfig?> FindByRedeemScriptAsync(
        IReadOnlyList<string> coSignerKeysHex, int requiredSignatures, string ownerWalletId, string? ownPublicKeyHex)
    {
        var script = TryBuildRedeemScriptHex(coSignerKeysHex, requiredSignatures);
        if (script is null) return null;

        foreach (var (id, _) in await ListWalletsAsync(ownerWalletId, ownPublicKeyHex))
        {
            var config = await GetConfigAsync(id, ownerWalletId, ownPublicKeyHex);
            if (config is not null && string.Equals(config.RedeemScriptHex, script, StringComparison.OrdinalIgnoreCase))
                return config;
        }
        return null;
    }

    /// <summary>Wallets on this device that own a multisig matching these keys and threshold (for the "switch wallet" offer).</summary>
    public async Task<List<string>> FindOwnerWalletIdsByRedeemScriptAsync(IReadOnlyList<string> coSignerKeysHex, int requiredSignatures)
    {
        var script = TryBuildRedeemScriptHex(coSignerKeysHex, requiredSignatures);
        if (script is null) return [];

        var owners = new List<string>();
        foreach (var entry in await ListWalletsRawAsync())
        {
            var config = await LoadConfigAsync(entry.Id);
            if (config is not null && string.Equals(config.RedeemScriptHex, script, StringComparison.OrdinalIgnoreCase))
                owners.AddRange(entry.OwnerWalletIds);
        }
        return owners.Distinct().ToList();
    }

    private string? TryBuildRedeemScriptHex(IReadOnlyList<string> coSignerKeysHex, int requiredSignatures)
    {
        try
        {
            var keys = coSignerKeysHex.Select(h => new PubKey(h.Trim())).ToArray();
            return new MultisigService(CurrentNetwork).CreateRedeemScript(requiredSignatures, keys).ToHex();
        }
        catch { return null; }
    }

    /// <summary>
    /// Saves a signing room's payment as a local pending proposal (FR-006). Destination, amount, fee and memo come from
    /// the PSBT (<see cref="MultisigProposalReader"/>), never from the room. If this device already holds the proposal, that
    /// copy is returned (and the room copy's signatures are merged into it). Signatures in the room copy are each verified.
    /// </summary>
    public async Task<PendingMultisigTransaction> AdoptProposalAsync(MultisigWalletConfig config, string psbtBase64)
    {
        var facts = MultisigProposalReader.Read(psbtBase64, config, CurrentNetwork);
        if (facts.Problems.Count > 0)
            throw new PsbtImportException(facts.Problems[0]);

        var existing = (await GetPendingTransactionsAsync(config.WalletId))
            .FirstOrDefault(p => string.Equals(p.UnsignedTxId, facts.UnsignedTxId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Status == PendingTxStatus.Cancelled)
                throw new PsbtImportException("You cancelled this payment on this device, so it can't be signed here.");
            if (existing.Status != PendingTxStatus.Broadcast)
                await MergeRoomSignaturesAsync(existing, psbtBase64);
            return existing;
        }

        var pending = NewPendingFromFacts(config, facts, psbtBase64, receivedFromCoSigner: false);
        await SavePendingTransactionAsync(pending);
        await MergeRoomSignaturesAsync(pending, psbtBase64);
        return pending;
    }

    /// <summary>
    /// Receives a co-signer's copy of a proposal (scan, paste or file; specs/022-multisig-proposal-scan-import). Adds it when
    /// this device does not hold it, merges verified signatures when it does, and reports a cancelled or already-sent proposal
    /// without changing it. Recipient, amount and fee come from the PSBT itself. Anything unusable (not this multisig's coins,
    /// unreadable, a signature from a key that is not a co-signer) is refused with a plain-language reason and nothing is saved.
    /// </summary>
    public async Task<ProposalReceiveResult> ReceiveProposalAsync(MultisigWalletConfig config, string psbtBase64)
    {
        var text = (psbtBase64 ?? "").Trim();
        var facts = MultisigProposalReader.Read(text, config, CurrentNetwork);
        if (facts.Problems.Count > 0)
            throw new PsbtImportException(facts.Problems[0]);
        RejectForeignSigners(text, config);

        var existing = (await GetPendingTransactionsAsync(config.WalletId))
            .FirstOrDefault(p => string.Equals(p.UnsignedTxId, facts.UnsignedTxId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Status == PendingTxStatus.Cancelled)
                return new ProposalReceiveResult(ProposalReceiveOutcome.Cancelled, existing, 0, []);
            if (existing.Status == PendingTxStatus.Broadcast)
                return new ProposalReceiveResult(ProposalReceiveOutcome.Closed, existing, 0, []);
            if (CheckProposalMatches(existing, config, text) is { } mismatch)
                throw new PsbtImportException(mismatch);

            var merged = await MergeRoomSignaturesAsync(existing, text);
            return new ProposalReceiveResult(
                merged.Accepted.Count > 0 ? ProposalReceiveOutcome.Merged : ProposalReceiveOutcome.AlreadyUpToDate,
                existing, merged.Accepted.Count, merged.Rejected);
        }

        var pending = NewPendingFromFacts(config, facts, text, receivedFromCoSigner: true);
        await SavePendingTransactionAsync(pending);
        var added = await MergeRoomSignaturesAsync(pending, text);
        return new ProposalReceiveResult(ProposalReceiveOutcome.Added, pending, added.Accepted.Count, added.Rejected);
    }

    /// <summary>
    /// Brings a proposal cancelled on this device back, after the user confirmed it, merging the verified signatures of
    /// <paramref name="psbtBase64"/>. The copy must be for this very transaction. Nothing changes if any check fails.
    /// </summary>
    public async Task<PendingMultisigTransaction> RestoreCancelledAsync(PendingMultisigTransaction pending, string psbtBase64)
    {
        if (pending.Status != PendingTxStatus.Cancelled)
            throw new PsbtImportException("This proposal was not cancelled, so there is nothing to restore.");

        var text = (psbtBase64 ?? "").Trim();
        var config = await LoadConfigAsync(pending.WalletId)
            ?? throw new PsbtImportException("The multisig for this proposal could not be found.");
        if (CheckProposalMatches(pending, config, text) is { } mismatch)
            throw new PsbtImportException(mismatch);
        RejectForeignSigners(text, config);

        pending.Status = PendingTxStatus.AwaitingSignatures;
        pending.CancelledAt = null;
        await MergeRoomSignaturesAsync(pending, text); // saves, and moves to ReadyToFinalize at the threshold
        return pending;
    }

    /// <summary>
    /// Another multisig of <paramref name="ownerWalletId"/> that the payment in <paramref name="psbtBase64"/> spends from
    /// (matched on the script of its first coin), for the "open that wallet" offer. Null when none matches or the text is not a PSBT.
    /// </summary>
    public async Task<ProposalWalletMatch?> FindWalletForProposalAsync(
        string psbtBase64, string ownerWalletId, string? ownPublicKeyHex, string excludeWalletId)
    {
        Script? spent;
        try { spent = PSBT.Parse((psbtBase64 ?? "").Trim(), CurrentNetwork).Inputs.FirstOrDefault()?.WitnessUtxo?.ScriptPubKey; }
        catch { return null; }
        if (spent is null) return null;

        foreach (var (id, name) in await ListWalletsAsync(ownerWalletId, ownPublicKeyHex))
        {
            if (id == excludeWalletId) continue;
            var config = await GetConfigAsync(id, ownerWalletId, ownPublicKeyHex);
            if (config is null) continue;
            try
            {
                if (BitcoinAddress.Create(config.Address, CurrentNetwork).ScriptPubKey == spent)
                    return new ProposalWalletMatch(id, name);
            }
            catch { /* an address that does not parse on this network can't match */ }
        }
        return null;
    }

    /// <summary>
    /// The ids (<c>txid:index</c>) of <paramref name="inputs"/> that are no longer among the multisig's unspent coins, i.e.
    /// a payment spending them can't complete. Empty when chain data is unavailable: nothing is guessed.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetSpentInputIdsAsync(MultisigWalletConfig config, IEnumerable<OutPoint> inputs)
    {
        try
        {
            var unspent = (await GetSpendableUtxosAsync(config))
                .Select(u => $"{u.TxId}:{u.OutputIndex}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return inputs.Select(i => $"{i.Hash}:{i.N}").Where(id => !unspent.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    /// <summary>A new local proposal for <paramref name="psbtBase64"/>: facts from the PSBT itself, every signature stripped (verified ones are merged afterwards).</summary>
    private PendingMultisigTransaction NewPendingFromFacts(MultisigWalletConfig config, ProposalFacts facts, string psbtBase64, bool receivedFromCoSigner)
    {
        var unsigned = new MultisigService(CurrentNetwork).DeserializePSBT(psbtBase64.Trim());
        foreach (var input in unsigned.Inputs) input.PartialSigs.Clear();

        return new PendingMultisigTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletId = config.WalletId,
            PsbtBase64 = MultisigService.SerializePSBT(unsigned),
            Description = DescribePayment(facts.Recipients),
            DestinationAddress = facts.DestinationAddress,
            AmountSatoshis = facts.AmountSat,
            Recipients = [.. facts.Recipients],
            FeeSatoshis = facts.FeeSat,
            RequiredSignatures = config.RequiredSignatures,
            UnsignedTxId = facts.UnsignedTxId,
            Memo = facts.Memo,
            ReceivedFromCoSigner = receivedFromCoSigner,
        };
    }

    /// <summary>Refuses a copy carrying a signature from a key that is not a co-signer of this multisig (specs/022 R1a).</summary>
    private void RejectForeignSigners(string psbtBase64, MultisigWalletConfig config)
    {
        PSBT psbt;
        try { psbt = new MultisigService(CurrentNetwork).DeserializePSBT(psbtBase64.Trim()); }
        catch { throw new PsbtImportException("That is not a valid signed transaction (PSBT)."); }

        var allowed = GetRedeemKeys(config);
        foreach (var input in psbt.Inputs)
            foreach (var signer in input.PartialSigs.Keys)
                if (!allowed.Contains(signer.ToHex()))
                    throw new PsbtImportException("This copy is signed by a key that is not a co-signer of this multisig.");
    }

    /// <summary>
    /// Null when <paramref name="psbtBase64"/> is the same payment as <paramref name="pending"/> and the local copy's saved
    /// recipients (in order), amounts and fee match what its transaction really does; otherwise a plain-language reason (FR-005).
    /// </summary>
    public string? CheckProposalMatches(PendingMultisigTransaction pending, MultisigWalletConfig config, string psbtBase64)
    {
        var room = MultisigProposalReader.Read(psbtBase64, config, CurrentNetwork);
        if (room.Problems.Count > 0) return room.Problems[0];

        var expected = pending.UnsignedTxId;
        if (string.IsNullOrEmpty(expected))
        {
            try { expected = new MultisigService(CurrentNetwork).DeserializePSBT(pending.PsbtBase64).GetGlobalTransaction().GetHash().ToString(); }
            catch { return "This device's copy of the payment could not be read."; }
        }
        if (!string.Equals(room.UnsignedTxId, expected, StringComparison.OrdinalIgnoreCase))
            return "The room's payment is a different transaction from the one on this device.";

        // Every saved line must be the transaction's line at the same position (specs/029-multisig-multiple-recipients).
        var saved = pending.AllRecipients;
        var sameLines = saved.Count == room.Recipients.Count
            && saved.Zip(room.Recipients).All(pair =>
                string.Equals(pair.First.Address, pair.Second.Address, StringComparison.Ordinal)
                && pair.First.AmountSatoshis == pair.Second.AmountSatoshis);
        if (!sameLines || room.FeeSat != pending.FeeSatoshis)
            return "This device's saved recipients, amounts or fee do not match the payment itself.";
        return null;
    }

    /// <summary>
    /// Merges the verified signatures in a room copy into <paramref name="pending"/> (FR-010, FR-011). The copy must be for
    /// this very transaction. Each partial signature must come from a co-signer key and verify against its input; others
    /// are discarded and reported in <see cref="RoomMergeResult.Rejected"/>. A key counts as signed once it has a valid
    /// signature on every input.
    /// </summary>
    public async Task<RoomMergeResult> MergeRoomSignaturesAsync(PendingMultisigTransaction pending, string psbtBase64)
    {
        if (pending.Status is PendingTxStatus.Cancelled or PendingTxStatus.Broadcast)
            throw new PsbtImportException("This proposal is closed, so it can't take more signatures.");

        var service = new MultisigService(CurrentNetwork);
        PSBT incoming, existing;
        try { incoming = service.DeserializePSBT((psbtBase64 ?? "").Trim()); }
        catch { throw new PsbtImportException("That is not a valid signed transaction (PSBT)."); }
        try { existing = service.DeserializePSBT(pending.PsbtBase64); }
        catch { throw new PsbtImportException("This proposal's stored transaction could not be read."); }

        var expected = string.IsNullOrEmpty(pending.UnsignedTxId)
            ? existing.GetGlobalTransaction().GetHash().ToString()
            : pending.UnsignedTxId;
        if (!string.Equals(incoming.GetGlobalTransaction().GetHash().ToString(), expected, StringComparison.OrdinalIgnoreCase))
            throw new PsbtImportException("This signature copy is for a different transaction, not this proposal.");

        var config = await LoadConfigAsync(pending.WalletId)
            ?? throw new PsbtImportException("The multisig for this proposal could not be found.");
        var allowed = GetRedeemKeys(config);

        // Keep only signatures that are from co-signer keys and verify; strip the rest before combining.
        var checks = PsbtSignatureCheck.Check(incoming);
        var rejected = checks.Where(c => !c.IsValid || !allowed.Contains(c.PublicKeyHex))
            .Select(c => c.PublicKeyHex.ToLowerInvariant()).Distinct().ToList();
        foreach (var bad in checks.Where(c => !c.IsValid || !allowed.Contains(c.PublicKeyHex)))
        {
            var input = incoming.Inputs[bad.InputIndex];
            var key = input.PartialSigs.Keys.FirstOrDefault(k => string.Equals(k.ToHex(), bad.PublicKeyHex, StringComparison.OrdinalIgnoreCase));
            if (key is not null) input.PartialSigs.Remove(key);
        }

        var combined = service.CombinePSBTs([existing, incoming]);
        var signed = PsbtSignatureCheck.KeysValidOnEveryInput(combined)
            .Where(allowed.Contains)
            .ToList();
        var before = pending.SignedBy.Select(s => s.ToLowerInvariant()).ToHashSet();
        var accepted = signed.Where(k => !before.Contains(k)).ToList();

        pending.PsbtBase64 = MultisigService.SerializePSBT(combined);
        pending.UnsignedTxId = expected;
        foreach (var key in signed)
            if (!pending.SignedBy.Contains(key, StringComparer.OrdinalIgnoreCase))
                pending.SignedBy.Add(key);

        if (pending.HasEnoughSignatures && pending.Status == PendingTxStatus.AwaitingSignatures)
            pending.Status = PendingTxStatus.ReadyToFinalize;

        await SavePendingTransactionAsync(pending);
        return new RoomMergeResult(accepted, rejected.Where(r => !signed.Contains(r)).ToList());
    }

    /// <summary>Records that another participant broadcast this proposal (FR-015). Repeating it with the same id is a no-op.</summary>
    public async Task MarkBroadcastAsync(PendingMultisigTransaction pending, string txId)
    {
        if (string.IsNullOrWhiteSpace(txId))
            throw new ArgumentException("A transaction id is required.", nameof(txId));
        if (pending.Status == PendingTxStatus.Cancelled)
            throw new InvalidOperationException("This proposal was cancelled on this device.");
        if (pending.Status == PendingTxStatus.Broadcast && pending.BroadcastTxId == txId)
            return;

        pending.Status = PendingTxStatus.Broadcast;
        pending.BroadcastTxId = txId.Trim();
        pending.FailureReason = null;
        await SavePendingTransactionAsync(pending);
    }

    /// <summary>Tries again after a failed broadcast. The signed transaction is untouched, so nothing is lost.</summary>
    public async Task<string> RetryBroadcastAsync(PendingMultisigTransaction pending)
    {
        if (pending.Status == PendingTxStatus.Cancelled)
            throw new InvalidOperationException("This proposal was cancelled and can no longer be broadcast from this device.");

        if (pending.Status == PendingTxStatus.Failed)
        {
            pending.Status = pending.HasEnoughSignatures ? PendingTxStatus.ReadyToFinalize : PendingTxStatus.AwaitingSignatures;
            pending.FailureReason = null;
            await SavePendingTransactionAsync(pending);
        }

        return await FinalizeAndBroadcastAsync(pending);
    }

    /// <summary>
    /// Cancels a proposal on this device only. Co-signers who already hold a copy could still sign and broadcast it,
    /// but this device will not. A proposal that is already on the network can't be cancelled.
    /// </summary>
    public async Task<PendingMultisigTransaction> CancelPendingAsync(PendingMultisigTransaction pending)
    {
        if (pending.Status == PendingTxStatus.Broadcast)
            throw new InvalidOperationException("This proposal was already broadcast and can't be cancelled.");

        pending.Status = PendingTxStatus.Cancelled;
        pending.CancelledAt = DateTime.UtcNow;
        await SavePendingTransactionAsync(pending);
        return pending;
    }

    /// <summary>
    /// Removes cancelled proposals from this device's list (all of them, or just <paramref name="pendingId"/>).
    /// Local only: nothing on-chain or on co-signers' devices changes. Returns how many were removed.
    /// </summary>
    public async Task<int> RemoveCancelledAsync(string walletId, string? pendingId = null)
    {
        var all = await GetPendingTransactionsAsync(walletId);
        var removed = all.RemoveAll(p => p.Status == PendingTxStatus.Cancelled && (pendingId is null || p.Id == pendingId));
        if (removed > 0)
        {
            await _storage.SetAsync(MultisigPendingPrefix + walletId, JsonSerializer.Serialize(all));
            RaisePendingChanged(walletId);
        }
        return removed;
    }

    /// <summary>
    /// Orders proposals as a to-do queue: needs this device's signature, ready to broadcast, waiting on others,
    /// failed, cancelled, then already broadcast. Newest first within each group.
    /// </summary>
    public static List<PendingMultisigTransaction> SortPending(IEnumerable<PendingMultisigTransaction> pending, MultisigWalletConfig config)
    {
        int Rank(PendingMultisigTransaction p) => p.Status switch
        {
            PendingTxStatus.AwaitingSignatures when NeedsMySignature(p, config) => 0,
            PendingTxStatus.ReadyToFinalize => 1,
            PendingTxStatus.AwaitingSignatures => 2,
            PendingTxStatus.Failed => 3,
            PendingTxStatus.Cancelled => 4,
            _ => 5,
        };

        return pending.OrderBy(Rank).ThenByDescending(p => p.CreatedAt).ToList();
    }

    /// <summary>Whether this device holds a key of the multisig that has not signed the proposal yet.</summary>
    public static bool NeedsMySignature(PendingMultisigTransaction pending, MultisigWalletConfig config)
    {
        if (pending.Status != PendingTxStatus.AwaitingSignatures) return false;
        var local = config.CoSigners.FirstOrDefault(c => c.IsLocal);
        if (local is null) return false;
        return !pending.SignedBy.Contains(local.PublicKeyHex, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How many proposals are waiting for this device's signature (0 when watch-only).</summary>
    public static int CountNeedingMySignature(IEnumerable<PendingMultisigTransaction> pending, MultisigWalletConfig config) =>
        pending.Count(p => NeedsMySignature(p, config));

    // ─── Ownership: listing and opening ───

    /// <summary>
    /// Lists the multisigs the given wallet owns. Older multisigs with no recorded owner are attributed to this wallet
    /// when <paramref name="ownPublicKeyHex"/> is one of their co-signer keys; nothing is ever deleted by this.
    /// </summary>
    public async Task<List<(string Id, string Name)>> ListWalletsAsync(string ownerWalletId, string? ownPublicKeyHex = null)
    {
        var list = await ListWalletsRawAsync();
        var dirty = false;
        foreach (var entry in list.Where(e => !e.OwnerWalletIds.Contains(ownerWalletId)))
            dirty |= await TryAttributeByKeyAsync(entry, ownerWalletId, ownPublicKeyHex);
        if (dirty) await SaveWalletListAsync(list);

        return list.Where(e => e.OwnerWalletIds.Contains(ownerWalletId)).Select(e => (e.Id, e.Name)).ToList();
    }

    /// <summary>Multisigs no wallet has claimed yet (saved before wallet scoping and not matched by key).</summary>
    public async Task<List<(string Id, string Name)>> ListUnassignedAsync()
    {
        var list = await ListWalletsRawAsync();
        return list.Where(e => e.OwnerWalletIds.Count == 0).Select(e => (e.Id, e.Name)).ToList();
    }

    /// <summary>Gives an unclaimed multisig to <paramref name="ownerWalletId"/>. Claiming twice is fine; claiming one another wallet owns is refused.</summary>
    public async Task ClaimAsync(string walletId, string ownerWalletId)
    {
        var list = await ListWalletsRawAsync();
        var entry = list.FirstOrDefault(e => e.Id == walletId)
            ?? throw new InvalidOperationException("That multisig could not be found.");

        if (entry.OwnerWalletIds.Contains(ownerWalletId)) return;
        if (entry.OwnerWalletIds.Count > 0)
            throw new InvalidOperationException("That multisig already belongs to another wallet.");

        entry.OwnerWalletIds.Add(ownerWalletId);
        await SaveWalletListAsync(list);
        await MirrorOwnersToConfigAsync(entry);
    }

    /// <summary>Says whether the active wallet may open a multisig, running the same key-based attribution as the list.</summary>
    public async Task<MultisigAccessState> GetAccessStateAsync(string walletId, string ownerWalletId, string? ownPublicKeyHex)
    {
        var list = await ListWalletsRawAsync();
        var entry = list.FirstOrDefault(e => e.Id == walletId);
        if (entry is null) return MultisigAccessState.NotFound;

        if (!entry.OwnerWalletIds.Contains(ownerWalletId) && await TryAttributeByKeyAsync(entry, ownerWalletId, ownPublicKeyHex))
            await SaveWalletListAsync(list);

        if (entry.OwnerWalletIds.Contains(ownerWalletId)) return MultisigAccessState.Owned;
        return entry.OwnerWalletIds.Count == 0 ? MultisigAccessState.Unassigned : MultisigAccessState.OtherWallet;
    }

    /// <summary>
    /// Loads a multisig the given wallet owns. Returns null when it does not exist or belongs to someone else
    /// (use <see cref="GetAccessStateAsync"/> to tell which).
    /// </summary>
    public async Task<MultisigWalletConfig?> GetConfigAsync(string walletId, string ownerWalletId, string? ownPublicKeyHex = null)
    {
        if (await GetAccessStateAsync(walletId, ownerWalletId, ownPublicKeyHex) != MultisigAccessState.Owned)
            return null;

        var config = await LoadConfigAsync(walletId);
        if (config is null) return null;

        var entry = (await ListWalletsRawAsync()).FirstOrDefault(e => e.Id == walletId);
        if (entry is not null)
        {
            config.Name = entry.Name;
            config.OwnerWalletIds = [.. entry.OwnerWalletIds];
        }
        ApplyViewerIdentity(config, ownPublicKeyHex);
        return config;
    }

    /// <summary>The label older versions gave the creator's own key. It means "me" only to the wallet that made it.</summary>
    private const string LegacyOwnKeyLabel = "You (This Wallet)";

    /// <summary>
    /// Marks which co-signer is the viewing wallet's own key. "You" is a property of who is looking, not of the wallet
    /// that created the multisig, so two wallets on one device that both hold a key each see their own key as theirs.
    /// This changes only the copy being returned, never what is stored. With no public key (locked or watch-only) no
    /// co-signer is shown as local.
    /// </summary>
    private static void ApplyViewerIdentity(MultisigWalletConfig config, string? ownPublicKeyHex)
    {
        var own = string.IsNullOrWhiteSpace(ownPublicKeyHex) ? null : ownPublicKeyHex.Trim();
        for (var i = 0; i < config.CoSigners.Count; i++)
        {
            var signer = config.CoSigners[i];
            signer.IsLocal = own is not null && string.Equals(signer.PublicKeyHex, own, StringComparison.OrdinalIgnoreCase);

            // The creator's placeholder would read as "you" to somebody else; show a neutral name instead.
            if (!signer.IsLocal && signer.Name == LegacyOwnKeyLabel)
                signer.Name = $"Signer {i + 1}";
        }
        config.OwnKeyIndex = config.CoSigners.FindIndex(c => c.IsLocal);
    }

    private async Task<MultisigWalletConfig?> LoadConfigAsync(string walletId)
    {
        var json = await _storage.GetAsync(MultisigConfigPrefix + walletId);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<MultisigWalletConfig>(json);
    }

    private async Task<bool> TryAttributeByKeyAsync(MultisigWalletEntry entry, string ownerWalletId, string? ownPublicKeyHex)
    {
        if (string.IsNullOrWhiteSpace(ownPublicKeyHex)) return false;

        var config = await LoadConfigAsync(entry.Id);
        if (config is null) return false;

        var holdsKey = config.CoSigners.Any(c => string.Equals(c.PublicKeyHex, ownPublicKeyHex.Trim(), StringComparison.OrdinalIgnoreCase))
                       || FindOwnKeyIndex(config.RedeemScriptHex, ownPublicKeyHex) >= 0;
        if (!holdsKey) return false;

        entry.OwnerWalletIds.Add(ownerWalletId);
        config.OwnerWalletIds = [.. entry.OwnerWalletIds];
        await SaveConfigAsync(config);
        return true;
    }

    private async Task MirrorOwnersToConfigAsync(MultisigWalletEntry entry)
    {
        var config = await LoadConfigAsync(entry.Id);
        if (config is null) return;
        config.OwnerWalletIds = [.. entry.OwnerWalletIds];
        await SaveConfigAsync(config);
    }

    /// <summary>
    /// Lists all pending transactions for a multisig wallet. Proposals saved before <c>UnsignedTxId</c> existed get it
    /// filled in from their stored PSBT (and saved), so signature copies can be checked against them.
    /// </summary>
    public async Task<List<PendingMultisigTransaction>> GetPendingTransactionsAsync(string walletId)
    {
        var json = await _storage.GetAsync(MultisigPendingPrefix + walletId);
        if (string.IsNullOrEmpty(json)) return [];
        var list = JsonSerializer.Deserialize<List<PendingMultisigTransaction>>(json) ?? [];

        var changed = false;
        var service = new MultisigService(CurrentNetwork);
        foreach (var p in list.Where(p => string.IsNullOrEmpty(p.UnsignedTxId)))
        {
            try
            {
                p.UnsignedTxId = service.DeserializePSBT(p.PsbtBase64).GetGlobalTransaction().GetHash().ToString();
                changed = true;
            }
            catch { /* an unreadable PSBT stays without an id; importing into it is refused later */ }
        }

        if (changed)
            await _storage.SetAsync(MultisigPendingPrefix + walletId, JsonSerializer.Serialize(list));
        return list;
    }

    // ─── Activity ───

    /// <summary>
    /// One page of activity: chain transactions (<paramref name="skip"/>/<paramref name="take"/>) merged with local
    /// proposals. If the chain can't be reached, local proposals are still returned and the page is marked partial.
    /// Proposals are included only on the first page, so "Load more" doesn't repeat them.
    /// </summary>
    public async Task<ActivityPage> GetActivityAsync(MultisigWalletConfig config, int skip = 0, int take = 25)
    {
        var proposals = skip == 0 ? await GetPendingTransactionsAsync(config.WalletId) : [];

        List<TransactionInfo> chain;
        try
        {
            chain = await _blockchain.GetAddressTransactionsAsync(config.Address, skip, take);
        }
        catch (Exception)
        {
            return new ActivityPage
            {
                Entries = MultisigActivityBuilder.Build(config.Address, [], proposals),
                IsPartial = true,
                HasMore = false,
            };
        }

        return new ActivityPage
        {
            Entries = MultisigActivityBuilder.Build(config.Address, chain, proposals),
            IsPartial = false,
            HasMore = chain.Count >= take,
        };
    }

    // ─── Rename, backup, delete ───

    /// <summary>Renames a multisig. The name is a local label only; the script and keys never change.</summary>
    public async Task<RenameResult> RenameWalletAsync(string walletId, string name)
    {
        if (!TryNormalizeName(name, out var trimmed, out var error))
            return new RenameResult(false, error, false, null);

        var list = await ListWalletsRawAsync();
        var entry = list.FirstOrDefault(e => e.Id == walletId);
        if (entry is null)
            return new RenameResult(false, "That multisig could not be found.", false, null);

        var duplicate = list.Any(e => e.Id != walletId
            && string.Equals(e.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)
            && e.OwnerWalletIds.Intersect(entry.OwnerWalletIds).Any());

        entry.Name = trimmed;
        await SaveWalletListAsync(list);

        var config = await LoadConfigAsync(walletId);
        if (config is not null)
        {
            config.Name = trimmed;
            await SaveConfigAsync(config);
        }
        return new RenameResult(true, null, duplicate, trimmed);
    }

    /// <summary>Renames a co-signer's label. The key is never editable.</summary>
    public async Task<RenameResult> RenameCoSignerAsync(string walletId, string publicKeyHex, string name)
    {
        if (!TryNormalizeName(name, out var trimmed, out var error))
            return new RenameResult(false, error, false, null);

        var config = await LoadConfigAsync(walletId);
        var signer = config?.CoSigners.FirstOrDefault(c => string.Equals(c.PublicKeyHex, publicKeyHex, StringComparison.OrdinalIgnoreCase));
        if (config is null || signer is null)
            return new RenameResult(false, "That co-signer could not be found.", false, null);

        var duplicate = config.CoSigners.Any(c => !ReferenceEquals(c, signer)
            && string.Equals(c.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        signer.Name = trimmed;
        await SaveConfigAsync(config);
        return new RenameResult(true, null, duplicate, trimmed);
    }

    /// <summary>Records that the redeem script was downloaded or copied as a backup.</summary>
    public async Task MarkBackedUpAsync(string walletId)
    {
        var config = await LoadConfigAsync(walletId);
        if (config is null) return;
        config.BackupAt = DateTime.UtcNow;
        await SaveConfigAsync(config);
    }

    /// <summary>
    /// Removes a multisig from the given wallet. Its data (config and proposals) is deleted only once no wallet
    /// owns it any more, so another wallet that co-signs it keeps it.
    /// </summary>
    public async Task DeleteWalletAsync(string walletId, string ownerWalletId)
    {
        var list = await ListWalletsRawAsync();
        var entry = list.FirstOrDefault(e => e.Id == walletId);
        if (entry is null) return;

        entry.OwnerWalletIds.Remove(ownerWalletId);
        if (entry.OwnerWalletIds.Count > 0)
        {
            await SaveWalletListAsync(list);
            await MirrorOwnersToConfigAsync(entry);
            return;
        }

        await _storage.RemoveAsync(MultisigConfigPrefix + walletId);
        await _storage.RemoveAsync(MultisigPendingPrefix + walletId);
        await RemoveFromWalletListAsync(walletId);
        RaisePendingChanged(walletId);
    }

    private static bool TryNormalizeName(string? name, out string trimmed, out string? error)
    {
        trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = "Enter a name.";
            return false;
        }
        if (trimmed.Length > MaxNameLength)
        {
            error = $"Names can be at most {MaxNameLength} characters.";
            return false;
        }
        error = null;
        return true;
    }

    // ─── Drafts (setup saved for later, e.g. while waiting for co-signers' keys) ───

    /// <summary>
    /// Lists the given wallet's saved drafts, most recently updated first. Older drafts with no owner are attributed
    /// to this wallet when <paramref name="ownPublicKeyHex"/> matches the key they were started with.
    /// </summary>
    public async Task<List<MultisigDraft>> ListDraftsAsync(string ownerWalletId, string? ownPublicKeyHex = null)
    {
        var all = await ListDraftsRawAsync();
        var dirty = false;
        if (!string.IsNullOrWhiteSpace(ownPublicKeyHex))
        {
            foreach (var d in all.Where(d => string.IsNullOrEmpty(d.OwnerWalletId)
                         && string.Equals(d.OwnPublicKeyHex, ownPublicKeyHex.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                d.OwnerWalletId = ownerWalletId;
                dirty = true;
            }
        }
        if (dirty) await _storage.SetAsync(MultisigDraftsKey, JsonSerializer.Serialize(all));

        return all.Where(d => d.OwnerWalletId == ownerWalletId).OrderByDescending(d => d.UpdatedAt).ToList();
    }

    public async Task<MultisigDraft?> GetDraftAsync(string draftId, string ownerWalletId, string? ownPublicKeyHex = null)
    {
        var drafts = await ListDraftsAsync(ownerWalletId, ownPublicKeyHex);
        return drafts.FirstOrDefault(d => d.Id == draftId);
    }

    /// <summary>Saves a new draft (blank Id gets a new one) or replaces the existing draft with the same Id.</summary>
    public async Task<MultisigDraft> SaveDraftAsync(MultisigDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new ArgumentException("A draft needs a wallet name.", nameof(draft));

        var drafts = await ListDraftsRawAsync();
        if (string.IsNullOrEmpty(draft.Id))
            draft.Id = Guid.NewGuid().ToString("N");
        draft.UpdatedAt = DateTime.UtcNow;

        drafts.RemoveAll(d => d.Id == draft.Id);
        drafts.Add(draft);
        await _storage.SetAsync(MultisigDraftsKey, JsonSerializer.Serialize(drafts));
        return draft;
    }

    /// <summary>Renames a draft without touching the rest of its progress.</summary>
    public async Task<RenameResult> RenameDraftAsync(string draftId, string name)
    {
        if (!TryNormalizeName(name, out var trimmed, out var error))
            return new RenameResult(false, error, false, null);

        var drafts = await ListDraftsRawAsync();
        var draft = drafts.FirstOrDefault(d => d.Id == draftId);
        if (draft is null)
            return new RenameResult(false, "That draft could not be found.", false, null);

        var duplicate = drafts.Any(d => d.Id != draftId && d.OwnerWalletId == draft.OwnerWalletId
            && string.Equals(d.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        draft.Name = trimmed;
        draft.UpdatedAt = DateTime.UtcNow;
        await _storage.SetAsync(MultisigDraftsKey, JsonSerializer.Serialize(drafts));
        return new RenameResult(true, null, duplicate, trimmed);
    }

    public async Task DeleteDraftAsync(string draftId)
    {
        var drafts = await ListDraftsRawAsync();
        if (drafts.RemoveAll(d => d.Id == draftId) > 0)
            await _storage.SetAsync(MultisigDraftsKey, JsonSerializer.Serialize(drafts));
    }

    private async Task<List<MultisigDraft>> ListDraftsRawAsync()
    {
        var json = await _storage.GetAsync(MultisigDraftsKey);
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<MultisigDraft>>(json) ?? [];
        }
        catch (JsonException) { return []; }
    }

    // ─── Private persistence helpers ───

    private async Task SaveConfigAsync(MultisigWalletConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        await _storage.SetAsync(MultisigConfigPrefix + config.WalletId, json);
    }

    private async Task SavePendingTransactionAsync(PendingMultisigTransaction pending)
    {
        pending.UpdatedAt = DateTime.UtcNow;
        var all = await GetPendingTransactionsAsync(pending.WalletId);
        var existing = all.FindIndex(p => p.Id == pending.Id);
        if (existing >= 0)
            all[existing] = pending;
        else
            all.Add(pending);

        var json = JsonSerializer.Serialize(all);
        await _storage.SetAsync(MultisigPendingPrefix + pending.WalletId, json);
        RaisePendingChanged(pending.WalletId);
    }

    private async Task AddToWalletListAsync(string walletId, string name, string? ownerWalletId)
    {
        var list = await ListWalletsRawAsync();
        list.Add(new MultisigWalletEntry
        {
            Id = walletId,
            Name = name,
            OwnerWalletIds = string.IsNullOrEmpty(ownerWalletId) ? [] : [ownerWalletId],
        });
        await SaveWalletListAsync(list);
    }

    private async Task RemoveFromWalletListAsync(string walletId)
    {
        var list = await ListWalletsRawAsync();
        list.RemoveAll(e => e.Id == walletId);
        await SaveWalletListAsync(list);
    }

    private Task SaveWalletListAsync(List<MultisigWalletEntry> list) =>
        _storage.SetAsync(MultisigListKey, JsonSerializer.Serialize(list));

    private async Task<List<MultisigWalletEntry>> ListWalletsRawAsync()
    {
        var json = await _storage.GetAsync(MultisigListKey);
        if (string.IsNullOrEmpty(json)) return [];
        return JsonSerializer.Deserialize<List<MultisigWalletEntry>>(json) ?? [];
    }

    private class MultisigWalletEntry
    {
        public required string Id { get; init; }
        public required string Name { get; set; }
        public List<string> OwnerWalletIds { get; set; } = [];
    }
}
