using System.Text.Json;
using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Signing, importing co-signer copies, cancelling, failing and retrying proposals, and the queue order (spec 016, US7).
/// </summary>
public class MultisigPendingImportTests
{
    private const string Owner = "wallet-a";

    private sealed class Rig
    {
        public required MultisigWalletService Sut { get; init; }
        public required MultisigFakeChain Chain { get; init; }
        public required MultisigFakeStorage Storage { get; init; }
        public required MultisigWalletConfig Config { get; init; }
        public required Key[] Keys { get; init; }     // 2-of-3; Keys[0] is this device's key
    }

    private static async Task<Rig> ArrangeAsync(long utxoAmount = 1_000_000_000, int utxoCount = 1)
    {
        var keys = new[] { new Key(), new Key(), new Key() };
        var chain = new MultisigFakeChain(utxos: Enumerable.Range(0, utxoCount).Select(_ => new UtxoInfo
        {
            TxId = RandomUtils.GetUInt256().ToString(),
            OutputIndex = 0,
            AmountSatoshis = utxoAmount,
            ScriptPubKey = "",
            Confirmations = 10,
        }).ToList());
        var storage = new MultisigFakeStorage();
        var sut = new MultisigWalletService(storage, chain, new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync("Pending", 2,
            keys.Select((k, i) => new CoSigner { Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = i == 0 }).ToList(),
            ownerWalletId: Owner);
        return new Rig { Sut = sut, Chain = chain, Storage = storage, Config = config, Keys = keys };
    }

    private static string Destination() =>
        new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString();

    /// <summary>What a co-signer would send back: the proposal signed with their key on their own device.</summary>
    private static string CoSignerCopy(string psbtBase64, Key key)
    {
        var service = new MultisigService(DigiByteNetwork.Mainnet);
        var psbt = service.DeserializePSBT(psbtBase64);
        service.SignPSBT(psbt, key);
        return MultisigService.SerializePSBT(psbt);
    }

    // ─── Importing signature copies ───

    [Fact]
    public async Task AValidCoSignerCopy_Merges_UpdatesSignedBy_AndReachesReadyAtTheThreshold()
    {
        var rig = await ArrangeAsync();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        await rig.Sut.SignPendingTransactionAsync(pending, rig.Keys[0]);
        Assert.Equal(PendingTxStatus.AwaitingSignatures, pending.Status);

        await rig.Sut.ImportSignedPSBTAsync(pending, CoSignerCopy(pending.PsbtBase64, rig.Keys[1]));

        Assert.Equal(2, pending.SignatureCount);
        Assert.Contains(rig.Keys[1].PubKey.ToHex(), pending.SignedBy);
        Assert.Equal(PendingTxStatus.ReadyToFinalize, pending.Status);
    }

    [Fact]
    public async Task ACopyForADifferentTransaction_IsRejected_WithASpecificReason()
    {
        var rig = await ArrangeAsync(utxoCount: 2);
        var first = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        var second = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 2m, 10);

        var ex = await Assert.ThrowsAsync<PsbtImportException>(
            () => rig.Sut.ImportSignedPSBTAsync(first, CoSignerCopy(second.PsbtBase64, rig.Keys[1])));

        Assert.Contains("different transaction", ex.Message);
        Assert.Empty(first.SignedBy);
    }

    [Fact]
    public async Task ACopySignedByAKeyOutsideTheMultisig_IsRejected()
    {
        var rig = await ArrangeAsync();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);

        var forged = PSBT.Parse(pending.PsbtBase64, DigiByteNetwork.Mainnet);
        var outsider = new Key();
        forged.Inputs[0].PartialSigs.Add(outsider.PubKey, new TransactionSignature(outsider.Sign(uint256.One), SigHash.All));

        var ex = await Assert.ThrowsAsync<PsbtImportException>(
            () => rig.Sut.ImportSignedPSBTAsync(pending, forged.ToBase64()));

