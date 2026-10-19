using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// The real mainnet DD transfer (block 24,411,694) verifies under the standard BIP341 key-path sighash with SIGHASH_DEFAULT,
/// so DigiByte adds no chain-specific change to Taproot signing and the wallet's signing approach is the one the chain accepted.
/// </summary>
public class DigiDollarRealSignatureTests
{
    private const string RealTransferHex = "7007000200010211f06c4a5e63b77e7e3e0151108f38a4cc3be60d7cc0e76606728c0216679ac50100000000ffffffff1250022ce69525e654bc4807c7b483c23c9697e042624ea5a2d233b1e62893610000000000ffffffff040000000000000000225120b42be14e35aeafcd503cfa260e34b61a96845f717988869c504e21e0718a04f60000000000000000225120d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f90f232778a020000160014f42063fc641f9ad6130f9ea49bd7e194db1aa65400000000000000000d6a0244440102028e0003c2c2000140513d570c2ba2a2af1aad2e5066f1c33da3672ad1305bd191c4e918e16f3f15c384d4742d322b4cdf3c2a79313b5a120a1adb4fde5a0d5223928d6512de41773902473044022023c78f92a16f5d0c3270c4b10c0d3bc1368cd98b300b00f62f53b0e1558cedfa02207dd28f664b2da1c8d17c5bcc52a7569a2a87da4f2521b57bf8a4d6eb50352e380121033a0b8654273468abe2b88316074cc130048670d0924d2ab867d0c05bf7c6c0bb00000000";

    [Fact]
    public void RealTransfer_DdInputSignature_VerifiesWithDefaultSighash()
    {
        var tx = Transaction.Parse(RealTransferHex, DigiByteNetworkSet.Instance.Mainnet);
        var spent = new[]
        {
            new TxOut(Money.Zero, new Script(Convert.FromHexString("5120d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f"))),
            new TxOut(Money.Satoshis(2793739560000), new Script(Convert.FromHexString("00148119f05da2641ff724939c368186c816f37f9ef8"))),
        };

        var witness = tx.Inputs[0].WitScript;
        Assert.Equal(1, witness.PushCount);
        Assert.Equal(64, witness[0].Length);                      // 64 bytes means SIGHASH_DEFAULT (no sighash byte appended)

        Assert.True(TaprootSignature.TryParse(witness[0], out var sig));
        Assert.Equal(TaprootSigHash.Default, sig!.SigHash);

        var pre = tx.PrecomputeTransactionData(spent);
        var hash = tx.GetSignatureHashTaproot(pre, new TaprootExecutionData(0));
        var outputKey = new TaprootPubKey(Convert.FromHexString("d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f"));

        Assert.True(outputKey.VerifySignature(hash, sig.SchnorrSignature));
    }

    [Fact]
    public void RealTransfer_Layout_MatchesWhatTheBuilderProduces()
    {
        var tx = Transaction.Parse(RealTransferHex, DigiByteNetworkSet.Instance.Mainnet);

        Assert.Equal(0x02000770u, tx.Version);
        Assert.Equal(0u, (uint)tx.LockTime.Value);
        Assert.All(tx.Inputs, i => Assert.Equal(0xFFFFFFFFu, i.Sequence.Value));
        Assert.Equal([0L, 0L], tx.Outputs.Take(2).Select(o => o.Value.Satoshi));                  // DD outputs carry no DGB
        Assert.StartsWith("5120", tx.Outputs[0].ScriptPubKey.ToHex());                             // OP_1 <32-byte key>
        Assert.StartsWith("6a02444401", tx.Outputs[^1].ScriptPubKey.ToHex());                      // the DD OP_RETURN is last
    }
}
