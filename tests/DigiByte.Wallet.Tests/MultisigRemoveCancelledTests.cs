using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>Cancelled proposals can be removed from this device, and only cancelled ones (spec 020).</summary>
public class MultisigRemoveCancelledTests
{
    private const string Wallet = "ms";

    private static PendingMultisigTransaction Item(string id, PendingTxStatus status, string wallet = Wallet) => new()
    {
        Id = id,
        WalletId = wallet,
        PsbtBase64 = "x",
        DestinationAddress = "d",
        AmountSatoshis = 1,
        FeeSatoshis = 1,
        RequiredSignatures = 2,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Status = status,
        BroadcastTxId = status == PendingTxStatus.Broadcast ? "tx-" + id : null,
        CancelledAt = status == PendingTxStatus.Cancelled ? new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) : null,
    };

    private static MultisigWalletService CreateService(MultisigFakeStorage storage, MultisigFakeChain? chain = null) =>
        new(storage, chain ?? new MultisigFakeChain(), new MultisigFakeCrypto());

    private static MultisigFakeStorage Seed(string wallet, params PendingMultisigTransaction[] items)
    {
        var storage = new MultisigFakeStorage();
        SeedInto(storage, wallet, items);
        return storage;
    }

    private static void SeedInto(MultisigFakeStorage storage, string wallet, params PendingMultisigTransaction[] items) =>
        storage.Put("multisig_pending_" + wallet, JsonSerializer.Serialize(items.ToList()));

    private static async Task<List<string>> IdsAsync(MultisigFakeStorage storage, string wallet = Wallet) =>
        (await CreateService(storage).GetPendingTransactionsAsync(wallet)).Select(p => p.Id).ToList();

    // ─── US1: remove one ───

    [Fact]
    public async Task RemoveById_DeletesOnlyThatCancelledProposal()
    {
        var storage = Seed(Wallet,
            Item("c1", PendingTxStatus.Cancelled),
            Item("c2", PendingTxStatus.Cancelled),
            Item("open", PendingTxStatus.AwaitingSignatures));

        var removed = await CreateService(storage).RemoveCancelledAsync(Wallet, "c1");

        Assert.Equal(1, removed);
        Assert.Equal(["c2", "open"], await IdsAsync(storage));
    }

    [Fact]
    public async Task Removal_PersistsAcrossAFreshService()
    {
        var storage = Seed(Wallet, Item("c1", PendingTxStatus.Cancelled));

        await CreateService(storage).RemoveCancelledAsync(Wallet, "c1");

        Assert.Empty(await IdsAsync(storage));
    }

    [Fact]
    public async Task RemoveById_WhenItIsAlreadyGone_RemovesNothing_AndDoesNotThrow()
    {
        var storage = Seed(Wallet, Item("open", PendingTxStatus.AwaitingSignatures));

        var removed = await CreateService(storage).RemoveCancelledAsync(Wallet, "missing");

        Assert.Equal(0, removed);
        Assert.Equal(["open"], await IdsAsync(storage));
    }

    [Fact]
    public async Task RemoveCancelled_WhenTheWalletHasNoProposals_ReturnsZero()
    {
        var removed = await CreateService(new MultisigFakeStorage()).RemoveCancelledAsync(Wallet);

        Assert.Equal(0, removed);
    }

    [Theory]
    [InlineData(PendingTxStatus.AwaitingSignatures)]
    [InlineData(PendingTxStatus.ReadyToFinalize)]
    [InlineData(PendingTxStatus.Failed)]
    [InlineData(PendingTxStatus.Broadcast)]
    public async Task RemoveById_NeverRemovesANonCancelledProposal_EvenWhenItsIdIsPassed(PendingTxStatus status)
    {
        var storage = Seed(Wallet, Item("p1", status));

        var removed = await CreateService(storage).RemoveCancelledAsync(Wallet, "p1");

        Assert.Equal(0, removed);
        var kept = Assert.Single(await CreateService(storage).GetPendingTransactionsAsync(Wallet));
        Assert.Equal(status, kept.Status);
    }

    [Fact]
    public async Task Removal_DoesNotTouchAnotherWalletsList()
    {
        var storage = new MultisigFakeStorage();
        SeedInto(storage, Wallet, Item("c1", PendingTxStatus.Cancelled));
        SeedInto(storage, "other", Item("c1", PendingTxStatus.Cancelled, "other"));

        await CreateService(storage).RemoveCancelledAsync(Wallet);

        Assert.Empty(await IdsAsync(storage));
        Assert.Equal(["c1"], await IdsAsync(storage, "other"));
    }

    [Fact]
    public async Task RemovedCancelledProposal_LeavesActivity_ButChainTransactionsStay()
    {
        var chain = new MultisigFakeChain();
        var storage = new MultisigFakeStorage();
        var sut = CreateService(storage, chain);
        var config = await sut.CreateMultisigWalletAsync("Activity", 2,
            [new CoSigner { Name = "a", PublicKeyHex = new Key().PubKey.ToHex() }, new CoSigner { Name = "b", PublicKeyHex = new Key().PubKey.ToHex() }],
            ownerWalletId: "wallet-a");
        chain.Transactions.Add(new TransactionInfo
        {
            TxId = "rx1",
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Confirmations = 5,
            FeeSatoshis = 0,
            Inputs = [new TxInput { Address = "dgb1qsomeone", AmountSatoshis = 10_000 }],
            Outputs = [new TxOutput { Address = config.Address, AmountSatoshis = 9_000, Index = 0 }],
        });
        SeedInto(storage, config.WalletId, Item("c1", PendingTxStatus.Cancelled, config.WalletId));

        var before = await sut.GetActivityAsync(config);
        Assert.Contains(before.Entries, e => e.Kind == ActivityKind.Cancelled);

        await sut.RemoveCancelledAsync(config.WalletId, "c1");

        var after = await sut.GetActivityAsync(config);
        Assert.DoesNotContain(after.Entries, e => e.Kind == ActivityKind.Cancelled);
        Assert.Contains(after.Entries, e => e.TxId == "rx1");
    }

    // ─── US2: clear all ───

    [Fact]
    public async Task ClearAll_RemovesEveryCancelledProposal_AndReturnsTheCount()
    {
        var storage = Seed(Wallet,
            Item("c1", PendingTxStatus.Cancelled),
            Item("c2", PendingTxStatus.Cancelled),
            Item("c3", PendingTxStatus.Cancelled),
            Item("open", PendingTxStatus.AwaitingSignatures));

        var removed = await CreateService(storage).RemoveCancelledAsync(Wallet);

        Assert.Equal(3, removed);
        Assert.Equal(["open"], await IdsAsync(storage));
    }

    [Fact]
    public async Task ClearAll_LeavesOpenReadyFailedAndBroadcastProposalsUnchanged()
    {
        var storage = Seed(Wallet,
            Item("c1", PendingTxStatus.Cancelled),
            Item("open", PendingTxStatus.AwaitingSignatures),
            Item("ready", PendingTxStatus.ReadyToFinalize),
            Item("failed", PendingTxStatus.Failed),
            Item("sent", PendingTxStatus.Broadcast));

        await CreateService(storage).RemoveCancelledAsync(Wallet);

        var kept = (await CreateService(storage).GetPendingTransactionsAsync(Wallet)).ToDictionary(p => p.Id, p => p.Status);
        Assert.Equal(4, kept.Count);
        Assert.Equal(PendingTxStatus.AwaitingSignatures, kept["open"]);
        Assert.Equal(PendingTxStatus.ReadyToFinalize, kept["ready"]);
        Assert.Equal(PendingTxStatus.Failed, kept["failed"]);
        Assert.Equal(PendingTxStatus.Broadcast, kept["sent"]);
    }
}
