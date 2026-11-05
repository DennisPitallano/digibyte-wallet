using DigiByte.Crypto.Psbt;
using NBitcoin;
using static DigiByte.Crypto.Tests.PsbtTestFixtures;

namespace DigiByte.Crypto.Tests;

public class PsbtSignerTests
{
    private static readonly Key Wallet = new();

    private static bool ScriptsVerify(Transaction tx, params FundedCoin[] coins) =>
        Net.CreateTransactionBuilder().AddCoins(coins.Select(c => (ICoin)c.Coin).ToArray()).Verify(tx, out _);

    [Fact]
    public void SignsOnlyTheListedInputs_WhenBothAreOwned()
    {
        var a = Fund(SegWit(Wallet), 100_000_000);
        var b = Fund(SegWit(Wallet), 100_000_000);
        var psbt = Builder().AddInput(a).AddInput(b).Pay(199_900_000).Build();

        var signed = PsbtSigner.SignInputs(psbt, new Dictionary<int, Key> { [0] = Wallet });

        Assert.Single(signed.Inputs[0].PartialSigs);
        Assert.Empty(signed.Inputs[1].PartialSigs);
    }

    [Fact]
    public void SegWitInput_SignsToAValidTransaction()
    {
        var coin = Fund(SegWit(Wallet), 100_000_000);
        var psbt = Builder().AddInput(coin).Pay(99_900_000).Build();

        var completion = PsbtSigner.TryComplete(PsbtSigner.SignInputs(psbt, new Dictionary<int, Key> { [0] = Wallet }));

        Assert.True(completion.IsComplete);
        Assert.True(ScriptsVerify(completion.Transaction!, coin));
    }

    [Fact]
    public void LegacyInput_SignsToAValidTransaction()
    {
        var coin = Fund(Legacy(Wallet), 100_000_000);
        var psbt = Builder().AddInput(coin).Pay(99_900_000).Build();

        var completion = PsbtSigner.TryComplete(PsbtSigner.SignInputs(psbt, new Dictionary<int, Key> { [0] = Wallet }));

        Assert.True(completion.IsComplete);
        Assert.True(ScriptsVerify(completion.Transaction!, coin));
    }

    [Fact]
    public void SigningTwice_LeavesOneIdenticalSignature()
    {
        var coin = Fund(SegWit(Wallet), 100_000_000);
        var psbt = Builder().AddInput(coin).Pay(99_900_000).Build();
        var keys = new Dictionary<int, Key> { [0] = Wallet };

        var once = PsbtSigner.SignInputs(psbt, keys);
        var twice = PsbtSigner.SignInputs(once, keys);

        Assert.Single(twice.Inputs[0].PartialSigs);
        Assert.Equal(once.Inputs[0].PartialSigs.Values.Single().ToBytes(), twice.Inputs[0].PartialSigs.Values.Single().ToBytes());
    }

    [Fact]
    public void SignInputs_LeavesTheOriginalUnchanged()
    {
        var psbt = Builder().AddInput(Fund(SegWit(Wallet), 100_000_000)).Pay(99_900_000).Build();
        var before = psbt.ToBase64();

        PsbtSigner.SignInputs(psbt, new Dictionary<int, Key> { [0] = Wallet });

        Assert.Equal(before, psbt.ToBase64());
    }

    [Fact]
    public void TryComplete_LeavesTheOriginalNotFinalized()
    {
        var psbt = Builder().AddInput(Fund(SegWit(Wallet), 100_000_000)).Pay(99_900_000).Build();
        var signed = PsbtSigner.SignInputs(psbt, new Dictionary<int, Key> { [0] = Wallet });

        Assert.True(PsbtSigner.TryComplete(signed).IsComplete);

        Assert.False(signed.Inputs[0].IsFinalized());
        Assert.Single(signed.Inputs[0].PartialSigs);
    }

    [Fact]
    public void TryComplete_RejectsATamperedSignature()
    {
        var psbt = Builder().AddInput(Fund(SegWit(Wallet), 100_000_000)).Pay(99_900_000).Build();
        psbt.Inputs[0].PartialSigs.Add(Wallet.PubKey, new TransactionSignature(new Key().Sign(RandomUtils.GetUInt256()), SigHash.All));

        var completion = PsbtSigner.TryComplete(psbt);

        Assert.False(completion.IsComplete);
        Assert.Null(completion.Transaction);
        Assert.NotEmpty(completion.Errors);
    }

    [Fact]
    public void TryComplete_IsFalse_WhenAnInputIsUnsigned()
    {
        var psbt = Builder().AddInput(Fund(SegWit(Wallet), 100_000_000)).Pay(99_900_000).Build();

        Assert.False(PsbtSigner.TryComplete(psbt).IsComplete);
    }
}
