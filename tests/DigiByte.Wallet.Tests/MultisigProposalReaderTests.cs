using DigiByte.Crypto.Multisig;
using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Reading a payment's facts from the PSBT itself, never from what a signing room says (specs/017, T005, research R5).
/// </summary>
public class MultisigProposalReaderTests
{
    private static readonly Network Net = DigiByteNetwork.Mainnet;

    private sealed record Rig(MultisigWalletService Sut, MultisigWalletConfig Config, Key[] Keys);

    private static async Task<Rig> ArrangeAsync(string addressType = "p2sh-p2wsh", long utxo = 1_000_000_000)
    {
        var keys = new[] { new Key(), new Key(), new Key() };
        var chain = new MultisigFakeChain(utxos:
        [
            new UtxoInfo { TxId = RandomUtils.GetUInt256().ToString(), OutputIndex = 0, AmountSatoshis = utxo, ScriptPubKey = "", Confirmations = 10 },
        ]);
        var sut = new MultisigWalletService(new MultisigFakeStorage(), chain, new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync("Reader", 2,
            keys.Select((k, i) => new CoSigner { Name = $"S{i}", PublicKeyHex = k.PubKey.ToHex(), IsLocal = i == 0 }).ToList(),
            addressType, "owner");
        return new Rig(sut, config, keys);
    }

    private static string Destination() => new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Net).ToString();

    [Theory]
    [InlineData("p2sh-p2wsh")]
    [InlineData("p2wsh")]
    public async Task Reads_destination_amount_change_fee_and_memo(string addressType)
    {
        var rig = await ArrangeAsync(addressType);
        var dest = Destination();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, dest, 1m, 10, memo: "rent");

        var facts = MultisigProposalReader.Read(pending.PsbtBase64, rig.Config, Net);

