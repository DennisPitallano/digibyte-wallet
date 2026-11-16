using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>Destination checks, scan classification, and fee / Max / change maths for the multisig Send page (spec 016, US5).</summary>
public class MultisigSendValidationTests
{
    private static string MainnetSegwit() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString();
    private static string MainnetLegacy() => new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Mainnet).ToString();
    private static string TestnetSegwit() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Testnet).ToString();

    private static UtxoInfo Utxo(long amount) => new()
    {
        TxId = RandomUtils.GetUInt256().ToString(),
        OutputIndex = 0,
        AmountSatoshis = amount,
        ScriptPubKey = "",
        Confirmations = 10,
    };

    // ─── Destination ───

    [Fact]
    public void ValidAddresses_AreAccepted_OnTheirOwnNetwork()
    {
        Assert.True(MultisigSendHelper.ValidateDestination(MainnetSegwit(), DigiByteNetwork.Mainnet).Ok);
        Assert.True(MultisigSendHelper.ValidateDestination(MainnetLegacy(), DigiByteNetwork.Mainnet).Ok);
        Assert.True(MultisigSendHelper.ValidateDestination(TestnetSegwit(), DigiByteNetwork.Testnet).Ok);
    }

    [Fact]
    public void AnAddressForAnotherNetwork_IsRejected_WithAWrongNetworkMessage()
    {
        var result = MultisigSendHelper.ValidateDestination(TestnetSegwit(), DigiByteNetwork.Mainnet);

        Assert.False(result.Ok);
        Assert.Contains("different network", result.Error);
    }

    [Fact]
    public void ABadChecksum_IsRejected()
    {
        var address = MainnetSegwit();
        var corrupted = address[..^1] + (address[^1] == 'q' ? 'p' : 'q');

        var result = MultisigSendHelper.ValidateDestination(corrupted, DigiByteNetwork.Mainnet);

        Assert.False(result.Ok);
        Assert.Contains("not a valid", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not an address")]
    public void EmptyOrGarbage_IsRejected(string? input)
    {
        Assert.False(MultisigSendHelper.ValidateDestination(input, DigiByteNetwork.Mainnet).Ok);
    }

    [Fact]
    public void SendingToTheMultisigsOwnAddress_IsFlagged_ButStillValid()
    {
        var own = MainnetSegwit();

        var result = MultisigSendHelper.ValidateDestination(own, DigiByteNetwork.Mainnet, own);

        Assert.True(result.Ok);
        Assert.True(result.IsOwnAddress);
        Assert.False(MultisigSendHelper.ValidateDestination(MainnetSegwit(), DigiByteNetwork.Mainnet, own).IsOwnAddress);
    }

    // ─── Scan classification ───

    [Fact]
    public void ClassifyScan_RecognisesAddresses_PaymentLinks_Psbts_AndUnknowns()
    {
        var address = MainnetSegwit();
        Assert.Equal(ScanKind.Address, MultisigSendHelper.ClassifyScan(address).Kind);

        var link = MultisigSendHelper.ClassifyScan($"digibyte:{address}?amount=1.5&label=Rent");
        Assert.Equal(ScanKind.PaymentUri, link.Kind);
        Assert.Equal(address, link.Address);
        Assert.Equal(1.5m, link.AmountDgb);
        Assert.Equal("Rent", link.Label);

        Assert.Equal(ScanKind.Psbt, MultisigSendHelper.ClassifyScan("cHNidP8BAHECAAAAA...").Kind);
        Assert.Equal(ScanKind.Unknown, MultisigSendHelper.ClassifyScan("hello world").Kind);
        Assert.Equal(ScanKind.Unknown, MultisigSendHelper.ClassifyScan("digibyte:").Kind);
        Assert.Equal(ScanKind.Unknown, MultisigSendHelper.ClassifyScan(null).Kind);
    }

    // ─── Fee, Max, change ───

    [Fact]
    public void Fee_GrowsWithTheNumberOfInputs_AndIsNotAFlatSize()
    {
        var one = MultisigSendHelper.EstimateVBytes(1, 2, 3, "p2sh-p2wsh", hasChange: true);
        var five = MultisigSendHelper.EstimateVBytes(5, 2, 3, "p2sh-p2wsh", hasChange: true);

        Assert.True(five > one);
        Assert.NotEqual(250, one);
    }

    [Fact]
    public void FeeEqualsEstimatedSize_TimesTheSatPerVByteRate()
    {
        var utxos = new List<UtxoInfo> { Utxo(1_000_000_000) };
        var vbytes = MultisigSendHelper.EstimateVBytes(1, 2, 3, "p2sh-p2wsh", hasChange: true);

        var estimate = MultisigSendHelper.EstimateSend(utxos, 50, 100_000_000, 2, 3, "p2sh-p2wsh");

        Assert.True(estimate.Ok);
        Assert.Equal(vbytes * 50L, estimate.FeeSatoshis);
        Assert.Equal(1_000_000_000 - 100_000_000 - estimate.FeeSatoshis, estimate.ChangeSatoshis);
        Assert.Equal(estimate.ChangeSatoshis, estimate.RemainingSatoshis);
    }

    [Fact]
    public void Max_SendsEverythingMinusTheFee_AndLeavesNothing()
    {
        var utxos = new List<UtxoInfo> { Utxo(600_000_000), Utxo(400_000_000) };

        var estimate = MultisigSendHelper.EstimateSend(utxos, 20, 0, 2, 3, "p2sh-p2wsh", max: true);

        Assert.True(estimate.Ok);
        Assert.Equal(1_000_000_000 - estimate.FeeSatoshis, estimate.AmountSatoshis);
        Assert.Equal(0, estimate.RemainingSatoshis);
        Assert.Equal(0, estimate.ChangeSatoshis);
        Assert.Equal(2, estimate.Inputs.Count);
        Assert.Equal(estimate.AmountSatoshis, estimate.MaxSendableSatoshis);
    }

    [Fact]
    public void AnAmountAboveTheSpendableBalance_IsBlocked_NamingTheAvailableBalance()
    {
        var utxos = new List<UtxoInfo> { Utxo(200_000_000) };

        var estimate = MultisigSendHelper.EstimateSend(utxos, 20, 300_000_000, 2, 3, "p2sh-p2wsh");

        Assert.False(estimate.Ok);
        Assert.Contains("2.00000000", estimate.Error);
    }

    [Fact]
    public void AnAmountThatLeavesNothingForTheFee_IsBlocked()
    {
        var utxos = new List<UtxoInfo> { Utxo(200_000_000) };

        var estimate = MultisigSendHelper.EstimateSend(utxos, 20, 200_000_000, 2, 3, "p2sh-p2wsh");

        Assert.False(estimate.Ok);
    }

    [Fact]
    public void DustThreshold_RejectsJustBelow_AndAcceptsExactlyAt()
    {
        var utxos = new List<UtxoInfo> { Utxo(1_000_000_000) };

        var below = MultisigSendHelper.EstimateSend(utxos, 20, MultisigSendHelper.DustThresholdSatoshis - 1, 2, 3, "p2sh-p2wsh");
        var at = MultisigSendHelper.EstimateSend(utxos, 20, MultisigSendHelper.DustThresholdSatoshis, 2, 3, "p2sh-p2wsh");

        Assert.False(below.Ok);
        Assert.Contains("minimum", below.Error);
        Assert.True(at.Ok);
        Assert.Equal(100_000, MultisigSendHelper.DustThresholdSatoshis);
    }

    [Fact]
    public void ZeroAndNegativeAmounts_AreRejected()
    {
        var utxos = new List<UtxoInfo> { Utxo(1_000_000_000) };

        Assert.False(MultisigSendHelper.EstimateSend(utxos, 20, 0, 2, 3, "p2sh-p2wsh").Ok);
        Assert.False(MultisigSendHelper.EstimateSend(utxos, 20, -5, 2, 3, "p2sh-p2wsh").Ok);
    }

    [Fact]
    public void NoFunds_IsReported()
    {
        var estimate = MultisigSendHelper.EstimateSend([], 20, 100_000_000, 2, 3, "p2sh-p2wsh");

        Assert.False(estimate.Ok);
        Assert.Contains("no spendable funds", estimate.Error);
    }

    [Fact]
    public void ChangeBelowDust_IsFoldedIntoTheFee_WithNoChangeOutput()
    {
        // Leave only ~0.0005 DGB after amount and fee: too little to be worth a change output.
        var utxos = new List<UtxoInfo> { Utxo(300_000_000) };
        var feeWithChange = MultisigSendHelper.EstimateVBytes(1, 2, 3, "p2sh-p2wsh", true) * 20L;
        var amount = 300_000_000 - feeWithChange - 50_000;

        var estimate = MultisigSendHelper.EstimateSend(utxos, 20, amount, 2, 3, "p2sh-p2wsh");

        Assert.True(estimate.Ok);
        Assert.Equal(0, estimate.ChangeSatoshis);
        Assert.Equal(300_000_000 - amount, estimate.FeeSatoshis);   // everything not sent is fee
        Assert.True(estimate.FeeSatoshis > feeWithChange);
    }

    [Fact]
    public void LargestCoinsAreChosenFirst_AndOnlyAsManyAsNeeded()
    {
        var utxos = new List<UtxoInfo> { Utxo(10_000_000), Utxo(900_000_000), Utxo(20_000_000) };

        var estimate = MultisigSendHelper.EstimateSend(utxos, 20, 100_000_000, 2, 3, "p2sh-p2wsh");

        Assert.True(estimate.Ok);
        Assert.Single(estimate.Inputs);
        Assert.Equal(900_000_000, estimate.Inputs[0].AmountSatoshis);
    }

    // ─── Creating a proposal uses exactly what was estimated ───

    private static async Task<(MultisigWalletService Sut, MultisigWalletConfig Config, MultisigFakeChain Chain)> ArrangeProposalAsync(long utxoAmount)
    {
        var chain = new MultisigFakeChain(utxos: [Utxo(utxoAmount)]);
        var sut = new MultisigWalletService(new MultisigFakeStorage(), chain, new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync("Spend", 2,
            [new CoSigner { Name = "a", PublicKeyHex = new Key().PubKey.ToHex() },
             new CoSigner { Name = "b", PublicKeyHex = new Key().PubKey.ToHex() },
             new CoSigner { Name = "c", PublicKeyHex = new Key().PubKey.ToHex() }],
            ownerWalletId: "wallet-a");
        return (sut, config, chain);
    }

    [Fact]
    public async Task CreatedProposal_MatchesTheEstimate_AndHasChangeBackToTheMultisig()
    {
        var (sut, config, _) = await ArrangeProposalAsync(1_000_000_000);
        var dest = MainnetSegwit();

        var pending = await sut.CreateSpendingPSBTAsync(config, dest, 1m, 20, "Rent");

        var estimate = MultisigSendHelper.EstimateSend([Utxo(1_000_000_000)], 20, 100_000_000, 2, 3, config.AddressType, memoBytes: 4);
        Assert.Equal(estimate.FeeSatoshis, pending.FeeSatoshis);
        Assert.Equal(100_000_000, pending.AmountSatoshis);
        Assert.Equal("Rent", pending.Memo);
        Assert.False(string.IsNullOrEmpty(pending.UnsignedTxId));

        var psbt = PSBT.Parse(pending.PsbtBase64, sut.ActiveNetwork);
        var tx = psbt.GetGlobalTransaction();
        Assert.Equal(pending.UnsignedTxId, tx.GetHash().ToString());
        Assert.Equal(1_000_000_000 - 100_000_000 - pending.FeeSatoshis,
            tx.Outputs.Single(o => o.ScriptPubKey == BitcoinAddress.Create(config.Address, sut.ActiveNetwork).ScriptPubKey).Value.Satoshi);
    }

    [Fact]
    public async Task CreatedProposal_WithMax_HasNoChangeOutput()
    {
        var (sut, config, chain) = await ArrangeProposalAsync(500_000_000);
        var estimate = MultisigSendHelper.EstimateSend(chain.Utxos, 20, 0, 2, 3, config.AddressType, max: true);

        var pending = await sut.CreateSpendingPSBTAsync(config, MainnetSegwit(), estimate.AmountSatoshis / 100_000_000m, 20);

        var tx = PSBT.Parse(pending.PsbtBase64, sut.ActiveNetwork).GetGlobalTransaction();
        Assert.Single(tx.Outputs);
        Assert.Equal(500_000_000 - estimate.AmountSatoshis, pending.FeeSatoshis);
    }

    [Fact]
    public async Task CreateProposal_RejectsAnInvalidDestination_AndOverBalanceAmounts()
    {
        var (sut, config, _) = await ArrangeProposalAsync(200_000_000);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, TestnetSegwit(), 1m, 20));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, "garbage", 1m, 20));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateSpendingPSBTAsync(config, MainnetSegwit(), 5m, 20));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.CreateSpendingPSBTAsync(config, MainnetSegwit(), 0.123456789m, 20));
    }

    [Fact]
    public async Task AmountPrecision_IsNeverRounded()
    {
        var (sut, config, _) = await ArrangeProposalAsync(1_000_000_000);

        var pending = await sut.CreateSpendingPSBTAsync(config, MainnetSegwit(), 1.23456789m, 20);

        Assert.Equal(123_456_789, pending.AmountSatoshis);
    }

    [Fact]
    public async Task TwoSubmissionsWithTheSameToken_CreateOnlyOneProposal()
    {
        var (sut, config, _) = await ArrangeProposalAsync(1_000_000_000);
        var dest = MainnetSegwit();

        var first = sut.CreateSpendingPSBTAsync(config, dest, 1m, 20, null, "token-1");
        var second = sut.CreateSpendingPSBTAsync(config, dest, 1m, 20, null, "token-1");
        var results = await Task.WhenAll(first, second);

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Single(await sut.GetPendingTransactionsAsync(config.WalletId));

        // A different token is a new submission.
        var third = await sut.CreateSpendingPSBTAsync(config, dest, 1m, 20, null, "token-2");
        Assert.NotEqual(results[0].Id, third.Id);
    }
}
