using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using static DigiByte.Wallet.Tests.ChangeAddressTests;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Payments a search finds are old payments. Before a wallet is changed to include them, it is recorded as having history,
/// so they are never celebrated as the wallet's first payment (spec 027, FR-013a).
/// </summary>
public class FoundFundsHistoryTests
{
    private static async Task<(Env Env, WalletService Service, FirstReceiveStore Store, string WalletId)> NewWalletAsync()
    {
        var env = new Env();
        var service = env.NewService();
        var wallet = await service.CreateWalletAsync("Test", env.Words, Pin);
        return (env, service, new FirstReceiveStore(env.Storage), wallet.Id);
    }

    private static List<TransactionRecord> OneReceipt() =>
    [
        new TransactionRecord
        {
            TxId = new string('a', 64),
            Direction = TransactionDirection.Received,
            AmountSatoshis = 5 * OneDgb,
            FeeSatoshis = 0,
            Timestamp = DateTime.UtcNow.AddMonths(-3),
            Confirmations = 1000,
        },
    ];

    [Fact]
    public async Task AWalletRecordedAsEmpty_BecomesHistory()
    {
        var (_, service, store, walletId) = await NewWalletAsync();
        await store.SetAsync(walletId, FirstReceiveState.Empty);

        await service.MarkHasHistoryAsync(walletId);

        Assert.Equal(FirstReceiveState.History, await store.GetAsync(walletId));
    }

    [Fact]
    public async Task AWalletNotYetDecided_BecomesHistory_SoALateEmptyLoadCannotRecordItAsEmpty()
    {
        var (_, service, store, walletId) = await NewWalletAsync();
        Assert.Equal(FirstReceiveState.Undecided, await store.GetAsync(walletId));

        await service.MarkHasHistoryAsync(walletId);
        await store.SetAsync(walletId, FirstReceiveState.Empty); // the stale empty load, arriving afterwards

        Assert.Equal(FirstReceiveState.History, await store.GetAsync(walletId));
    }

    [Fact]
    public async Task AWalletWithHistory_StaysAsItIs()
    {
        var (_, service, store, walletId) = await NewWalletAsync();
        await store.SetAsync(walletId, FirstReceiveState.History);

        await service.MarkHasHistoryAsync(walletId);

        Assert.Equal(FirstReceiveState.History, await store.GetAsync(walletId));
    }

    [Fact]
    public async Task AWalletAlreadyCelebrated_StaysCelebrated()
    {
        var (_, service, store, walletId) = await NewWalletAsync();
        await store.SetAsync(walletId, FirstReceiveState.Empty);
        await store.SetAsync(walletId, FirstReceiveState.Celebrated);

        await service.MarkHasHistoryAsync(walletId);

        Assert.Equal(FirstReceiveState.Celebrated, await store.GetAsync(walletId));
    }

    [Fact]
    public async Task WhenStorageRefusesTheWrite_ItDoesNotThrow()
    {
        var (env, service, store, walletId) = await NewWalletAsync();
        await store.SetAsync(walletId, FirstReceiveState.Empty);
        env.Storage.FailWritesWithPrefix = "firstReceive:";

        await service.MarkHasHistoryAsync(walletId);

        Assert.Equal(FirstReceiveState.Empty, await store.GetAsync(walletId));
    }

    /// <summary>
    /// Why the helper matters: the rule celebrates any receipt seen by a wallet recorded as empty, whatever its age, and
    /// celebrates nothing for a wallet with history. (Asserting only that the rule returns nothing for the stored state would
    /// prove nothing: it returns nothing for <see cref="FirstReceiveState.History"/> whatever the list holds.)
    /// </summary>
    [Fact]
    public void TheRuleCelebratesAnOldReceiptForAnEmptyWallet_AndNothingForOneWithHistory()
    {
        var receipts = OneReceipt();

        Assert.NotNull(FirstReceiveRule.Evaluate(FirstReceiveState.Empty, receipts, HistorySource.CompleteLoad).Payment);
        Assert.Null(FirstReceiveRule.Evaluate(FirstReceiveState.History, receipts, HistorySource.CompleteLoad).Payment);
    }
}
