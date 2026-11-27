using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// After a refresh finds new or newly confirmed transactions, the wallet says which of its addresses they touch, so a
/// payment request can notice money arriving after it stopped being watched closely (spec 036, T016, FR-020).
/// </summary>
public class TransactionsChangedTests
{
    private static readonly TimeSpan PastSnapshot = TimeSpan.FromSeconds(31);

    [Fact]
    public async Task ANewPayment_IsReported_WithTheAddressItPaid()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        var reports = new List<IReadOnlyCollection<string>>();
        service.TransactionsChanged += reports.Add;
        await service.GetBalanceAsync();
        reports.Clear();

        env.Explorer.Pay(env.Receive(3), 5000);
        env.Clock.Advance(PastSnapshot);
        await service.GetBalanceAsync();

        var report = Assert.Single(reports);
        Assert.Equal([env.Receive(3)], report);
    }

    [Fact]
    public async Task ARefreshWithNoChange_ReportsNothing()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        env.Explorer.Pay(env.Receive(0), 5000);
        var reports = new List<IReadOnlyCollection<string>>();
        service.TransactionsChanged += reports.Add;
        await service.GetBalanceAsync();
        await service.GetTransactionHistoryAsync();
        reports.Clear();

        env.Clock.Advance(PastSnapshot);
        await service.GetBalanceAsync();
        await service.GetTransactionHistoryAsync();

        Assert.Empty(reports);
    }

    [Fact]
    public async Task APendingPaymentThatConfirms_IsReportedAgain()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        var reports = new List<IReadOnlyCollection<string>>();
        service.TransactionsChanged += reports.Add;
        var tx = env.Explorer.Pay(env.Receive(2), 5000, height: null);
        await service.GetTransactionHistoryAsync();
        Assert.Contains(reports, r => r.Contains(env.Receive(2)));
        reports.Clear();

        env.Explorer.Confirm(tx, 901);
        env.Clock.Advance(PastSnapshot);
        await service.GetTransactionHistoryAsync();

        Assert.Contains(reports, r => r.Contains(env.Receive(2)));
    }

    [Fact]
    public async Task AFailingHandler_DoesNotBreakTheRefresh()
    {
        var env = new HistorySyncEnv();
        var service = await env.CreateAsync();
        service.TransactionsChanged += _ => throw new InvalidOperationException("boom");
        env.Explorer.Pay(env.Receive(0), 5000);

        var balance = await service.GetBalanceAsync();

        Assert.Equal(5000, balance.ConfirmedSatoshis);
    }
}
