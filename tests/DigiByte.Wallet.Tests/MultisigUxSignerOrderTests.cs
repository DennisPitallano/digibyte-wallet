using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>The order of signers in a room: you, then who has signed, then who is here, then who is away (spec 026, FR-030).</summary>
public class MultisigUxSignerOrderTests
{
    private static SignerCardModel Card(string name, SignerPresence? presence = SignerPresence.Offline,
        SignerStatus status = SignerStatus.Waiting, bool you = false) =>
        new(name, name, you, "?", null, presence, status, false);

    private static string[] Names(IEnumerable<SignerCardModel> cards) => cards.Select(c => c.DisplayName).ToArray();

    [Fact]
    public void You_come_first_whatever_your_state()
    {
        var sorted = SignerOrder.Sort(
        [
            Card("signed", SignerPresence.Online, SignerStatus.Signed),
            Card("me", SignerPresence.Offline, SignerStatus.Waiting, you: true),
        ]);

        Assert.Equal(["me", "signed"], Names(sorted));
    }

    [Fact]
    public void Then_signed_then_online_then_offline()
    {
        var sorted = SignerOrder.Sort(
        [
            Card("away"),
            Card("here", SignerPresence.Online),
            Card("signed-away", SignerPresence.Offline, SignerStatus.Signed),
            Card("me", SignerPresence.Online, you: true),
            Card("signed-here", SignerPresence.Online, SignerStatus.Signed),
        ]);

        Assert.Equal(["me", "signed-away", "signed-here", "here", "away"], Names(sorted));
    }

    [Fact]
    public void The_order_inside_each_group_is_the_order_given()
    {
        var sorted = SignerOrder.Sort(
        [
            Card("away-1"), Card("here-1", SignerPresence.Online), Card("away-2"), Card("here-2", SignerPresence.Online),
            Card("signed-1", status: SignerStatus.Signed), Card("signed-2", status: SignerStatus.Signed),
        ]);

        Assert.Equal(["signed-1", "signed-2", "here-1", "here-2", "away-1", "away-2"], Names(sorted));
    }

    [Fact]
    public void Someone_who_declined_sorts_by_whether_they_are_here()
    {
        var sorted = SignerOrder.Sort(
        [
            Card("declined-away", SignerPresence.Offline, SignerStatus.Declined),
            Card("waiting-away"),
            Card("declined-here", SignerPresence.Online, SignerStatus.Declined),
        ]);

        Assert.Equal(["declined-here", "declined-away", "waiting-away"], Names(sorted));
    }

    [Fact]
    public void Unknown_presence_counts_as_away()
    {
        var sorted = SignerOrder.Sort([Card("unknown", presence: null), Card("here", SignerPresence.Online)]);

        Assert.Equal(["here", "unknown"], Names(sorted));
    }

    [Fact]
    public void Nothing_in_nothing_out_and_the_input_is_left_alone()
    {
        Assert.Empty(SignerOrder.Sort([]));

        SignerCardModel[] input = [Card("b"), Card("a", SignerPresence.Online)];
        SignerOrder.Sort(input);
        Assert.Equal(["b", "a"], Names(input));
    }
}
