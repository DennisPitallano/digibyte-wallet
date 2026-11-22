using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// What the pages call after a history load (spec 025, contracts/first-receive.md): the stored state, the pending
/// celebration, "shown once", and the per-device first-session marker.
/// </summary>
public class FirstReceiveCoordinatorTests
{
    private readonly TestMemoryStorage _storage = new();
    private FirstReceiveStore Store => new(_storage);
    private FirstReceiveCoordinator NewCoordinator() => new(Store);

    private static int _next;
    private static TransactionRecord Receipt(long sats = 100_000_000, int confirmations = 0) => new()
    {
        TxId = (++_next).ToString("x").PadLeft(64, 'c'),
        Direction = TransactionDirection.Received,
        AmountSatoshis = sats,
        FeeSatoshis = 0,
        Timestamp = DateTime.UtcNow,
        Confirmations = confirmations,
    };

    private static readonly TransactionRecord[] None = [];

    [Fact]
    public async Task ACreatedWallet_CelebratesItsFirstPayment_EvenWhenTheFirstLoadAlreadyContainsIt()
    {
        var c = NewCoordinator();
        await c.MarkCreatedEmptyAsync("w1");

        var payment = await c.ObserveAsync("w1", [Receipt(250_000_000)], HistorySource.CompleteLoad);

        Assert.NotNull(payment);
        Assert.Equal(2.5m, payment.AmountDgb);
        Assert.Same(payment, c.Pending("w1"));
        Assert.Equal(FirstReceiveState.Empty, await Store.GetAsync("w1")); // not Celebrated until it is shown
    }

    [Fact]
    public async Task ARecoveredWalletWithHistory_NeverCelebrates_NowOrLater()
    {
        var c = NewCoordinator();

        Assert.Null(await c.ObserveAsync("w1", [Receipt(confirmations: 500)], HistorySource.CompleteLoad));
        Assert.Equal(FirstReceiveState.History, await Store.GetAsync("w1"));

        Assert.Null(await c.ObserveAsync("w1", [Receipt(), Receipt(confirmations: 500)], HistorySource.CompleteLoad));
        Assert.Null(c.Pending("w1"));
    }

    [Fact]
    public async Task ARecoveredWalletThatIsReallyEmpty_CelebratesItsFirstPaymentLater()
    {
        var c = NewCoordinator();
        Assert.Null(await c.ObserveAsync("w1", None, HistorySource.CompleteLoad));

        Assert.NotNull(await c.ObserveAsync("w1", [Receipt()], HistorySource.CompleteLoad));
    }

    [Fact]
    public async Task AFailedOrEarlyLoad_DecidesNothing()
    {
        var c = NewCoordinator();

        Assert.Null(await c.ObserveAsync("w1", None, HistorySource.IncompleteLoad));
        Assert.Null(await c.ObserveAsync("w1", None, HistorySource.PartialAddressLoad));
        Assert.Null(await c.ObserveAsync("w1", None, HistorySource.SavedSnapshot));
        Assert.Equal(FirstReceiveState.Undecided, await Store.GetAsync("w1"));

        // So the first complete load that shows history records "has history", and there is no celebration.
        Assert.Null(await c.ObserveAsync("w1", [Receipt()], HistorySource.CompleteLoad));
        Assert.Equal(FirstReceiveState.History, await Store.GetAsync("w1"));
    }

    [Fact]
    public async Task SavedHistory_IsEnoughToRecordHasHistory()
    {
        var c = NewCoordinator();

        await c.ObserveAsync("w1", [Receipt(confirmations: 9)], HistorySource.SavedSnapshot);

        Assert.Equal(FirstReceiveState.History, await Store.GetAsync("w1"));
    }

    [Fact]
    public async Task OnceShown_ItIsNeverPendingAgain_IncludingAfterAReload()
    {
        var c = NewCoordinator();
        await c.MarkCreatedEmptyAsync("w1");
        var first = Receipt();
        await c.ObserveAsync("w1", [first], HistorySource.CompleteLoad);

        await c.MarkShownAsync("w1");

        Assert.Null(c.Pending("w1"));
        Assert.Equal(FirstReceiveState.Celebrated, await Store.GetAsync("w1"));
        Assert.Null(await c.ObserveAsync("w1", [Receipt(), first], HistorySource.CompleteLoad));

        var afterReload = NewCoordinator();
        Assert.Null(await afterReload.ObserveAsync("w1", [Receipt(), first], HistorySource.CompleteLoad));
        Assert.Null(afterReload.Pending("w1"));
    }

    /// <summary>A celebration that was detected but never shown (the user left, or reloaded) is offered again.</summary>
    [Fact]
    public async Task DetectedButNotShown_IsOfferedAgain_AfterAReload()
    {
        var c = NewCoordinator();
        await c.MarkCreatedEmptyAsync("w1");
        var first = Receipt();
        await c.ObserveAsync("w1", [first], HistorySource.CompleteLoad);

        var afterReload = NewCoordinator();
        Assert.Null(afterReload.Pending("w1"));
        Assert.NotNull(await afterReload.ObserveAsync("w1", [first], HistorySource.CompleteLoad));
    }

