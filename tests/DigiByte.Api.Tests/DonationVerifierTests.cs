using DigiByte.Api.Services;
using DigiByte.P2P.Shared.Sponsors;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Tests;

public class DonationVerifierTests
{
    private const string Current = "dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa";
    private const string Old = "DPSNEw5UmtUMQwTZPNoiUFDmCLu3Tp5SrK";
    private static readonly string Tx = new('a', 64);

    private static SponsorOptions Opts() => new()
    {
        DonationAddresses = [Current, Old],
        MinConfirmations = 3,
        MinDonationSatoshis = 100_000_000,
    };

    private static DonationVerifier Verifier(params IExplorerTxLookup[] lookups) =>
        new(lookups, Options.Create(Opts()));

    private static DonationTx Paid(string address, long sats, int confirmations) =>
        new(Tx, confirmations, [new DonationOutput("someone-else", 5), new DonationOutput(address, sats)]);

    [Fact]
    public async Task Confirmed_PayingProjectAddress_AboveMinimum_Passes()
    {
        var r = await Verifier(new FakeLookup(Paid(Current, 150_000_000, 3))).VerifyAsync(Tx, default);
        Assert.True(r.Ok);
        Assert.Null(r.Reason);
    }

    [Fact]
    public async Task UnknownTransaction_IsNotFound()
    {
        var r = await Verifier(new FakeLookup(null)).VerifyAsync(Tx, default);
        Assert.False(r.Ok);
        Assert.Equal(DonationRejection.NotFound, r.Reason);
    }

    [Fact]
    public async Task TooFewConfirmations_IsRejectedWithItsReason()
    {
        var r = await Verifier(new FakeLookup(Paid(Current, 150_000_000, 2))).VerifyAsync(Tx, default);
        Assert.Equal(DonationRejection.TooFewConfirmations, r.Reason);
    }

    [Fact]
    public async Task Unconfirmed_IsTooFewConfirmations()
    {
        var r = await Verifier(new FakeLookup(Paid(Current, 150_000_000, 0))).VerifyAsync(Tx, default);
        Assert.Equal(DonationRejection.TooFewConfirmations, r.Reason);
    }

    [Fact]
    public async Task PaidToAnotherAddress_IsWrongRecipient()
    {
        var r = await Verifier(new FakeLookup(Paid("dgb1qsomeoneelse", 150_000_000, 10))).VerifyAsync(Tx, default);
        Assert.Equal(DonationRejection.WrongRecipient, r.Reason);
    }

    [Fact]
    public async Task HalfADgb_IsBelowMinimum()
    {
        var r = await Verifier(new FakeLookup(Paid(Current, 50_000_000, 10))).VerifyAsync(Tx, default);
        Assert.Equal(DonationRejection.BelowMinimum, r.Reason);
    }

    [Fact]
    public async Task ExactlyTheMinimum_Passes()
    {
        var r = await Verifier(new FakeLookup(Paid(Current, 100_000_000, 3))).VerifyAsync(Tx, default);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task PaymentToTheOlderAddress_Counts()
    {
        var r = await Verifier(new FakeLookup(Paid(Old, 200_000_000, 5))).VerifyAsync(Tx, default);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task OutputsToProjectAddresses_AreSummed()
    {
        var tx = new DonationTx(Tx, 5, [new DonationOutput(Current, 60_000_000), new DonationOutput(Old, 60_000_000)]);
        var r = await Verifier(new FakeLookup(tx)).VerifyAsync(Tx, default);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task Bech32Address_MatchesCaseInsensitively_ButBase58DoesNot()
    {
        var upper = Paid(Current.ToUpperInvariant(), 150_000_000, 5);
        Assert.True((await Verifier(new FakeLookup(upper)).VerifyAsync(Tx, default)).Ok);

        var lowerBase58 = Paid(Old.ToLowerInvariant(), 150_000_000, 5);
        Assert.Equal(DonationRejection.WrongRecipient, (await Verifier(new FakeLookup(lowerBase58)).VerifyAsync(Tx, default)).Reason);
    }

    [Fact]
    public async Task FirstExplorerFailing_FallsBackToTheSecond()
    {
        var r = await Verifier(new FakeLookup(error: new HttpRequestException("down")), new FakeLookup(Paid(Current, 150_000_000, 4)))
            .VerifyAsync(Tx, default);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task FirstExplorerSaysNotFound_SecondFindsIt()
    {
        var r = await Verifier(new FakeLookup(null), new FakeLookup(Paid(Current, 150_000_000, 4))).VerifyAsync(Tx, default);
        Assert.True(r.Ok);
    }

    [Fact]
    public async Task EveryExplorerFailing_IsAnError_NeverAPass()
    {
        var v = Verifier(new FakeLookup(error: new HttpRequestException("down")), new FakeLookup(error: new TimeoutException()));
        await Assert.ThrowsAsync<DonationLookupUnavailableException>(() => v.VerifyAsync(Tx, default));
    }

    [Fact]
    public async Task OneNotFoundAndOneFailing_IsAnError_BecauseWeCannotBeSure()
    {
        var v = Verifier(new FakeLookup(null), new FakeLookup(error: new HttpRequestException("down")));
        await Assert.ThrowsAsync<DonationLookupUnavailableException>(() => v.VerifyAsync(Tx, default));
    }

    [Fact]
    public async Task NoExplorersConfigured_IsAnError()
    {
        await Assert.ThrowsAsync<DonationLookupUnavailableException>(() => Verifier().VerifyAsync(Tx, default));
    }

    private sealed class FakeLookup(DonationTx? tx = null, Exception? error = null) : IExplorerTxLookup
    {
        public string Name => "fake";
        public Task<DonationTx?> LookupAsync(string txId, CancellationToken ct) =>
            error is not null ? throw error : Task.FromResult(tx);
    }
}
