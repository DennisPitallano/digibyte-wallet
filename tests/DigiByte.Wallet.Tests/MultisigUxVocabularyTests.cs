using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>One shared vocabulary of multisig states (spec 018, FR-010).</summary>
public class MultisigUxVocabularyTests
{
    public static IEnumerable<object[]> AllStates() =>
        Enum.GetValues<MultisigState>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Every_state_has_label_and_icon(MultisigState state)
    {
        var info = MultisigStateVocabulary.For(state);
        Assert.False(string.IsNullOrWhiteSpace(info.Label));
        Assert.False(string.IsNullOrWhiteSpace(info.Icon));
    }

    [Fact]
    public void Labels_are_unique_so_two_states_never_read_the_same()
    {
        var labels = Enum.GetValues<MultisigState>().Select(s => MultisigStateVocabulary.For(s).Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Theory]
    [InlineData(MultisigState.NeedsYourSignature, PrimaryAction.Sign)]
    [InlineData(MultisigState.LiveRoom, PrimaryAction.OpenRoom)]
    [InlineData(MultisigState.ReadyToBroadcast, PrimaryAction.Broadcast)]
    [InlineData(MultisigState.YouSigned, PrimaryAction.View)]
    [InlineData(MultisigState.WaitingForOthers, PrimaryAction.View)]
    [InlineData(MultisigState.Sent, PrimaryAction.None)]
    [InlineData(MultisigState.CancelledOrExpired, PrimaryAction.None)]
    public void Primary_action_fits_the_state(MultisigState state, PrimaryAction expected)
    {
        Assert.Equal(expected, MultisigStateVocabulary.For(state).Action);
    }

    [Fact]
    public void Wording_matches_the_spec()
    {
        Assert.Equal("Needs your signature", MultisigStateVocabulary.For(MultisigState.NeedsYourSignature).Label);
        Assert.Equal("You signed", MultisigStateVocabulary.For(MultisigState.YouSigned).Label);
        Assert.Equal("Ready to broadcast", MultisigStateVocabulary.For(MultisigState.ReadyToBroadcast).Label);
        Assert.Equal(UxTone.Positive, MultisigStateVocabulary.For(MultisigState.ReadyToBroadcast).Tone);
    }
}
