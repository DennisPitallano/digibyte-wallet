using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Requests used to be one list for every wallet on the device. Each wallet takes the old requests whose address is
/// one of its own, as untracked (spec 036, data-model "Migration"); the rest stay where they were.
/// </summary>
public class RequestMigrationTests
{
    private static string Legacy(params (string Id, string Address)[] items) =>
        JsonSerializer.Serialize(items.Select(i => new PaymentRequest { Id = i.Id, Address = i.Address }).ToList());

    private static readonly HashSet<string> Mine = ["dgb1qmine1", "dgb1qmine2"];

    [Fact]
    public void RequestsForThisWallet_MoveOut_AsUntracked_AndTheRestStay()
    {
        var result = RequestMigration.Split(Legacy(("a", "dgb1qmine1"), ("b", "dgb1qother"), ("c", "dgb1qmine2")), Mine);

        Assert.True(result.Changed);
        Assert.Equal(["a", "c"], result.Moved.Select(r => r.Id));
        Assert.All(result.Moved, r =>
        {
            Assert.False(r.Tracked);
            Assert.Equal(PaymentRequestStatus.NotTracked, r.Status);
        });
        var remaining = JsonSerializer.Deserialize<List<PaymentRequest>>(result.RemainingJson!, PaymentRequestService.JsonOptions)!;
        Assert.Equal("b", Assert.Single(remaining).Id);
    }

    [Fact]
    public void WhenEverythingMoves_TheLegacyKeyIsToBeRemoved()
    {
        var result = RequestMigration.Split(Legacy(("a", "dgb1qmine1")), Mine);

        Assert.True(result.Changed);
        Assert.Single(result.Moved);
        Assert.Null(result.RemainingJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public void NothingStored_MovesNothing(string? legacy)
    {
        var result = RequestMigration.Split(legacy, Mine);

        Assert.False(result.Changed);
        Assert.Empty(result.Moved);
    }

    [Fact]
    public void NoRequestForThisWallet_ChangesNothing()
    {
        var legacy = Legacy(("b", "dgb1qother"));

        var result = RequestMigration.Split(legacy, Mine);

        Assert.False(result.Changed);
        Assert.Empty(result.Moved);
        Assert.Equal(legacy, result.RemainingJson);
    }

    [Fact]
    public void RunningItAgain_OnWhatWasLeft_MovesNothing()
    {
        var first = RequestMigration.Split(Legacy(("a", "dgb1qmine1"), ("b", "dgb1qother")), Mine);

        var second = RequestMigration.Split(first.RemainingJson, Mine);

        Assert.False(second.Changed);
        Assert.Empty(second.Moved);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Id\":\"a\"}")]
    [InlineData("[{\"Address\":")]
    public void UnreadableLegacyJson_IsLeftUntouched(string legacy)
    {
        var result = RequestMigration.Split(legacy, Mine);

        Assert.False(result.Changed);
        Assert.Empty(result.Moved);
        Assert.Equal(legacy, result.RemainingJson);
    }
}
