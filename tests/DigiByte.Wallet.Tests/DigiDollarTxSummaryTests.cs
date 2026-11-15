using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A DigiDollar transaction in the transaction list: recognised from its outputs, shown by its DD amounts and its fee, and its "DD" marker
/// is never read as a text note.
/// </summary>
public class DigiDollarTxSummaryTests
{
    private static readonly byte[] KeyA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] KeyB = Enumerable.Range(41, 32).Select(i => (byte)i).ToArray();

    private static TxOutput Out(string scriptHex, long sats, uint index) => new() { Address = "x" + index, AmountSatoshis = sats, Index = index, ScriptHex = scriptHex };

    private static TxOutput DdOutput(byte[] key, uint index) => Out(Convert.ToHexString(DigiDollarScripts.CreateOutputScript(key).ToBytes()).ToLowerInvariant(), 0, index);

    private static TxOutput OpReturn(DigiDollarTxType type, uint index, params long[] cents) =>
        Out(Convert.ToHexString(DigiDollarScripts.BuildOpReturn(type, cents).ToBytes()).ToLowerInvariant(), 0, index);

    private static TxOutput DgbChange(uint index) => Out("00142b47e225169eb6a8e8e4e0d83e9a3daaecd8c278", 1_000_000_000, index);

    private static TxOutput TextMemo(string text, uint index) => Out("6a" + Convert.ToHexString([(byte)text.Length]) + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text)), 0, index);

    // ---------- recognising a DigiDollar transaction ----------

    [Fact]
    public void A_real_mainnet_transfer_is_read_from_its_outputs()
    {
        // 0be30f9d... on mainnet: one DD output, DGB change, and the DD OP_RETURN (marker "DD", type 2 = transfer, amount 0x01f4 = 500 cents).
        List<TxOutput> outputs =
        [
            Out("5120de97f7db8feb2239d09cfd72a4cbc5b4b83a43e6172b562bda8251321cd39332", 0, 0),
            Out("00142b47e225169eb6a8e8e4e0d83e9a3daaecd8c278", 1_239_462_265_200, 1),
            Out("6a024444010202f401", 0, 2),
        ];

        var summary = DigiDollarTxSummary.TryRead(outputs);

        Assert.NotNull(summary);
        Assert.Equal(DigiDollarTxType.Transfer, summary.Type);
        Assert.Equal([500L], summary.OutputCents);
        Assert.Equal("$5.00", summary.OutputsText);
        Assert.Equal("DigiDollar transfer", summary.Label);
    }

    [Fact]
    public void A_transfer_with_change_lists_each_dd_output_amount()
    {
        List<TxOutput> outputs = [DdOutput(KeyA, 0), DdOutput(KeyB, 1), DgbChange(2), OpReturn(DigiDollarTxType.Transfer, 3, 142, 49_858)];

        var summary = DigiDollarTxSummary.TryRead(outputs);

        Assert.NotNull(summary);
        Assert.Equal([142L, 49_858L], summary.OutputCents);
        Assert.Equal(50_000, summary.TotalCents);
        Assert.Equal("$1.42 + $498.58", summary.OutputsText);
    }

    [Fact]
    public void A_mint_is_read_even_with_the_extra_pushes_after_the_amount()
    {
        // OP_RETURN "DD" 1 <50000> <lock height> <tier> <32-byte commitment>, as seen on mainnet mints.
        var script = new Script(
            OpcodeType.OP_RETURN, Op.GetPushOp(System.Text.Encoding.ASCII.GetBytes("DD")), Op.GetPushOp(1), Op.GetPushOp(50_000),
            Op.GetPushOp(1_000), Op.GetPushOp(3), Op.GetPushOp(new byte[32]));
        List<TxOutput> outputs = [DdOutput(KeyA, 0), DgbChange(1), Out(Convert.ToHexString(script.ToBytes()), 0, 2)];

        var summary = DigiDollarTxSummary.TryRead(outputs);

        Assert.NotNull(summary);
        Assert.Equal(DigiDollarTxType.Mint, summary.Type);
        Assert.Equal([50_000L], summary.OutputCents);
        Assert.Equal("DigiDollar minted", summary.Label);
    }

    [Fact]
    public void Amounts_are_shown_with_two_decimals()
    {
        Assert.Equal("$0.01", DigiDollarTxSummary.Usd(1));
        Assert.Equal("$100,000.00", DigiDollarTxSummary.Usd(10_000_000).Replace(" ", ","));
    }

    // ---------- things that are not DigiDollar transactions ----------

    [Fact]
    public void An_ordinary_transaction_is_not_a_digidollar_transaction()
    {
        Assert.Null(DigiDollarTxSummary.TryRead([DgbChange(0), DgbChange(1)]));
        Assert.Null(DigiDollarTxSummary.TryRead([DgbChange(0), TextMemo("hello", 1)]));
    }

    [Fact]
    public void A_text_note_that_is_just_the_letters_dd_is_not_a_digidollar_transaction()
    {
        Assert.Null(DigiDollarTxSummary.TryRead([DgbChange(0), TextMemo("DD", 1)]));
    }

    [Fact]
    public void A_transaction_with_two_dd_op_returns_or_mismatched_amounts_or_no_dd_output_is_rejected()
    {
        Assert.Null(DigiDollarTxSummary.TryRead([DdOutput(KeyA, 0), OpReturn(DigiDollarTxType.Transfer, 1, 100), OpReturn(DigiDollarTxType.Transfer, 2, 100)]));
        Assert.Null(DigiDollarTxSummary.TryRead([DdOutput(KeyA, 0), OpReturn(DigiDollarTxType.Transfer, 1, 100, 200)])); // two amounts, one DD output
        Assert.Null(DigiDollarTxSummary.TryRead([DgbChange(0), OpReturn(DigiDollarTxType.Transfer, 1, 100)])); // no DD output at all
    }

    [Fact]
    public void Outputs_with_no_script_data_or_bad_hex_do_not_throw()
    {
        List<TxOutput> outputs =
        [
            new() { Address = "a", AmountSatoshis = 5, Index = 0, ScriptHex = null },
            new() { Address = "b", AmountSatoshis = 5, Index = 1, ScriptHex = "zz" },
            new() { Address = "c", AmountSatoshis = 5, Index = 2, ScriptHex = "abc" },
        ];

        Assert.Null(DigiDollarTxSummary.TryRead(outputs));
    }

    // ---------- the note a sender may have written ----------

    [Fact]
    public void The_dd_marker_is_never_returned_as_a_text_note()
    {
        List<TxOutput> outputs = [DdOutput(KeyA, 0), DgbChange(1), OpReturn(DigiDollarTxType.Transfer, 2, 500)];

        Assert.Null(WalletService.ExtractOpReturnMemo(outputs));
    }

    [Fact]
    public void A_real_text_note_is_still_returned_and_so_is_a_note_that_reads_dd()
    {
        Assert.Equal("thanks for lunch", WalletService.ExtractOpReturnMemo([DgbChange(0), TextMemo("thanks for lunch", 1)]));
        Assert.Equal("DD", WalletService.ExtractOpReturnMemo([DgbChange(0), TextMemo("DD", 1)]));
        Assert.Null(WalletService.ExtractOpReturnMemo([DgbChange(0)]));
    }
}