    [Fact]
    public async Task CelebrationsArePerWallet()
    {
        var c = NewCoordinator();
        await c.MarkCreatedEmptyAsync("w1");
        await c.MarkCreatedEmptyAsync("w2");
        await c.ObserveAsync("w1", [Receipt()], HistorySource.CompleteLoad);
        await c.MarkShownAsync("w1");

        Assert.NotNull(await c.ObserveAsync("w2", [Receipt()], HistorySource.CompleteLoad));
        Assert.Null(c.Pending("w1"));
        Assert.NotNull(c.Pending("w2"));
    }

    [Fact]
    public async Task TwoLoadsAtOnce_DetectOnePayment_AndRaiseChangedOnce()
    {
        var c = NewCoordinator();
        await c.MarkCreatedEmptyAsync("w1");
        var tx = Receipt();
        var changes = 0;
        c.Changed += () => changes++;

        var results = await Task.WhenAll(
            c.ObserveAsync("w1", [tx], HistorySource.CompleteLoad),
            c.ObserveAsync("w1", [tx], HistorySource.CompleteLoad));

        Assert.All(results, Assert.NotNull);
        Assert.Equal(1, changes); // pending + first session ended, raised together by the first load only
    }

    [Fact]
    public async Task AWalletWhoseFirstTransactionIsNotAReceipt_DropsOutForGood()
    {
        var c = NewCoordinator();
        await c.MarkCreatedEmptyAsync("w1");
        var sent = new TransactionRecord
        {
            TxId = new string('d', 64), Direction = TransactionDirection.Sent, AmountSatoshis = 5, FeeSatoshis = 1,
            Timestamp = DateTime.UtcNow, Confirmations = 1,
        };

        Assert.Null(await c.ObserveAsync("w1", [sent], HistorySource.CompleteLoad));
        Assert.Equal(FirstReceiveState.History, await Store.GetAsync("w1"));
        Assert.Null(await c.ObserveAsync("w1", [Receipt(), sent], HistorySource.CompleteLoad));
    }

    [Fact]
    public async Task AnEmptyOrMissingWalletId_IsIgnored()
    {
        var c = NewCoordinator();
        Assert.Null(await c.ObserveAsync("", [Receipt()], HistorySource.CompleteLoad));
        Assert.Null(await c.ObserveAsync(null, [Receipt()], HistorySource.CompleteLoad));
        Assert.Null(c.Pending(null));
        await c.MarkShownAsync("");
        await c.MarkCreatedEmptyAsync("");
        Assert.False(c.FirstSessionEnded);
    }

    // ── First-session marker (FR-029) ──

    [Fact]
    public async Task FirstSession_IsNotEnded_OnANewDevice()
    {
        var c = NewCoordinator();
        await c.InitializeAsync(() => Task.FromResult(false));

        Assert.False(c.FirstSessionEnded);
        await c.ObserveAsync("w1", None, HistorySource.CompleteLoad);
        Assert.False(c.FirstSessionEnded);
    }

    [Theory]
    [InlineData(HistorySource.CompleteLoad)]
    [InlineData(HistorySource.PartialAddressLoad)]
    [InlineData(HistorySource.IncompleteLoad)]
    [InlineData(HistorySource.SavedSnapshot)]
    public async Task FirstSession_Ends_WhenAnyHistoryHasATransaction_AndStaysEnded(HistorySource source)
    {
        var c = NewCoordinator();
        await c.InitializeAsync();
        var changes = 0;
        c.Changed += () => changes++;

        await c.ObserveAsync("w1", [Receipt(confirmations: 3)], source);

        Assert.True(c.FirstSessionEnded);
        Assert.True(changes >= 1);
        Assert.True(await Store.IsFirstSessionEndedAsync());

        // Deleting the wallet's record does not bring the first session back.
        await Store.DeleteAsync("w1");
        var afterReload = NewCoordinator();
        await afterReload.InitializeAsync(() => Task.FromResult(false));
        Assert.True(afterReload.FirstSessionEnded);
    }

    /// <summary>An existing user after an update: a wallet with saved history means the device is past its first session.</summary>
    [Fact]
    public async Task Initialize_EndsTheFirstSession_WhenAWalletAlreadyHasSavedHistory()
    {
        var c = NewCoordinator();
        var asked = 0;

        await c.InitializeAsync(() => { asked++; return Task.FromResult(true); });
        await c.InitializeAsync(() => { asked++; return Task.FromResult(true); });

        Assert.True(c.FirstSessionEnded);
        Assert.True(await Store.IsFirstSessionEndedAsync());
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Initialize_DoesNotAsk_WhenTheMarkerIsAlreadySet_AndSurvivesAFailingCheck()
    {
        await Store.EndFirstSessionAsync();
        var c = NewCoordinator();
        await c.InitializeAsync(() => throw new InvalidOperationException("must not be called"));
        Assert.True(c.FirstSessionEnded);

        var storage2 = new TestMemoryStorage();
        var fresh = new FirstReceiveCoordinator(new FirstReceiveStore(storage2));
        await fresh.InitializeAsync(() => throw new InvalidOperationException("snapshots unreadable"));
        Assert.False(fresh.FirstSessionEnded);
    }

    [Fact]
    public void OtherDialogOpen_RaisesChanged_OnlyWhenItChanges()
    {
        var c = NewCoordinator();
        var changes = 0;
        c.Changed += () => changes++;

        c.OtherDialogOpen = false;
        c.OtherDialogOpen = true;
        c.OtherDialogOpen = true;
        c.OtherDialogOpen = false;

        Assert.Equal(2, changes);
    }
}
