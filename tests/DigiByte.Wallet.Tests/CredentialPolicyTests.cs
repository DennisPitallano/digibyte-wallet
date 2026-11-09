using System.Text.Json;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

public class CredentialPolicyTests
{
    private sealed class FakeList(params string[] entries) : ICommonPasswordList
    {
        private readonly HashSet<string> _set = new(entries);
        public bool Contains(string lowercased) => _set.Contains(lowercased);
    }

    private static readonly ICommonPasswordList Common = new FakeList("correct horse battery staple", "letmein-letmein-letmein");

    [Fact]
    public void Accepts_a_long_unusual_passphrase()
    {
        var r = CredentialPolicy.Check("violet kettle 7 orbit lantern", Common);
        Assert.True(r.Accepted);
        Assert.Equal(PassphraseRejection.None, r.Reason);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")] // 11
    public void Rejects_under_twelve_characters(string text)
    {
        var r = CredentialPolicy.Check(text, Common);
        Assert.False(r.Accepted);
        Assert.Equal(PassphraseRejection.TooShort, r.Reason);
    }

    [Fact]
    public void Accepts_exactly_twelve_characters()
    {
        Assert.True(CredentialPolicy.Check("violet-kettl", Common).Accepted);
    }

    [Fact]
    public void Length_is_counted_after_normalisation_so_a_compatibility_form_does_not_pad_the_length()
    {
        // Eleven full-width letters normalise to eleven ASCII letters: still too short.
        var fullWidth = new string(Enumerable.Range(0, 11).Select(i => (char)(0xFF41 + i)).ToArray());
        Assert.Equal(PassphraseRejection.TooShort, CredentialPolicy.Check(fullWidth, Common).Reason);
    }

    [Fact]
    public void Exactly_256_characters_is_accepted_and_257_is_not()
    {
        string Make(int n) => string.Join("", Enumerable.Range(0, n).Select(i => (char)('a' + (i * 7 + i / 5) % 26)));
        Assert.True(CredentialPolicy.Check(Make(256), Common).Accepted);
        Assert.Equal(PassphraseRejection.TooLong, CredentialPolicy.Check(Make(257), Common).Reason);
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("Correct Horse Battery Staple")] // case-insensitive
    public void Rejects_entries_on_the_common_list(string text)
    {
        Assert.Equal(PassphraseRejection.CommonPassword, CredentialPolicy.Check(text, Common).Reason);
    }

    [Fact]
    public void Common_list_check_is_skipped_when_no_list_is_available_but_other_rules_still_apply()
    {
        Assert.True(CredentialPolicy.Check("correct horse battery staple", null).Accepted);
        Assert.Equal(PassphraseRejection.TooShort, CredentialPolicy.Check("short", null).Reason);
    }

    [Theory]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("abababababab")]
    [InlineData("passwordpassword")]
    [InlineData("correct horse correct horse ")]
    public void Rejects_trivially_repeated_strings(string text)
    {
        Assert.Equal(PassphraseRejection.Repeated, CredentialPolicy.Check(text, Common).Reason);
    }

    [Theory]
    [InlineData("abcdefghijkl")]
    [InlineData("zyxwvutsrqpo")]
    [InlineData("123456789012")]
    [InlineData("789012345678")] // digits wrap 9 to 0
    public void Rejects_sequential_strings(string text)
    {
        Assert.Equal(PassphraseRejection.Sequential, CredentialPolicy.Check(text, Common).Reason);
    }

    [Fact]
    public void Composed_and_decomposed_forms_of_the_same_text_are_equal_after_normalisation()
    {
        var composed = "café au lait demain";
        var decomposed = "café au lait demain";
        Assert.Equal(CredentialPolicy.Normalise(composed), CredentialPolicy.Normalise(decomposed));
        Assert.Equal(CredentialPolicy.Check(composed, Common).Accepted, CredentialPolicy.Check(decomposed, Common).Accepted);
    }

    [Fact]
    public void Leading_and_trailing_spaces_are_preserved_as_typed()
    {
        Assert.Equal("  violet kettle 7 orbit  ", CredentialPolicy.Normalise("  violet kettle 7 orbit  "));
        Assert.NotEqual(CredentialPolicy.Normalise("violet kettle 7 orbit"), CredentialPolicy.Normalise(" violet kettle 7 orbit"));
    }

    [Fact]
    public void Emoji_and_non_ascii_are_accepted_and_counted_as_characters()
    {
        Assert.True(CredentialPolicy.Check("☕☕ café 你好 tren", Common).Accepted);
    }

    [Fact]
    public void Every_rejection_has_a_specific_locale_key()
    {
        var keys = new[]
        {
            PassphraseRejection.TooShort, PassphraseRejection.TooLong, PassphraseRejection.CommonPassword,
            PassphraseRejection.Repeated, PassphraseRejection.Sequential,
        }.Select(CredentialPolicy.ReasonKey).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.StartsWith("passphrase.reject.", k));
    }

    [Fact]
    public void Normalisation_matches_the_shared_vectors_also_used_by_the_JS_tests()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "normalisation-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var v in doc.RootElement.EnumerateArray())
            Assert.Equal(v.GetProperty("nfkc").GetString(), CredentialPolicy.Normalise(v.GetProperty("input").GetString()!));
    }

    [Fact]
    public void A_six_digit_pin_is_valid_and_anything_else_is_not()
    {
        Assert.True(CredentialPolicy.IsValidPin("123456"));
        Assert.False(CredentialPolicy.IsValidPin("12345"));
        Assert.False(CredentialPolicy.IsValidPin("1234567"));
        Assert.False(CredentialPolicy.IsValidPin("12345a"));
        Assert.False(CredentialPolicy.IsValidPin(""));
    }
}
