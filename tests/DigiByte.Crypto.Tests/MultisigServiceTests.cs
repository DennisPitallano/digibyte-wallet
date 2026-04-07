using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class MultisigServiceTests
{
    private readonly MultisigService _sut = new(DigiByteNetwork.Mainnet);

    private static Key[] GenerateKeys(int count)
    {
        var keys = new Key[count];
        for (int i = 0; i < count; i++)
            keys[i] = new Key();
        return keys;
    }

    // ─── CreateRedeemScript ───

    [Fact]
    public void CreateRedeemScript_2of3_ReturnsValidMultisigScript()
    {
        var keys = GenerateKeys(3);
        var pubKeys = keys.Select(k => k.PubKey).ToArray();

        var script = _sut.CreateRedeemScript(2, pubKeys);

        var extracted = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);
        Assert.NotNull(extracted);
        Assert.Equal(2, extracted.SignatureCount);
        Assert.Equal(3, extracted.PubKeys.Length);
    }

    [Fact]
    public void CreateRedeemScript_1of2_ReturnsValidScript()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(1, keys.Select(k => k.PubKey));

        var extracted = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(script);
        Assert.NotNull(extracted);
        Assert.Equal(1, extracted.SignatureCount);
        Assert.Equal(2, extracted.PubKeys.Length);
    }

    [Fact]
    public void CreateRedeemScript_SortsKeysBIP67()
    {
        var keys = GenerateKeys(3);
        var pubKeys = keys.Select(k => k.PubKey).ToArray();

        var script1 = _sut.CreateRedeemScript(2, pubKeys);
        var script2 = _sut.CreateRedeemScript(2, pubKeys.Reverse());

        // Same script regardless of input order (BIP67)
        Assert.Equal(script1.ToHex(), script2.ToHex());
    }

    [Fact]
    public void CreateRedeemScript_Deterministic_SameKeysProduceSameScript()
    {
        var keys = GenerateKeys(3);
        var pubKeys = keys.Select(k => k.PubKey).ToArray();

        var s1 = _sut.CreateRedeemScript(2, pubKeys);
        var s2 = _sut.CreateRedeemScript(2, pubKeys);

        Assert.Equal(s1.ToHex(), s2.ToHex());
    }

    [Fact]
    public void CreateRedeemScript_RequiredZero_Throws()
    {
        var keys = GenerateKeys(2);
        Assert.Throws<ArgumentException>(() =>
            _sut.CreateRedeemScript(0, keys.Select(k => k.PubKey)));
    }

    [Fact]
    public void CreateRedeemScript_RequiredExceedsTotal_Throws()
    {
        var keys = GenerateKeys(2);
        Assert.Throws<ArgumentException>(() =>
            _sut.CreateRedeemScript(3, keys.Select(k => k.PubKey)));
    }

    [Fact]
    public void CreateRedeemScript_Over15Keys_Throws()
    {
        var keys = GenerateKeys(16);
        Assert.Throws<ArgumentException>(() =>
            _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey)));
    }

    [Fact]
    public void CreateRedeemScript_15Keys_Succeeds()
    {
        var keys = GenerateKeys(15);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        Assert.NotNull(script);
    }

    // ─── Address derivation ───

    [Fact]
    public void GetP2SH_P2WSH_Address_ReturnsNonNull()
    {
        var keys = GenerateKeys(3);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var address = _sut.GetP2SH_P2WSH_Address(script);

        Assert.NotNull(address);
        Assert.NotEmpty(address.ToString());
    }

    [Fact]
    public void GetP2SH_P2WSH_Address_Deterministic()
    {
        var keys = GenerateKeys(3);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var addr1 = _sut.GetP2SH_P2WSH_Address(script);
        var addr2 = _sut.GetP2SH_P2WSH_Address(script);

        Assert.Equal(addr1.ToString(), addr2.ToString());
    }

    [Fact]
    public void GetP2WSH_Address_ReturnsNonNull()
    {
        var keys = GenerateKeys(3);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var address = _sut.GetP2WSH_Address(script);

        Assert.NotNull(address);
        Assert.StartsWith("dgb1", address.ToString());
    }

    [Fact]
    public void P2SH_And_P2WSH_Addresses_AreDifferent()
    {
        var keys = GenerateKeys(3);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var p2sh = _sut.GetP2SH_P2WSH_Address(script);
        var p2wsh = _sut.GetP2WSH_Address(script);

        Assert.NotEqual(p2sh.ToString(), p2wsh.ToString());
    }

    [Fact]
    public void GetP2WSH_Address_TestnetPrefix()
    {
        var testnetService = new MultisigService(DigiByteNetwork.Testnet);
        var keys = GenerateKeys(2);
        var script = testnetService.CreateRedeemScript(2, keys.Select(k => k.PubKey));

        var address = testnetService.GetP2WSH_Address(script);

        Assert.StartsWith("dgbt1", address.ToString());
    }

    // ─── PSBT workflow ───

    [Fact]
    public void CreateMultisigPSBT_ReturnsValidPSBT()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);

        // Fake UTXO on the multisig address
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));

        Assert.NotNull(psbt);
        Assert.True(psbt.Inputs.Count > 0);
    }

    [Fact]
    public void CreateMultisigPSBT_WithMemo_IncludesOpReturn()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1), memo: "test memo");

        Assert.NotNull(psbt);
        // The underlying tx should have an OP_RETURN output
        var tx = psbt.GetGlobalTransaction();
        Assert.Contains(tx.Outputs, o => o.ScriptPubKey.ToString().Contains("OP_RETURN"));
    }

    [Fact]
    public void SignPSBT_AddsPartialSignature()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));

        // Add witness script to PSBT inputs so signing works
        foreach (var input in psbt.Inputs)
            input.WitnessScript = script;

        _sut.SignPSBT(psbt, keys[0]);

        Assert.Equal(1, _sut.CountSignatures(psbt, 0));
    }

    [Fact]
    public void CombinePSBTs_MergesSignatures()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var basePsbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));
        foreach (var input in basePsbt.Inputs)
            input.WitnessScript = script;

        // Signer 1
        var psbt1 = _sut.DeserializePSBT(MultisigService.SerializePSBT(basePsbt));
        _sut.SignPSBT(psbt1, keys[0]);

        // Signer 2
        var psbt2 = _sut.DeserializePSBT(MultisigService.SerializePSBT(basePsbt));
        _sut.SignPSBT(psbt2, keys[1]);

        var combined = _sut.CombinePSBTs([psbt1, psbt2]);

        Assert.Equal(2, _sut.CountSignatures(combined, 0));
    }

    [Fact]
    public void CombinePSBTs_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CombinePSBTs([]));
    }

    [Fact]
    public void CanFinalize_ReturnsFalse_WhenNotEnoughSignatures()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));
        foreach (var input in psbt.Inputs)
            input.WitnessScript = script;

        _sut.SignPSBT(psbt, keys[0]);

        Assert.False(_sut.CanFinalize(psbt, 2));
    }

    [Fact]
    public void CanFinalize_ReturnsTrue_WhenEnoughSignatures()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));
        foreach (var input in psbt.Inputs)
            input.WitnessScript = script;

        _sut.SignPSBT(psbt, keys[0]);
        _sut.SignPSBT(psbt, keys[1]);

        Assert.True(_sut.CanFinalize(psbt, 2));
    }

    [Fact]
    public void FinalizePSBT_FullySigned_ExtractsTransaction()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));
        foreach (var input in psbt.Inputs)
            input.WitnessScript = script;

        _sut.SignPSBT(psbt, keys[0]);
        _sut.SignPSBT(psbt, keys[1]);

        var tx = _sut.FinalizePSBT(psbt);

        Assert.NotNull(tx);
        Assert.True(tx.Inputs.Count > 0);
        Assert.True(tx.Outputs.Count > 0);
    }

    [Fact]
    public void TryFinalize_NotFullySigned_ReturnsNull()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));
        foreach (var input in psbt.Inputs)
            input.WitnessScript = script;

        _sut.SignPSBT(psbt, keys[0]);

        var tx = _sut.TryFinalize(psbt);

        Assert.Null(tx);
    }

    // ─── Serialization ───

    [Fact]
    public void SerializeDeserialize_PSBT_RoundTrips()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));

        var base64 = MultisigService.SerializePSBT(psbt);
        var deserialized = _sut.DeserializePSBT(base64);

        Assert.Equal(psbt.GetGlobalTransaction().GetHash(), deserialized.GetGlobalTransaction().GetHash());
    }

    // ─── CountSignatures / GetSigners ───

    [Fact]
    public void CountSignatures_InvalidIndex_ReturnsZero()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));

        Assert.Equal(0, _sut.CountSignatures(psbt, -1));
        Assert.Equal(0, _sut.CountSignatures(psbt, 99));
    }

    [Fact]
    public void GetSigners_ReturnsCorrectPubKeys()
    {
        var keys = GenerateKeys(3);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));
        foreach (var input in psbt.Inputs)
            input.WitnessScript = script;

        _sut.SignPSBT(psbt, keys[1]);

        var signers = _sut.GetSigners(psbt, 0);
        Assert.Single(signers);
        Assert.Equal(keys[1].PubKey.ToHex(), signers[0].ToHex());
    }

    [Fact]
    public void GetSigners_InvalidIndex_ReturnsEmpty()
    {
        var keys = GenerateKeys(2);
        var script = _sut.CreateRedeemScript(2, keys.Select(k => k.PubKey));
        var address = _sut.GetP2WSH_Address(script);
        var destination = keys[0].PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        var txId = uint256.Parse("ab" + new string('0', 62));
        var coin = new Coin(txId, 0, Money.Coins(10m), script.WitHash.ScriptPubKey);

        var psbt = _sut.CreateMultisigPSBT(
            script, [coin], destination, Money.Coins(1m), address,
            new FeeRate(Money.Satoshis(5), 1));

        var signers = _sut.GetSigners(psbt, -1);
        Assert.Empty(signers);
    }
}
