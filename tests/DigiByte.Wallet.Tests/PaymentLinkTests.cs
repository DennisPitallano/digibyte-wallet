using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Payment links (specs/030-desktop-paste-scan, FR-019 to FR-024): a web link to this wallet whose payment details
/// sit after the #, so the web host never receives them. A link is used only when every check passes; otherwise
/// nothing at all is taken from it.
/// </summary>
public class PaymentLinkTests
{
    private static readonly Uri Base = new("https://dgbwallet.app/");
    private static readonly string Address =
        new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Mainnet).ToString();
    private static readonly string TestnetAddress =
        new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByteNetwork.Testnet).ToString();

    private static PaymentRequest Request(decimal? amount = null, string? label = null, string? message = null) => new()
    {
        Id = "r1",
        Address = Address,
        AmountDgb = amount,
        Label = label,
        Message = message,
    };

    private static PaymentLinkData? Read(string text, Network? network = null) =>
        PaymentLink.TryRead(text, Base, network, out var data) ? data : null;

    // ----- Create -----

    [Fact]
    public void A_link_is_the_wallets_pay_page_with_the_payment_request_after_the_hash()
    {
        var request = Request(12.5m, "Coffee");

        Assert.Equal("https://dgbwallet.app/pay#" + request.ToUri(), PaymentLink.Create(Base, request));
    }

    [Fact]
    public void Nothing_about_the_payment_is_before_the_hash()
    {
        var link = PaymentLink.Create(Base, Request(12.5m, "Coffee", "Thanks"));
        var beforeHash = link[..link.IndexOf('#')];

        Assert.Equal("https://dgbwallet.app/pay", beforeHash);
    }

    // ----- TryRead: what is accepted -----

    [Fact]
    public void An_address_only_link_round_trips()
    {
        var data = Read(PaymentLink.Create(Base, Request()));

        Assert.NotNull(data);
        Assert.Equal(Address, data.Address);
        Assert.Null(data.AmountDgb);
    }

    [Fact]
    public void An_address_and_amount_round_trip()
    {
        var data = Read(PaymentLink.Create(Base, Request(12.5m)));

        Assert.NotNull(data);
        Assert.Equal(12.5m, data.AmountDgb);
    }

    [Fact]
    public void Label_and_message_round_trip()
    {
        var data = Read(PaymentLink.Create(Base, Request(1m, "Coffee & cake", "Table 4, thanks!")));

        Assert.NotNull(data);
        Assert.Equal("Coffee & cake", data.Label);
        Assert.Equal("Table 4, thanks!", data.Message);
    }

    [Fact]
    public void A_bare_digibyte_request_is_read()
    {
        var data = Read($"digibyte:{Address}?amount=3");

        Assert.NotNull(data);
        Assert.Equal(Address, data.Address);
        Assert.Equal(3m, data.AmountDgb);
    }

    [Fact]
    public void A_web_plus_digibyte_request_is_read()
    {
        Assert.Equal(Address, Read($"web+digibyte:{Address}?amount=3")?.Address);
    }

    [Fact]
    public void An_escaped_web_plus_digibyte_request_is_read()
    {
        Assert.Equal(Address, Read(Uri.EscapeDataString($"web+digibyte:{Address}?amount=3"))?.Address);
    }

    [Fact]
    public void A_testnet_address_is_read_when_no_network_is_given()
    {
        Assert.Equal(TestnetAddress, Read($"digibyte:{TestnetAddress}")?.Address);
    }

    // ----- TryRead: what is refused, with nothing taken -----

    [Theory]
    [InlineData("https://dgbwallet.app.evil.example/pay#")]
    [InlineData("https://evil.example/pay#")]
    [InlineData("https://dgbwallet.app:8443/pay#")]
    [InlineData("http://dgbwallet.app/pay#")]
    [InlineData("https://dgbwallet.app/send#")]
    [InlineData("https://dgbwallet.app/pay/x#")]
    public void A_link_to_any_other_place_is_refused(string prefix)
    {
        Assert.Null(Read(prefix + $"digibyte:{Address}?amount=1"));
    }

    [Fact]
    public void A_link_with_the_payment_in_the_query_is_refused()
    {
        Assert.Null(Read($"https://dgbwallet.app/pay?uri=digibyte:{Address}"));
    }

    [Fact]
    public void An_address_with_one_changed_character_is_refused()
    {
        var last = Address[^1];
        var changed = Address[..^1] + (last == 'q' ? 'p' : 'q');

        Assert.Null(Read($"digibyte:{changed}?amount=1"));
    }

    [Fact]
    public void A_testnet_address_is_refused_on_mainnet()
    {
        Assert.Null(Read($"digibyte:{TestnetAddress}", DigiByteNetwork.Mainnet));
    }

    [Fact]
    public void An_address_over_128_characters_is_refused()
    {
        Assert.Null(Read("digibyte:" + new string('q', 129)));
    }

    [Theory]
    [InlineData("digibyte:")]
    [InlineData("digibyte:?amount=1")]
    [InlineData("https://dgbwallet.app/pay#")]
    [InlineData("https://dgbwallet.app/pay")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    public void A_missing_address_is_refused(string text)
    {
        Assert.Null(Read(text));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("0.123456789")]
    [InlineData("21000000000")]
    [InlineData("abc")]
    [InlineData("")]
    public void An_amount_that_is_not_a_valid_payment_is_refused(string amount)
    {
        Assert.Null(Read($"digibyte:{Address}?amount={amount}"));
    }

    [Theory]
    [InlineData("0.00000001", "0.00000001")]
    [InlineData("20999999999.99999999", "20999999999.99999999")]
    [InlineData("12.5", "12.5")]
    public void Amounts_at_the_edges_are_accepted(string amount, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Read($"digibyte:{Address}?amount={amount}")?.AmountDgb);
    }

    [Fact]
    public void A_label_over_256_characters_is_refused()
    {
        Assert.Null(Read($"digibyte:{Address}?label=" + new string('a', 257)));
        Assert.NotNull(Read($"digibyte:{Address}?label=" + new string('a', 256)));
    }

    [Fact]
    public void A_message_over_256_characters_is_refused()
    {
        Assert.Null(Read($"digibyte:{Address}?message=" + new string('a', 257)));
    }

    [Fact]
    public void Control_characters_are_removed_from_label_and_message()
    {
        var data = Read($"digibyte:{Address}?label=" + Uri.EscapeDataString("Cof\u0000fee\r\n") + "&message=" + Uri.EscapeDataString("a\tb\u001bc"));

        Assert.NotNull(data);
        Assert.Equal("Coffee", data.Label);
        Assert.Equal("abc", data.Message);
    }

    // ----- SendPath and ToBip21 -----

    [Fact]
    public void SendPath_carries_address_and_an_invariant_amount()
    {
        var data = Read($"digibyte:{Address}?amount=12.5")!;

        Assert.Equal($"/send?address={Address}&amount=12.5", PaymentLink.SendPath(data));
    }

    [Fact]
    public void SendPath_leaves_out_a_missing_amount()
    {
        Assert.Equal($"/send?address={Address}", PaymentLink.SendPath(Read($"digibyte:{Address}")!));
    }

    [Fact]
    public void ToBip21_without_the_note_drops_label_and_message()
    {
        var data = Read(PaymentLink.Create(Base, Request(2m, "Coffee", "Thanks")))!;

        var back = PaymentRequest.FromUri(data.ToBip21(includeNote: false));

        Assert.NotNull(back);
        Assert.Equal(Address, back.Address);
        Assert.Equal(2m, back.AmountDgb);
        Assert.Null(back.Label);
        Assert.Null(back.Message);
    }

    [Fact]
    public void ToBip21_keeps_label_and_message_by_default()
    {
        var data = Read(PaymentLink.Create(Base, Request(2m, "Coffee", "Thanks")))!;

        var back = PaymentRequest.FromUri(data.ToBip21());

        Assert.Equal("Coffee", back!.Label);
        Assert.Equal("Thanks", back.Message);
    }
}