        Assert.Contains("not a co-signer", ex.Message);
        Assert.Empty(pending.SignedBy);
    }

    [Theory]
    [InlineData("not base64 at all !!!")]
    [InlineData("")]
    [InlineData("cHNidP8=")]
    public async Task MalformedCopies_AreRejected_WithAUserFacingMessage(string garbage)
    {
        var rig = await ArrangeAsync();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);

        var ex = await Assert.ThrowsAsync<PsbtImportException>(() => rig.Sut.ImportSignedPSBTAsync(pending, garbage));

        Assert.Contains("not a valid", ex.Message);
    }

    [Fact]
    public async Task AClosedProposal_TakesNoMoreSignatures()
    {
        var rig = await ArrangeAsync();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        await rig.Sut.CancelPendingAsync(pending);

        await Assert.ThrowsAsync<PsbtImportException>(
            () => rig.Sut.ImportSignedPSBTAsync(pending, CoSignerCopy(pending.PsbtBase64, rig.Keys[1])));
    }

    // ─── Proposals saved before UnsignedTxId existed ───

    [Fact]
    public async Task ALegacyProposalWithoutUnsignedTxId_IsBackfilled_Persisted_AndStillAcceptsSignatures()
    {
        var rig = await ArrangeAsync();
        var created = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        var expectedId = created.UnsignedTxId;

        // Rewrite storage the way the OLD model saved it: no UnsignedTxId.
        var key = $"multisig_pending_{rig.Config.WalletId}";
        var legacy = JsonSerializer.Deserialize<List<PendingMultisigTransaction>>(rig.Storage.Peek(key)!)!;
        legacy[0].UnsignedTxId = "";
        rig.Storage.Put(key, JsonSerializer.Serialize(legacy));

        var loaded = (await rig.Sut.GetPendingTransactionsAsync(rig.Config.WalletId)).Single();
        Assert.Equal(expectedId, loaded.UnsignedTxId);
        Assert.Contains(expectedId, rig.Storage.Peek(key)!);   // saved back

        await rig.Sut.SignPendingTransactionAsync(loaded, rig.Keys[0]);
        await rig.Sut.ImportSignedPSBTAsync(loaded, CoSignerCopy(loaded.PsbtBase64, rig.Keys[1]));
        Assert.Equal(PendingTxStatus.ReadyToFinalize, loaded.Status);
    }

    // ─── Cancel, broadcast, failure, retry ───

    private static async Task<PendingMultisigTransaction> ReadyProposalAsync(Rig rig)
    {
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        await rig.Sut.SignPendingTransactionAsync(pending, rig.Keys[0]);
        await rig.Sut.ImportSignedPSBTAsync(pending, CoSignerCopy(pending.PsbtBase64, rig.Keys[1]));
        return pending;
    }

    [Fact]
    public async Task Cancel_SetsCancelled_AndTheProposalIsNeverBroadcastFromThisDevice()
    {
        var rig = await ArrangeAsync();
        var pending = await ReadyProposalAsync(rig);

        await rig.Sut.CancelPendingAsync(pending);

        Assert.Equal(PendingTxStatus.Cancelled, pending.Status);
        Assert.NotNull(pending.CancelledAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.FinalizeAndBroadcastAsync(pending));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.RetryBroadcastAsync(pending));
        Assert.Equal(0, rig.Chain.BroadcastCalls);
    }

    [Fact]
    public async Task ABroadcastProposal_CannotBeCancelled()
    {
        var rig = await ArrangeAsync();
        var pending = await ReadyProposalAsync(rig);
        await rig.Sut.FinalizeAndBroadcastAsync(pending);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Sut.CancelPendingAsync(pending));
    }

    [Fact]
    public async Task Broadcast_Succeeds_AndRecordsTheTxId()
    {
        var rig = await ArrangeAsync();
        rig.Chain.BroadcastResult = "abc123";
        var pending = await ReadyProposalAsync(rig);

        var txId = await rig.Sut.FinalizeAndBroadcastAsync(pending);

        Assert.Equal("abc123", txId);
        Assert.Equal(PendingTxStatus.Broadcast, pending.Status);
        Assert.Equal("abc123", pending.BroadcastTxId);
    }

    [Fact]
    public async Task AFailedBroadcast_SetsFailedWithAReason_AndRetryRecovers()
    {
        var rig = await ArrangeAsync();
        var pending = await ReadyProposalAsync(rig);
        rig.Chain.BroadcastFailure = new HttpRequestException("transaction rejected by the network");

        await Assert.ThrowsAsync<HttpRequestException>(() => rig.Sut.FinalizeAndBroadcastAsync(pending));

        Assert.Equal(PendingTxStatus.Failed, pending.Status);
        Assert.Equal("transaction rejected by the network", pending.FailureReason);
        Assert.False(string.IsNullOrEmpty(pending.PsbtBase64));   // nothing is lost: the signed transaction is intact

        rig.Chain.BroadcastFailure = null;
        rig.Chain.BroadcastResult = "recovered";
        var txId = await rig.Sut.RetryBroadcastAsync(pending);

        Assert.Equal("recovered", txId);
        Assert.Equal(PendingTxStatus.Broadcast, pending.Status);
        Assert.Null(pending.FailureReason);
    }

    [Fact]
    public async Task TwoConcurrentBroadcasts_OfTheSameProposal_BroadcastOnce()
    {
        var rig = await ArrangeAsync();
        rig.Chain.BroadcastDelay = TimeSpan.FromMilliseconds(150);
        var pending = await ReadyProposalAsync(rig);

        static async Task<bool> Try(Func<Task<string>> call)
        {
            try { await call(); return true; }
            catch (InvalidOperationException) { return false; }
        }

        var results = await Task.WhenAll(
            Try(() => rig.Sut.FinalizeAndBroadcastAsync(pending)),
            Try(() => rig.Sut.FinalizeAndBroadcastAsync(pending)));

        Assert.Equal(1, rig.Chain.BroadcastCalls);
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(PendingTxStatus.Broadcast, pending.Status);
    }

    // ─── Queue order and "needs my signature" ───

    private static PendingMultisigTransaction Item(string id, PendingTxStatus status, DateTime created, params string[] signedBy) => new()
    {
        Id = id,
        WalletId = "ms",
        PsbtBase64 = "x",
        DestinationAddress = "d",
        AmountSatoshis = 1,
        FeeSatoshis = 1,
        RequiredSignatures = 2,
        CreatedAt = created,
        Status = status,
        SignedBy = signedBy.ToList(),
    };

    private static MultisigWalletConfig ConfigWithLocal(string localKey, params string[] others) => new()
    {
        WalletId = "ms",
        RequiredSignatures = 2,
        TotalSigners = 1 + others.Length,
        CoSigners = [new CoSigner { Name = "me", PublicKeyHex = localKey, IsLocal = true }, .. others.Select(o => new CoSigner { Name = "x", PublicKeyHex = o })],
        RedeemScriptHex = "00",
        Address = "addr",
    };

    [Fact]
    public void Queue_PutsNeedsMySignatureFirst_ThenReady_ThenWaiting_ThenFailed_ThenCancelled()
    {
        var config = ConfigWithLocal("02me", "02bob");
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = new[]
        {
            Item("cancelled", PendingTxStatus.Cancelled, t.AddHours(5)),
            Item("failed", PendingTxStatus.Failed, t.AddHours(4)),
            Item("waiting", PendingTxStatus.AwaitingSignatures, t.AddHours(3), "02me"),      // I already signed
            Item("ready", PendingTxStatus.ReadyToFinalize, t.AddHours(2), "02me", "02bob"),
            Item("mine-old", PendingTxStatus.AwaitingSignatures, t.AddHours(0), "02bob"),
            Item("mine-new", PendingTxStatus.AwaitingSignatures, t.AddHours(1), "02bob"),
        };

        var sorted = MultisigWalletService.SortPending(items, config);

        Assert.Equal(["mine-new", "mine-old", "ready", "waiting", "failed", "cancelled"], sorted.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void NeedsMySignature_IsFalseForWatchOnly_AndOnceIHaveSigned()
    {
        var t = DateTime.UtcNow;
        var mine = ConfigWithLocal("02me", "02bob");
        var watchOnly = new MultisigWalletConfig
        {
            WalletId = "ms", RequiredSignatures = 2, TotalSigners = 2, RedeemScriptHex = "00", Address = "addr",
            CoSigners = [new CoSigner { Name = "a", PublicKeyHex = "02aa" }, new CoSigner { Name = "b", PublicKeyHex = "02bb" }],
        };
        var unsigned = Item("u", PendingTxStatus.AwaitingSignatures, t);
        var signed = Item("s", PendingTxStatus.AwaitingSignatures, t, "02ME");   // case-insensitive

        Assert.True(MultisigWalletService.NeedsMySignature(unsigned, mine));
        Assert.False(MultisigWalletService.NeedsMySignature(signed, mine));
        Assert.False(MultisigWalletService.NeedsMySignature(unsigned, watchOnly));
        Assert.Equal(1, MultisigWalletService.CountNeedingMySignature([unsigned, signed], mine));
        Assert.Equal(0, MultisigWalletService.CountNeedingMySignature([unsigned, signed], watchOnly));
    }

    // ─── Backup status ───

    [Fact]
    public async Task MarkBackedUp_SetsTheDate_Persists_AndUpdatesOnASecondBackup()
    {
        var rig = await ArrangeAsync();
        Assert.Null((await rig.Sut.GetConfigAsync(rig.Config.WalletId, Owner))!.BackupAt);

        await rig.Sut.MarkBackedUpAsync(rig.Config.WalletId);
        var first = (await rig.Sut.GetConfigAsync(rig.Config.WalletId, Owner))!.BackupAt;
        Assert.NotNull(first);
        Assert.InRange((DateTime.UtcNow - first!.Value).TotalSeconds, -5, 30);

        await Task.Delay(20);
        await rig.Sut.MarkBackedUpAsync(rig.Config.WalletId);
        var second = (await rig.Sut.GetConfigAsync(rig.Config.WalletId, Owner))!.BackupAt;
        Assert.True(second > first);
    }
}
