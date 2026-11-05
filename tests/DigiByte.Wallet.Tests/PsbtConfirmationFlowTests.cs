using DigiByte.Crypto.Psbt;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

public class PsbtConfirmationFlowTests
{
    private readonly List<string> _calls = [];
    private string? _limitBlock;
    private bool _limitThrows;
    private bool _warningsAccepted = true;
    private bool _warningsThrow;
    private bool _authenticated = true;
    private bool _authThrows;
    private decimal? _limitAmount;

    private PsbtConfirmationFlow Flow() => new(
        amount =>
        {
            _calls.Add("limit");
            _limitAmount = amount;
            if (_limitThrows) throw new InvalidOperationException("storage unavailable");
            return Task.FromResult(_limitBlock);
        },
        _ =>
        {
            _calls.Add("warnings");
            if (_warningsThrow) throw new InvalidOperationException("history unavailable");
            return Task.FromResult(_warningsAccepted);
        },
        () =>
        {
            _calls.Add("auth");
            if (_authThrows) throw new InvalidOperationException("biometrics unavailable");
            return Task.FromResult(_authenticated);
        });

    private static ReviewSummary Review(long net = 50_000_000, bool needsAck = false) =>
        new() { NetAmount = net, NeedsAcknowledgment = needsAck, CanSign = true, InputsToSign = 1 };

    [Fact]
    public async Task Steps_RunInOrder_AndApprove()
    {
        var outcome = await Flow().RunAsync(Review(needsAck: true), acknowledged: true);

        Assert.True(outcome.Approved);
        Assert.Null(outcome.FailedStep);
        Assert.Equal(["limit", "warnings", "auth"], _calls);
    }

    [Fact]
    public async Task MissingAcknowledgment_StopsBeforeAnythingElse()
    {
        var outcome = await Flow().RunAsync(Review(needsAck: true), acknowledged: false);

        Assert.False(outcome.Approved);
        Assert.Equal(PsbtConfirmationStep.Acknowledgment, outcome.FailedStep);
        Assert.NotNull(outcome.Message);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task OverTheSpendingLimit_StopsTheFlow_WithItsMessage()
    {
        _limitBlock = "Daily limit reached.";

        var outcome = await Flow().RunAsync(Review(), acknowledged: false);

        Assert.False(outcome.Approved);
        Assert.Equal(PsbtConfirmationStep.SpendingLimit, outcome.FailedStep);
        Assert.Equal("Daily limit reached.", outcome.Message);
        Assert.Equal(["limit"], _calls);
    }

    [Fact]
    public async Task SpendingLimit_UsesTheNetAmountInDgb_OrZeroWhenTheWalletGains()
    {
        await Flow().RunAsync(Review(net: 150_000_000), acknowledged: false);
        Assert.Equal(1.5m, _limitAmount);

        await Flow().RunAsync(Review(net: -200_000_000), acknowledged: false);
        Assert.Equal(0m, _limitAmount);
    }

    [Fact]
    public async Task SpendingLimitCheck_ThatFailsToRun_DoesntBlock()
    {
        _limitThrows = true;

        var outcome = await Flow().RunAsync(Review(), acknowledged: false);

        Assert.True(outcome.Approved);
        Assert.Equal(["limit", "warnings", "auth"], _calls);
    }

    [Fact]
    public async Task DeclinedWarning_StopsTheFlow()
    {
        _warningsAccepted = false;

        var outcome = await Flow().RunAsync(Review(), acknowledged: false);

        Assert.False(outcome.Approved);
        Assert.Equal(PsbtConfirmationStep.Warnings, outcome.FailedStep);
        Assert.Equal(["limit", "warnings"], _calls);
    }

    [Fact]
    public async Task WarningCheck_ThatFailsToRun_DoesntBlock()
    {
        _warningsThrow = true;

        Assert.True((await Flow().RunAsync(Review(), acknowledged: false)).Approved);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task FailedOrCancelledAuthentication_GivesNoGoAhead(bool authenticated, bool throws)
    {
        _authenticated = authenticated;
        _authThrows = throws;

        var outcome = await Flow().RunAsync(Review(), acknowledged: false);

        Assert.False(outcome.Approved);
        Assert.Equal(PsbtConfirmationStep.Authentication, outcome.FailedStep);
    }

    [Fact]
    public async Task AlreadySignedPsbt_GoesThroughTheSameSteps_AndIsBlockedOverTheLimit()
    {
        var signed = new ReviewSummary { NetAmount = 50_000_000, IsComplete = true, CanBroadcastSigned = true };

        Assert.True((await Flow().RunAsync(signed, acknowledged: false)).Approved);
        Assert.Equal(["limit", "warnings", "auth"], _calls);

        _calls.Clear();
        _limitBlock = "Weekly limit reached.";
        var blocked = await Flow().RunAsync(signed, acknowledged: false);
        Assert.False(blocked.Approved);
        Assert.Equal(PsbtConfirmationStep.SpendingLimit, blocked.FailedStep);
        Assert.Equal(["limit"], _calls);
    }
}
