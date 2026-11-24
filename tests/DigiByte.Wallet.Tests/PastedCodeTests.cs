using DigiByte.Wallet.Services;

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
}
