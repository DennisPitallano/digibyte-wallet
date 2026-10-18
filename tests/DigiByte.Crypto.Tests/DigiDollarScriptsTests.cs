using DigiByte.Crypto.Transactions;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

/// <summary>
/// T031 (script codec): version constant, the value-0 DD output script <c>OP_1 &lt;key&gt;</c>, and the single
/// DD <c>OP_RETURN</c> that carries the amounts (research R1, from DigiByte Core txbuilder.cpp / validation.cpp).
/// </summary>
public class DigiDollarScriptsTests
{
    private static byte[] Key(int seed) =>
        new Key(Enumerable.Range(seed, 32).Select(i => (byte)i).ToArray()).PubKey.TaprootInternalKey.ToBytes();

    private static string Hex(Script s) => s.ToHex();

    // ---------- version ----------

    [Fact]
    public void TransferVersion_Is_0x02000770()
    {
        Assert.Equal(0x02000770u, DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer));
    }

    [Theory]
    [InlineData(DigiDollarTxType.Mint, 0x01000770u)]
    [InlineData(DigiDollarTxType.Redeem, 0x03000770u)]
    public void MakeVersion_PutsTheTypeInTheHighByte(DigiDollarTxType type, uint expected) =>
        Assert.Equal(expected, DigiDollarScripts.MakeVersion(type));

    [Fact]
    public void MakeVersion_PutsFlagsInBits16To23() =>
        Assert.Equal(0x02050770u, DigiDollarScripts.MakeVersion(DigiDollarTxType.Transfer, flags: 5));

    [Theory]
    [InlineData(0x02000770u, true)]
    [InlineData(0x0D1D0770u, true)]   // the marker constant itself (low 16 bits only are compared)
    [InlineData(0x00000770u, true)]
    [InlineData(2u, false)]            // an ordinary tx version
    [InlineData(1u, false)]
    [InlineData(0x02000771u, false)]
    public void IsDigiDollarVersion_ComparesOnlyTheLow16Bits(uint version, bool expected) =>
        Assert.Equal(expected, DigiDollarScripts.IsDigiDollarVersion(version));

    [Theory]
    [InlineData(0x02000770u, DigiDollarTxType.Transfer)]
    [InlineData(0x01000770u, DigiDollarTxType.Mint)]
    [InlineData(0x03000770u, DigiDollarTxType.Redeem)]
    [InlineData(0x00000770u, DigiDollarTxType.None)]
    public void GetTxType_ReadsTheHighByte(uint version, DigiDollarTxType expected) =>
        Assert.Equal(expected, DigiDollarScripts.GetTxType(version));

    // ---------- output script ----------

    [Fact]
    public void OutputScript_Is_Op1_Push32()
    {
        var key = Key(1);
        var script = DigiDollarScripts.CreateOutputScript(key);

        Assert.Equal("5120" + Convert.ToHexString(key).ToLowerInvariant(), Hex(script));
    }

    [Fact]
    public void OutputScript_RoundTrips_ThroughTryGetOutputKey()
    {
        var key = Key(9);
        Assert.True(DigiDollarScripts.TryGetOutputKey(DigiDollarScripts.CreateOutputScript(key), out var parsed));
        Assert.Equal(key, parsed);
    }

    [Fact]
    public void TryGetOutputKey_RejectsNonP2TR()
    {
        var key = new Key();
        Assert.False(DigiDollarScripts.TryGetOutputKey(key.PubKey.WitHash.ScriptPubKey, out _)); // P2WPKH
        Assert.False(DigiDollarScripts.TryGetOutputKey(new Script(OpcodeType.OP_2, Op.GetPushOp(Key(1))), out _)); // witness v2
        Assert.False(DigiDollarScripts.TryGetOutputKey(new Script(OpcodeType.OP_1, Op.GetPushOp(new byte[33])), out _)); // wrong length
        Assert.False(DigiDollarScripts.TryGetOutputKey(Script.Empty, out _));
        Assert.False(DigiDollarScripts.TryGetOutputKey(new Script(OpcodeType.OP_RETURN), out _));
    }

    [Fact]
    public void CreateOutputScript_RejectsWrongKeyLength() =>
        Assert.Throws<ArgumentException>(() => DigiDollarScripts.CreateOutputScript(new byte[31]));

    // ---------- OP_RETURN ----------

    [Fact]
    public void BuildOpReturn_Matches_CoreByteLayout()
    {
        // OP_RETURN, push "DD", push CScriptNum(2), push CScriptNum(500)=f401, push CScriptNum(2000)=d007
        var script = DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [500, 2000]);

        Assert.Equal("6a" + "024444" + "0102" + "02f401" + "02d007", Hex(script));
    }

    [Theory]
    [InlineData(1L, "0101")]
    [InlineData(100L, "0164")]
    [InlineData(127L, "017f")]
    [InlineData(128L, "028000")]        // high bit set: extra zero byte keeps it positive
    [InlineData(255L, "02ff00")]
    [InlineData(256L, "020001")]
    [InlineData(32768L, "03008000")]
    [InlineData(10_000_000L, "0480969800")] // $100,000.00 in cents = 0x989680; top byte 0x98 has the high bit set, so a sign byte is appended
    public void AmountEncoding_Is_MinimalScriptNum(long cents, string expectedPush)
    {
        var script = DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [cents]);
        Assert.EndsWith(expectedPush, Hex(script));
    }

    [Fact]
    public void OpReturn_RoundTrips_AmountsInOrder()
    {
        var amounts = new long[] { 100, 255, 128, 10_000_000, 1, 99_999 };
        var script = DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, amounts);

        Assert.True(DigiDollarScripts.TryParseOpReturn(script, out var type, out var parsed));
        Assert.Equal(DigiDollarTxType.Transfer, type);
        Assert.Equal(amounts, parsed);
    }

    [Fact]
    public void TryParseOpReturn_ToleratesSmallIntegerOpcodes()
    {
        // Some encoders emit OP_2 for the type and OP_5 for a 5-cent amount; consensus reads CScriptNum data pushes,
        // the indexer tolerates both.
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DD"u8.ToArray()), OpcodeType.OP_2, Op.GetPushOp(new byte[] { 0x64 }));

        Assert.True(DigiDollarScripts.TryParseOpReturn(script, out var type, out var amounts));
        Assert.Equal(DigiDollarTxType.Transfer, type);
        Assert.Equal(new long[] { 100 }, amounts);
    }

    // ---------- negative paths (Constitution III) ----------

    [Fact]
    public void TryParseOpReturn_RejectsNonOpReturn()
    {
        Assert.False(DigiDollarScripts.TryParseOpReturn(DigiDollarScripts.CreateOutputScript(Key(1)), out _, out _));
        Assert.False(DigiDollarScripts.TryParseOpReturn(Script.Empty, out _, out _));
    }

    [Fact]
    public void TryParseOpReturn_RejectsWrongMarker()
    {
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DX"u8.ToArray()), Op.GetPushOp(new byte[] { 2 }), Op.GetPushOp(new byte[] { 0x64 }));
        Assert.False(DigiDollarScripts.TryParseOpReturn(script, out _, out _));
    }

    [Fact]
    public void TryParseOpReturn_RejectsNoAmounts()
    {
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DD"u8.ToArray()), Op.GetPushOp(new byte[] { 2 }));
        Assert.False(DigiDollarScripts.TryParseOpReturn(script, out _, out _));
    }

    [Fact]
    public void TryParseOpReturn_RejectsMissingType()
    {
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DD"u8.ToArray()));
        Assert.False(DigiDollarScripts.TryParseOpReturn(script, out _, out _));
    }

    [Theory]
    [InlineData(new byte[0])]              // zero amount
    [InlineData(new byte[] { 0x81 })]      // negative one
    [InlineData(new byte[] { 0x00, 0x80 })] // negative zero
    public void TryParseOpReturn_RejectsZeroOrNegativeAmounts(byte[] push)
    {
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DD"u8.ToArray()), Op.GetPushOp(new byte[] { 2 }), Op.GetPushOp(push));
        Assert.False(DigiDollarScripts.TryParseOpReturn(script, out _, out _));
    }

    [Fact]
    public void TryParseOpReturn_RejectsAmountPushLongerThan8Bytes()
    {
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DD"u8.ToArray()), Op.GetPushOp(new byte[] { 2 }),
            Op.GetPushOp(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.False(DigiDollarScripts.TryParseOpReturn(script, out _, out _));
    }

    [Fact]
    public void TryParseOpReturn_RejectsNonPushOpcodesAfterTheHeader()
    {
        var script = new Script(OpcodeType.OP_RETURN, Op.GetPushOp("DD"u8.ToArray()), Op.GetPushOp(new byte[] { 2 }),
            Op.GetPushOp(new byte[] { 0x64 }), OpcodeType.OP_ADD);
        Assert.False(DigiDollarScripts.TryParseOpReturn(script, out _, out _));
    }

    [Fact]
    public void BuildOpReturn_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => DigiDollarScripts.BuildOpReturn(DigiDollarTxType.Transfer, [-5]));
        Assert.Throws<ArgumentException>(() => DigiDollarScripts.BuildOpReturn(DigiDollarTxType.None, [100]));
    }
}
