using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Text that reaches the scan panel by paste, typing, a picture or the screen (specs/030-desktop-paste-scan).
/// Clean() takes off what chat apps and emails add around a code; it never changes the code itself.
/// </summary>
public class PastedCodeTests
{
    private const string Code = "dgb1qexampleaddress0000000000000000000000";

    [Theory]
    [InlineData("  " + Code + "  ")]
    [InlineData("\t" + Code + "\t")]
    [InlineData("\r\n" + Code + "\r\n")]
    [InlineData("\n \t" + Code + " \r\n ")]
    public void White_space_and_line_breaks_around_the_code_are_removed(string raw)
    {
        Assert.Equal(Code, PastedCode.Clean(raw));
    }

    [Theory]
    [InlineData("\"" + Code + "\"")]
    [InlineData("'" + Code + "'")]
    [InlineData("“" + Code + "”")]
    [InlineData("‘" + Code + "’")]
    [InlineData("`" + Code + "`")]
    [InlineData("  \"  " + Code + "  \"  ")]
    public void One_pair_of_matching_quotes_is_removed(string raw)
    {
        Assert.Equal(Code, PastedCode.Clean(raw));
    }

    [Theory]
    [InlineData("\"" + Code + "'")]
    [InlineData("\"" + Code)]
    [InlineData(Code + "\"")]
    [InlineData("“" + Code + "\"")]
    public void Quotes_that_do_not_match_are_kept(string raw)
    {
        Assert.Equal(raw, PastedCode.Clean(raw));
    }

    [Fact]
    public void Only_the_outer_pair_of_nested_quotes_is_removed()
    {
        Assert.Equal("'" + Code + "'", PastedCode.Clean("\"'" + Code + "'\""));
    }

    [Fact]
    public void Quotes_inside_the_code_are_left_alone()
    {
        var raw = "digibyte:" + Code + "?label=Bob's%20shop";
        Assert.Equal(raw, PastedCode.Clean(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    [InlineData("\"\"")]
    [InlineData("  ' '  ")]
    public void Nothing_usable_returns_null(string? raw)
    {
        Assert.Null(PastedCode.Clean(raw));
    }

    [Fact]
    public void The_size_limit_is_8192_characters()
    {
        Assert.Equal(8192, PastedCode.MaxLength);
    }

    [Fact]
    public void Text_of_exactly_the_limit_is_accepted()
    {
        var text = new string('a', 8192);
        Assert.Equal(text, PastedCode.Clean(text));
    }

    [Fact]
    public void Text_over_the_limit_returns_null()
    {
        Assert.Null(PastedCode.Clean(new string('a', 8193)));
    }

    [Fact]
    public void The_limit_applies_after_white_space_is_removed()
    {
        var text = new string('a', 8192);
        Assert.Equal(text, PastedCode.Clean("   " + text + "\r\n"));
    }

    [Fact]
    public void IsTooLong_tells_an_over_limit_text_from_an_empty_one()
    {
        Assert.True(PastedCode.IsTooLong(new string('a', 8193)));
        Assert.False(PastedCode.IsTooLong("   "));
        Assert.False(PastedCode.IsTooLong(null));
        Assert.False(PastedCode.IsTooLong(Code));
    }

    // ----- LooksSecret (FR-027): a recovery phrase or private key pasted where it does not belong -----

    [Theory]
    [InlineData(WordCount.Twelve)]
    [InlineData(WordCount.Fifteen)]
    [InlineData(WordCount.Eighteen)]
    [InlineData(WordCount.TwentyOne)]
    [InlineData(WordCount.TwentyFour)]
    public void A_recovery_phrase_of_every_standard_length_looks_secret(WordCount count)
    {
        var phrase = new Mnemonic(Wordlist.English, count).ToString();
        Assert.True(PastedCode.LooksSecret(phrase));
    }

    [Fact]
    public void A_phrase_with_a_couple_of_typos_still_looks_secret()
    {
        var words = new Mnemonic(Wordlist.English, WordCount.Twelve).Words.ToArray();
        words[0] = "zzzzq";
        words[5] = "qqqqz";
        Assert.True(PastedCode.LooksSecret(string.Join(' ', words)));
    }

    [Fact]
    public void A_phrase_split_over_lines_still_looks_secret()
    {
        var words = new Mnemonic(Wordlist.English, WordCount.Twelve).Words;
        Assert.True(PastedCode.LooksSecret(string.Join("\r\n", words)));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(25)]
    public void Other_word_counts_are_not_a_recovery_phrase(int count)
    {
        var words = new Mnemonic(Wordlist.English, WordCount.TwentyFour).Words.Concat(new Mnemonic(Wordlist.English, WordCount.TwentyFour).Words);
        Assert.False(PastedCode.LooksSecret(string.Join(' ', words.Take(count))));
    }

    [Fact]
    public void An_ordinary_twelve_word_sentence_is_not_a_recovery_phrase()
    {
        Assert.False(PastedCode.LooksSecret("send me the payment for the coffee we had together last week"));
    }

    [Fact]
    public void A_mainnet_private_key_looks_secret()
    {
        Assert.True(PastedCode.LooksSecret(new Key().GetWif(DigiByteNetwork.Mainnet).ToString()));
    }

    [Fact]
    public void A_testnet_private_key_looks_secret()
    {
        Assert.True(PastedCode.LooksSecret(new Key().GetWif(DigiByteNetwork.Testnet).ToString()));
    }

    [Fact]
    public void A_raw_64_character_hex_key_looks_secret()
    {
        Assert.True(PastedCode.LooksSecret(Convert.ToHexString(new Key().ToBytes())));
        Assert.True(PastedCode.LooksSecret(Convert.ToHexString(new Key().ToBytes()).ToLowerInvariant()));
    }

    [Fact]
    public void An_address_is_not_secret()
    {
        Assert.False(PastedCode.LooksSecret(new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString()));
        Assert.False(PastedCode.LooksSecret(new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, DigiByteNetwork.Mainnet).ToString()));
    }

    [Fact]
    public void A_payment_request_is_not_secret()
    {
        var address = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet);
        Assert.False(PastedCode.LooksSecret($"digibyte:{address}?amount=12.5&label=Coffee"));
    }

    [Fact]
    public void A_multisig_redeem_script_is_not_secret()
    {
        Assert.False(PastedCode.LooksSecret("5221" + new string('a', 66) + "21" + new string('b', 66) + "52ae"));
    }

    [Fact]
    public void A_multisig_proposal_is_not_secret()
    {
        Assert.False(PastedCode.LooksSecret("cHNidP8BAHECAAAAAbcdEFGHijklMNOPqrstUVWXyz0123456789"));
    }

    [Fact]
    public void A_DigiId_link_is_not_secret()
    {
        Assert.False(PastedCode.LooksSecret("digiid://example.com/callback?x=abc123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    public void Short_or_empty_text_is_not_secret(string text)
    {
        Assert.False(PastedCode.LooksSecret(text));
    }
}
