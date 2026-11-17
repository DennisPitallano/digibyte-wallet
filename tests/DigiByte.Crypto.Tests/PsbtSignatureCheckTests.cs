using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// Checking each partial signature in a multisig PSBT against its input's signature hash
/// (specs/017-multisig-online-signing, research R6).
/// </summary>
public class PsbtSignatureCheckTests
{
    private static readonly Network Net = DigiByteNetwork.Mainnet;

    private static (PSBT Psbt, Key[] Keys, Script Redeem) TwoOfThree(bool p2wsh = false, int inputs = 1)
    {
        var keys = new[] { new Key(), new Key(), new Key() };
        var service = new MultisigService(Net);
        var redeem = service.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = p2wsh ? service.GetP2WSH_Address(redeem) : service.GetP2SH_P2WSH_Address(redeem);
        var coins = Enumerable.Range(0, inputs)
            .Select(_ => new Coin(RandomUtils.GetUInt256(), 0, Money.Coins(10), address.ScriptPubKey))
            .ToList();
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net);
        var psbt = service.CreateMultisigPSBTWithFee(redeem, coins, dest, Money.Coins(1), address, 10_000, 100_000);
        return (psbt, keys, redeem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Valid_signatures_are_reported_valid_for_both_address_types(bool p2wsh)
    {
        var (psbt, keys, _) = TwoOfThree(p2wsh);
        psbt.SignWithKeys(keys[0]);

        var results = PsbtSignatureCheck.Check(psbt);

        var sig = Assert.Single(results);
        Assert.Equal(0, sig.InputIndex);
        Assert.Equal(keys[0].PubKey.ToHex(), sig.PublicKeyHex);
        Assert.True(sig.IsValid);
    }

    [Fact]
    public void A_signature_over_the_wrong_hash_is_reported_invalid()
    {
        var (psbt, keys, _) = TwoOfThree();
        psbt.Inputs[0].PartialSigs.Add(keys[1].PubKey, new TransactionSignature(keys[1].Sign(uint256.One), SigHash.All));

        var sig = Assert.Single(PsbtSignatureCheck.Check(psbt));
        Assert.False(sig.IsValid);
    }

    [Fact]
    public void A_signature_stored_under_another_key_is_reported_invalid()
    {
        var (psbt, keys, _) = TwoOfThree();
        psbt.SignWithKeys(keys[0]);
        var real = psbt.Inputs[0].PartialSigs[keys[0].PubKey];
        psbt.Inputs[0].PartialSigs.Clear();
        psbt.Inputs[0].PartialSigs.Add(keys[2].PubKey, real);

        var sig = Assert.Single(PsbtSignatureCheck.Check(psbt));
        Assert.Equal(keys[2].PubKey.ToHex(), sig.PublicKeyHex);
        Assert.False(sig.IsValid);
    }

    [Fact]
    public void An_input_without_its_spent_output_reports_its_signatures_invalid()
    {
        var (psbt, keys, _) = TwoOfThree();
        psbt.SignWithKeys(keys[0]);
        psbt.Inputs[0].WitnessUtxo = null;
        psbt.Inputs[0].NonWitnessUtxo = null;

        Assert.All(PsbtSignatureCheck.Check(psbt), s => Assert.False(s.IsValid));
    }

    [Fact]
    public void Keys_valid_on_every_input_are_the_signed_keys()
    {
        var (psbt, keys, _) = TwoOfThree(inputs: 2);
        psbt.SignWithKeys(keys[0]);
        // keys[1] signs only the first input.
        var copy = psbt.Clone();
        copy.SignWithKeys(keys[1]);
        psbt.Inputs[0].PartialSigs.Add(keys[1].PubKey, copy.Inputs[0].PartialSigs[keys[1].PubKey]);

        var signed = PsbtSignatureCheck.KeysValidOnEveryInput(psbt);

        Assert.Equal([keys[0].PubKey.ToHex()], signed);
    }

    [Fact]
    public void Spends_multisig_script_accepts_both_forms_and_rejects_others()
    {
        foreach (var p2wsh in new[] { false, true })
        {
            var (psbt, keys, _) = TwoOfThree(p2wsh);
            var hexes = keys.Select(k => k.PubKey.ToHex()).Reverse().ToList();   // order must not matter
            Assert.True(PsbtSignatureCheck.SpendsMultisig(psbt, hexes, 2));
            Assert.False(PsbtSignatureCheck.SpendsMultisig(psbt, hexes, 1));
            Assert.False(PsbtSignatureCheck.SpendsMultisig(psbt, [hexes[0], hexes[1], new Key().PubKey.ToHex()], 2));
        }

        var (missing, k, _) = TwoOfThree();
        missing.Inputs[0].WitnessUtxo = null;
        Assert.False(PsbtSignatureCheck.SpendsMultisig(missing, k.Select(x => x.PubKey.ToHex()).ToList(), 2));
    }
}
