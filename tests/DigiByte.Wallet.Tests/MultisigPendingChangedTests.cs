using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The service says when a shared wallet's saved payments change, so the counts on the menu and the home shortcut
/// can follow without reloading (spec 026, FR-010a).
/// </summary>
public class MultisigPendingChangedTests
{
    private const string Wallet = "ms";

    private static PendingMultisigTransaction Item(string id, PendingTxStatus status) => new()
    {
        Id = id,
        WalletId = Wallet,
        PsbtBase64 = "x",
        DestinationAddress = "d",
        AmountSatoshis = 1,
        FeeSatoshis = 1,
        RequiredSignatures = 2,
        Status = status,
        // Set, so loading the list does not try to read the placeholder PSBT.
        UnsignedTxId = "tx-" + id,
    };

    private static (MultisigWalletService Service, List<string> Raised) Create(params PendingMultisigTransaction[] items)
    {
        var storage = new MultisigFakeStorage();
        if (items.Length > 0)
            storage.Put("multisig_pending_" + Wallet, JsonSerializer.Serialize(items.ToList()));
        var service = new MultisigWalletService(storage, new MultisigFakeChain(), new MultisigFakeCrypto());
        var raised = new List<string>();
        service.PendingChanged += raised.Add;
        return (service, raised);
    }

    [Fact]
    public async Task Saving_a_payment_raises_it_once_with_the_shared_wallet_id()
    {
        var item = Item("p1", PendingTxStatus.AwaitingSignatures);
        var (service, raised) = Create(item);

        await service.CancelPendingAsync(item);

        Assert.Equal([Wallet], raised);
    }

    [Fact]
    public async Task Marking_a_payment_as_broadcast_raises_it()
    {
        var item = Item("p1", PendingTxStatus.ReadyToFinalize);
        var (service, raised) = Create(item);

        await service.MarkBroadcastAsync(item, "txid");

        Assert.Equal([Wallet], raised);
    }

    [Fact]
    public async Task Removing_cancelled_payments_raises_it_once()
    {
        var (service, raised) = Create(Item("c1", PendingTxStatus.Cancelled), Item("c2", PendingTxStatus.Cancelled));

        var removed = await service.RemoveCancelledAsync(Wallet);

        Assert.Equal(2, removed);
        Assert.Equal([Wallet], raised);
    }

    [Fact]
    public async Task Removing_when_nothing_is_cancelled_raises_nothing()
    {
        var (service, raised) = Create(Item("p1", PendingTxStatus.AwaitingSignatures));

        var removed = await service.RemoveCancelledAsync(Wallet);

        Assert.Equal(0, removed);
        Assert.Empty(raised);
    }

    [Fact]
    public async Task Reading_the_list_raises_nothing()
    {
        var (service, raised) = Create(Item("p1", PendingTxStatus.AwaitingSignatures));

        await service.GetPendingTransactionsAsync(Wallet);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Deleting_a_shared_wallet_raises_it_for_that_wallet()
    {
        var (service, raised) = Create();
        var config = await service.CreateMultisigWalletAsync("Team", 2,
        [
            new CoSigner { Name = "A", PublicKeyHex = new Key().PubKey.ToHex() },
            new CoSigner { Name = "B", PublicKeyHex = new Key().PubKey.ToHex() },
            new CoSigner { Name = "C", PublicKeyHex = new Key().PubKey.ToHex() },
        ], ownerWalletId: "owner");
        raised.Clear();

        await service.DeleteWalletAsync(config.WalletId, "owner");

        Assert.Equal([config.WalletId], raised);
    }

    [Fact]
    public async Task Deleting_a_shared_wallet_that_is_not_there_raises_nothing()
    {
        var (service, raised) = Create();

        await service.DeleteWalletAsync("missing", "owner");

        Assert.Empty(raised);
    }
}