        Assert.Empty(facts.Problems);
        Assert.Equal(pending.UnsignedTxId, facts.UnsignedTxId);
        Assert.Equal(dest, facts.DestinationAddress);
        Assert.Equal(100_000_000, facts.AmountSat);
        Assert.Equal(pending.FeeSatoshis, facts.FeeSat);
        Assert.Equal(1_000_000_000 - 100_000_000 - pending.FeeSatoshis, facts.ChangeSat);
        Assert.Equal("rent", facts.Memo);
        Assert.Single(facts.Inputs);
        Assert.True(facts.FeeSat > 0);
    }

    [Fact]
    public async Task A_foreign_input_is_a_problem()
    {
        var rig = await ArrangeAsync();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        var psbt = PSBT.Parse(pending.PsbtBase64, Net);
        psbt.Inputs[0].WitnessUtxo = new TxOut(Money.Coins(10), new Key().PubKey.WitHash.ScriptPubKey);

        var facts = MultisigProposalReader.Read(psbt.ToBase64(), rig.Config, Net);

        Assert.Contains(facts.Problems, p => p.Contains("not this multisig"));
    }

    [Fact]
    public async Task A_missing_spent_amount_is_a_problem()
    {
        var rig = await ArrangeAsync();
        var pending = await rig.Sut.CreateSpendingPSBTAsync(rig.Config, Destination(), 1m, 10);
        var psbt = PSBT.Parse(pending.PsbtBase64, Net);
        psbt.Inputs[0].WitnessUtxo = null;

        var facts = MultisigProposalReader.Read(psbt.ToBase64(), rig.Config, Net);

        Assert.Contains(facts.Problems, p => p.Contains("amount"));
    }

    /// <summary>A PSBT spending one 10 DGB coin of the multisig and paying <paramref name="outputs"/> as given.</summary>
    private static string PsbtPaying(Rig rig, params (Script Script, long Sat)[] outputs)
    {
        var redeem = new Script(Convert.FromHexString(rig.Config.RedeemScriptHex));
        var address = BitcoinAddress.Create(rig.Config.Address, Net);
        var coin = new Coin(RandomUtils.GetUInt256(), 0, Money.Coins(10), address.ScriptPubKey);
        var tx = Net.CreateTransaction();
        tx.Inputs.Add(new TxIn(coin.Outpoint));
        foreach (var (script, sat) in outputs) tx.Outputs.Add(Money.Satoshis(sat), script);
        var psbt = PSBT.FromTransaction(tx, Net);
        psbt.AddCoins(coin.ToScriptCoin(redeem));
        return psbt.ToBase64();
    }

    private static Script To(string address) => BitcoinAddress.Create(address, Net).ScriptPubKey;

    // ─── Several recipients (specs/029-multisig-multiple-recipients) ───

    [Fact]
    public async Task Several_recipients_are_read_in_order_without_change_or_memo()
    {
        var rig = await ArrangeAsync();
        var (a, b, c) = (Destination(), Destination(), Destination());
        var memo = TxNullDataTemplate.Instance.GenerateScriptPubKey(System.Text.Encoding.UTF8.GetBytes("payroll"));

        var facts = MultisigProposalReader.Read(PsbtPaying(rig,
            (To(a), 100_000_000), (To(b), 200_000_000), (memo, 0), (To(c), 50_000_000), (To(rig.Config.Address), 600_000_000)),
            rig.Config, Net);

        Assert.Empty(facts.Problems);
        Assert.Empty(facts.Warnings);
        Assert.Equal([new ProposalRecipient(a, 100_000_000), new(b, 200_000_000), new(c, 50_000_000)], facts.Recipients);
        Assert.Equal(a, facts.DestinationAddress);
        Assert.Equal(350_000_000, facts.AmountSat);
        Assert.Equal(600_000_000, facts.ChangeSat);
        Assert.Equal("payroll", facts.Memo);
        Assert.Equal(1_000_000_000 - 350_000_000 - 600_000_000, facts.FeeSat);
    }

    [Fact]
    public async Task Rule_breaking_lines_are_warnings_never_problems()
    {
        var rig = await ArrangeAsync();
        var (a, b) = (Destination(), Destination());

        var facts = MultisigProposalReader.Read(PsbtPaying(rig,
            (To(a), 100_000_000), (To(b), 50_000), (To(a), 200_000_000)), rig.Config, Net);

        Assert.Empty(facts.Problems);
        Assert.Equal(3, facts.Recipients.Count);
        Assert.Equal(
            [new ProposalWarning(ProposalWarningKind.RecipientBelowMinimum, 1), new ProposalWarning(ProposalWarningKind.RecipientRepeated, 2)],
            facts.Warnings);
    }

    [Fact]
    public async Task More_than_twenty_recipients_is_a_warning()
    {
        var rig = await ArrangeAsync();
        var outputs = Enumerable.Range(0, 21).Select(_ => (To(Destination()), 1_000_000L)).ToArray();

        var facts = MultisigProposalReader.Read(PsbtPaying(rig, outputs), rig.Config, Net);

        Assert.Empty(facts.Problems);
        Assert.Equal(21, facts.Recipients.Count);
        Assert.Equal([new ProposalWarning(ProposalWarningKind.TooManyRecipients, null)], facts.Warnings);
    }

    [Fact]
    public async Task No_recipient_and_an_unreadable_recipient_are_still_problems()
    {
        var rig = await ArrangeAsync();

        var onlyChange = MultisigProposalReader.Read(PsbtPaying(rig, (To(rig.Config.Address), 900_000_000)), rig.Config, Net);
        var unreadable = MultisigProposalReader.Read(PsbtPaying(rig,
            (To(Destination()), 100_000_000), (new Script(OpcodeType.OP_TRUE), 100_000_000)), rig.Config, Net);

        Assert.Contains(onlyChange.Problems, p => p.Contains("no recipient"));
        Assert.Contains(unreadable.Problems, p => p.Contains("can't be read"));
    }

    [Fact]
    public async Task Outputs_above_inputs_are_a_problem()
    {
        var rig = await ArrangeAsync();
        var redeem = new Script(Convert.FromHexString(rig.Config.RedeemScriptHex));
        var address = BitcoinAddress.Create(rig.Config.Address, Net);
        var coin = new Coin(RandomUtils.GetUInt256(), 0, Money.Coins(1), address.ScriptPubKey);
        var tx = Net.CreateTransaction();
        tx.Inputs.Add(new TxIn(coin.Outpoint));
        tx.Outputs.Add(Money.Coins(2), BitcoinAddress.Create(Destination(), Net));
        var psbt = PSBT.FromTransaction(tx, Net);
        psbt.AddCoins(coin.ToScriptCoin(redeem));

        var facts = MultisigProposalReader.Read(psbt.ToBase64(), rig.Config, Net);

        Assert.Contains(facts.Problems, p => p.Contains("fee"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a psbt")]
    public async Task Unreadable_data_is_a_problem(string garbage)
    {
        var rig = await ArrangeAsync();
        var facts = MultisigProposalReader.Read(garbage, rig.Config, Net);
        Assert.Contains(facts.Problems, p => p.Contains("not a valid"));
    }
}
