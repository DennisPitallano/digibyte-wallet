using DigiByte.Crypto.Networks;
using DigiByte.Web.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// What the scan panel does with a value (specs/030-desktop-paste-scan): FR-009, FR-011a, FR-027 and User Story 1,
/// scenario 6. Pasted, typed, picture, camera and screen values all go through these rules.
/// </summary>
public class ScanHandOffTests
{
    private const string App = "https://dgbwallet.app/";
    private static readonly string Address =
        new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString();
    private static readonly string Phrase = new Mnemonic(Wordlist.English, WordCount.Twelve).ToString();
    private static readonly string Wif = new Key().GetWif(DigiByteNetwork.Mainnet).ToString();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n ")]
    [InlineData("\"\"")]
    public void Nothing_usable_hands_nothing_over(string? raw)
    {
        Assert.Equal(new HandOffDecision(HandOffOutcome.Nothing, null), ScanHandOff.Decide(raw, true, true, App));
        Assert.Equal(new HandOffDecision(HandOffOutcome.Nothing, null), ScanHandOff.Decide(raw, false, true, App));
    }

    [Fact]
    public void Text_over_the_limit_is_kept_for_correction()
    {
        var raw = new string('a', 8193);

        Assert.Equal(new HandOffDecision(HandOffOutcome.TooLong, raw), ScanHandOff.Decide(raw, true, true, App));
    }

    // ----- The menu -----

    [Fact]
    public void A_recovery_phrase_in_the_menu_is_cleared_not_handed_over()
    {
        var decision = ScanHandOff.Decide("  " + Phrase + "\n", fromMenu: true, keepOpenIfUnknown: true, App);

        Assert.Equal(HandOffOutcome.SecretCleared, decision.Outcome);
        Assert.Null(decision.Value);
    }

    [Fact]
    public void A_private_key_from_the_menu_camera_is_cleared_too()
    {
        Assert.Equal(HandOffOutcome.SecretCleared, ScanHandOff.Decide(Wif, fromMenu: true, keepOpenIfUnknown: false, App).Outcome);
    }

    [Fact]
    public void Text_the_menu_cannot_open_is_kept_in_the_field()
    {
        Assert.Equal(new HandOffDecision(HandOffOutcome.Unknown, "hello world"), ScanHandOff.Decide(" 'hello world' ", true, true, App));
    }

    [Fact]
    public void An_unknown_camera_code_in_the_menu_is_still_handed_to_the_menu_which_reports_it()
    {
        Assert.Equal(new HandOffDecision(HandOffOutcome.HandOver, "hello world"), ScanHandOff.Decide("hello world", true, false, App));
    }

    [Fact]
    public void An_address_in_the_menu_is_handed_over_cleaned()
    {
        Assert.Equal(new HandOffDecision(HandOffOutcome.HandOver, Address), ScanHandOff.Decide($"\"{Address}\"\r\n", true, true, App));
    }

    [Fact]
    public void A_payment_link_in_the_menu_is_handed_over_whole_for_the_menu_to_route()
    {
        var link = $"{App}pay#digibyte:{Address}?amount=2&message=Thanks";

        Assert.Equal(new HandOffDecision(HandOffOutcome.HandOver, link), ScanHandOff.Decide(link, true, true, App));
    }

    // ----- A page's own scan button -----

    [Fact]
    public void A_page_receives_a_private_key_because_the_page_decides()
    {
        // Recover's own scan button exists to take a private key.
        Assert.Equal(new HandOffDecision(HandOffOutcome.HandOver, Wif), ScanHandOff.Decide(Wif, false, true, App));
    }

    [Fact]
    public void A_page_receives_unknown_text_and_checks_it_itself()
    {
        Assert.Equal(new HandOffDecision(HandOffOutcome.HandOver, "hello world"), ScanHandOff.Decide("hello world", false, true, App));
    }

    [Fact]
    public void A_page_receives_a_payment_link_as_its_digibyte_request_without_the_note()
    {
        var decision = ScanHandOff.Decide($"{App}pay#digibyte:{Address}?amount=2&label=Coffee&message=Thanks", false, true, App);

        Assert.Equal(HandOffOutcome.HandOver, decision.Outcome);
        Assert.StartsWith($"digibyte:{Address}?amount=2", decision.Value);
        Assert.DoesNotContain("label", decision.Value);
        Assert.DoesNotContain("message", decision.Value);
    }

    [Fact]
    public void A_look_alike_payment_link_reaches_the_page_unchanged()
    {
        var link = $"https://dgbwallet.app.evil.example/pay#digibyte:{Address}?amount=2";

        Assert.Equal(new HandOffDecision(HandOffOutcome.HandOver, link), ScanHandOff.Decide(link, false, true, App));
    }
}
