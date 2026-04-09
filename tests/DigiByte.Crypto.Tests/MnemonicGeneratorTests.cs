using DigiByte.Crypto.KeyGeneration;
using NBitcoin;

namespace DigiByte.Crypto.Tests;

public class MnemonicGeneratorTests
{
    [Fact]
    public void Generate_Returns_24_Words_By_Default()
    {
        var mnemonic = MnemonicGenerator.Generate();

        var words = mnemonic.ToString().Split(' ');
        Assert.Equal(24, words.Length);
    }

    [Fact]
    public void Generate_Returns_12_Words_When_Specified()
    {
        var mnemonic = MnemonicGenerator.Generate(wordCount: WordCount.Twelve);

        var words = mnemonic.ToString().Split(' ');
        Assert.Equal(12, words.Length);
    }

    [Fact]
    public void Generate_Different_Each_Time()
    {
        var m1 = MnemonicGenerator.Generate();
        var m2 = MnemonicGenerator.Generate();

        Assert.NotEqual(m1.ToString(), m2.ToString());
    }

    [Fact]
    public void FromWords_RoundTrips_Generated_Mnemonic()
    {
        var original = MnemonicGenerator.Generate();
        var recovered = MnemonicGenerator.FromWords(original.ToString());

        Assert.Equal(original.ToString(), recovered.ToString());
    }

    [Fact]
    public void IsValid_Returns_True_For_Valid_Mnemonic()
    {
        var mnemonic = MnemonicGenerator.Generate();

        Assert.True(MnemonicGenerator.IsValid(mnemonic.ToString()));
    }

    [Fact]
    public void IsValid_Returns_False_For_Invalid_Words()
    {
        Assert.False(MnemonicGenerator.IsValid("not a valid mnemonic phrase at all"));
    }

    [Fact]
    public void IsValid_Returns_False_For_Empty_String()
    {
        Assert.False(MnemonicGenerator.IsValid(""));
    }

    [Fact]
    public void FromWords_Accepts_Known_24_Word_Mnemonic()
    {
        var words =
            "abandon abandon abandon abandon abandon abandon abandon abandon " +
            "abandon abandon abandon abandon abandon abandon abandon abandon " +
            "abandon abandon abandon abandon abandon abandon abandon art";

        var mnemonic = MnemonicGenerator.FromWords(words);

        Assert.Equal(words, mnemonic.ToString());
    }

    [Fact]
    public void Generated_Mnemonic_Passes_BIP39_Validation()
    {
        // NBitcoin validates the checksum internally; this confirms our generator
        // always produces checksummed mnemonics
        for (int i = 0; i < 5; i++)
        {
            var mnemonic = MnemonicGenerator.Generate();
            Assert.True(MnemonicGenerator.IsValid(mnemonic.ToString()));
        }
    }
}
