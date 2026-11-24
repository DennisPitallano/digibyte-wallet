using DigiByte.Wallet.Services;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The in-memory hand-off from a payment link's /pay page to Send (specs/030-desktop-paste-scan, FR-021): taken
/// once, then gone, so a reload of Send starts empty.
/// </summary>
public class PaymentHandoffTests
{
    private static PaymentLinkData Link(string address, decimal? amount = null) => new(address, amount, null, null);

    [Fact]
    public void Nothing_is_waiting_at_first()
    {
        Assert.Null(new PaymentHandoff().Take());
    }

    [Fact]
    public void A_payment_is_taken_once_then_gone()
    {
        var handoff = new PaymentHandoff();
        var link = Link("dgb1qexample", 2m);
        handoff.Set(link);

        Assert.Equal(link, handoff.Take());
        Assert.Null(handoff.Take());
    }

    [Fact]
    public void A_new_payment_replaces_one_that_was_waiting()
    {
        var handoff = new PaymentHandoff();
        handoff.Set(Link("dgb1qfirst", 1m));
        handoff.Set(Link("dgb1qsecond", 5m));

        Assert.Equal("dgb1qsecond", handoff.Take()?.Address);
        Assert.Null(handoff.Take());
    }
}
