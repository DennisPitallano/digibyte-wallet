using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>Signer cards: initials, disambiguation, presence and status (spec 018, FR-001/FR-017).</summary>
public class MultisigUxSignerCardsTests
{
    private static SignerInput In(string key, string name, bool online = true, SignerStatus status = SignerStatus.Waiting, bool creator = false) =>
        new(key, name, online, status, creator);

    [Theory]
    [InlineData("Alex Rivera", "AR")]
    [InlineData("alex", "A")]
    [InlineData("  maria   luisa  santos ", "ML")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("😀", "?")]
    [InlineData("Émile", "É")]
    public void Initials(string name, string expected)
    {
        Assert.Equal(expected, SignerCards.InitialsOf(name));
    }

    [Fact]
    public void Same_name_gets_a_fingerprint_and_unique_names_do_not()
    {
        var cards = SignerCards.Build(
            [In("02ab12cd", "Sam"), In("02ef34ab", "Sam"), In("02cc99aa", "Priya")], null);

        Assert.NotNull(cards[0].Fingerprint);
        Assert.NotNull(cards[1].Fingerprint);
        Assert.NotEqual(cards[0].Fingerprint, cards[1].Fingerprint);
        Assert.Null(cards[2].Fingerprint);
    }

    [Fact]
    public void Name_clash_ignores_case_and_surrounding_space()
    {
        var cards = SignerCards.Build([In("02ab12cd", "Sam"), In("02ef34ab", " sam ")], null);
        Assert.NotNull(cards[0].Fingerprint);
    }

    [Fact]
    public void Signed_stays_signed_when_offline()
    {
        var cards = SignerCards.Build([In("02aa", "A", online: false, status: SignerStatus.Signed)], null);
        Assert.Equal(SignerStatus.Signed, cards[0].Status);
        Assert.Equal(SignerPresence.Offline, cards[0].Presence);
    }

    [Fact]
    public void Declined_is_kept()
    {
        var cards = SignerCards.Build([In("02aa", "A", status: SignerStatus.Declined)], null);
        Assert.Equal(SignerStatus.Declined, cards[0].Status);
    }

    [Fact]
    public void The_viewer_is_marked_and_the_creator_is_flagged()
    {
        var cards = SignerCards.Build([In("02AA", "Me", creator: true), In("02bb", "Other")], "02aa");
        Assert.True(cards[0].IsYou);
        Assert.True(cards[0].IsCreator);
        Assert.False(cards[1].IsYou);
    }

    [Fact]
    public void Presence_comes_from_the_smoothing_function_when_given()
    {
        var cards = SignerCards.Build([In("02aa", "A", online: false)], null, isOnline: _ => true);
        Assert.Equal(SignerPresence.Online, cards[0].Presence);
    }

    [Fact]
    public void Creation_room_has_joined_ready_and_no_presence()
    {
        var cards = SignerCards.ForCreationRoom(
            [In("02aa", "A", status: SignerStatus.Ready, creator: true), In("02bb", "B", status: SignerStatus.Joined)], "02aa", totalSigners: 3);

        Assert.Equal(3, cards.Count);
        Assert.Equal(SignerStatus.Ready, cards[0].Status);
        Assert.Equal(SignerStatus.Joined, cards[1].Status);
        Assert.All(cards, c => Assert.Null(c.Presence));
    }

    [Fact]
    public void Creation_room_adds_not_joined_slots_for_missing_co_signers()
    {
        var cards = SignerCards.ForCreationRoom([In("02aa", "A", status: SignerStatus.Joined)], "02aa", totalSigners: 3);

        Assert.Equal(3, cards.Count);
        Assert.Equal(2, cards.Count(c => c.Status == SignerStatus.NotJoined));
        Assert.All(cards.Where(c => c.Status == SignerStatus.NotJoined), c => Assert.Equal("", c.PublicKeyHex));
    }

    [Fact]
    public void Creation_room_never_shows_more_slots_than_total()
    {
        var cards = SignerCards.ForCreationRoom(
            [In("02aa", "A"), In("02bb", "B")], null, totalSigners: 2);
        Assert.Equal(2, cards.Count);
    }
}
