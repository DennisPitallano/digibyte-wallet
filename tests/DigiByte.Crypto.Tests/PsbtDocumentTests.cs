using DigiByte.Crypto.Psbt;
using NBitcoin;
using static DigiByte.Crypto.Tests.PsbtTestFixtures;

namespace DigiByte.Crypto.Tests;

public class PsbtDocumentTests
{
    private static PSBT Valid() => Builder().AddInput(Fund(Stranger(), 100_000_000)).Pay(90_000_000).Build();

    [Fact]
    public void Text_Base64_Loads()
    {
        var psbt = Valid();

        var result = PsbtDocument.FromText(psbt.ToBase64(), Net);

        Assert.Null(result.Refusal);
        Assert.Equal(PsbtSourceFormat.Text, result.Document!.SourceFormat);
        Assert.Equal(psbt.ToBase64(), result.Document.Psbt.ToBase64());
    }

    [Fact]
    public void Text_WithSurroundingWhitespace_Loads()
    {
        var result = PsbtDocument.FromText("\r\n  " + Valid().ToBase64() + "  \n", Net);

        Assert.Null(result.Refusal);
    }

    [Fact]
    public void Binary_Loads()
    {
        var result = PsbtDocument.FromBytes(Valid().ToBytes(), Net);

        Assert.Null(result.Refusal);
        Assert.Equal(PsbtSourceFormat.Binary, result.Document!.SourceFormat);
    }

    [Theory]
    [InlineData("hex")]
    [InlineData("random")]
    [InlineData("base64 of something else")]
    [InlineData("empty")]
    public void Text_ThatIsNotABase64Psbt_IsRefused(string kind)
    {
        var text = kind switch
        {
            "hex" => Valid().ToHex(),
            "random" => "this is not a psbt at all!",
            "base64 of something else" => Convert.ToBase64String("hello world, not a psbt"u8.ToArray()),
            _ => "   ",
        };

        var result = PsbtDocument.FromText(text, Net);

        Assert.Null(result.Document);
        Assert.Equal(PsbtRefusalReason.NotAPsbt, result.Refusal!.Reason);
    }

    [Fact]
    public void Binary_WithoutPsbtMagic_IsRefused()
    {
        var result = PsbtDocument.FromBytes("not a psbt"u8.ToArray(), Net);

        Assert.Equal(PsbtRefusalReason.NotAPsbt, result.Refusal!.Reason);
    }

    [Fact]
    public void Text_OverOneMegabyte_IsRefusedBeforeDecoding()
    {
        var result = PsbtDocument.FromText(new string('A', PsbtDocument.MaxBytes + 1), Net);

        Assert.Equal(PsbtRefusalReason.TooLarge, result.Refusal!.Reason);
    }

    [Fact]
    public void Binary_OverOneMegabyte_IsRefused()
    {
        var result = PsbtDocument.FromBytes(new byte[PsbtDocument.MaxBytes + 1], Net);

        Assert.Equal(PsbtRefusalReason.TooLarge, result.Refusal!.Reason);
    }

    [Fact]
    public void DuplicateInput_IsRefusedAsInvalidStructure()
    {
        var result = PsbtDocument.FromBytes(DuplicateInputBytes(), Net);

        Assert.Equal(PsbtRefusalReason.InvalidStructure, result.Refusal!.Reason);
    }

    [Fact]
    public void NoOutputs_IsRefusedAsInvalidStructure()
    {
        var psbt = Builder().AddInput(Fund(Stranger(), 100_000_000)).Build();

        var result = PsbtDocument.FromText(psbt.ToBase64(), Net);

        Assert.Equal(PsbtRefusalReason.InvalidStructure, result.Refusal!.Reason);
    }

    [Fact]
    public void NoInputs_IsRefusedAsInvalidStructure()
    {
        var result = PsbtDocument.FromBytes(NoInputsBytes(), Net);

        Assert.Equal(PsbtRefusalReason.InvalidStructure, result.Refusal!.Reason);
    }

    [Fact]
    public void BadBytesAfterTheMagic_AreRefusedAsInvalidStructure()
    {
        var bytes = Valid().ToBytes();
        var truncated = bytes[..(bytes.Length / 2)];

        var result = PsbtDocument.FromBytes(truncated, Net);

        Assert.Equal(PsbtRefusalReason.InvalidStructure, result.Refusal!.Reason);
    }
}
