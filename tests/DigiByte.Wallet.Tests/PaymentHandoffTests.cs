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

    // ─── A payment names the Send it is for (specs/032-desktop-receive-everywhere, FR-002, FR-004) ───

    [Fact]
    public void A_payment_for_a_shared_wallet_is_taken_by_that_wallets_Send()
    {
        var handoff = new PaymentHandoff();
        var link = Link("dgb1qshared", 4m);
        handoff.Set(link, "ms1");

        Assert.Equal(link, handoff.Take("ms1"));
        Assert.Null(handoff.Take("ms1"));
    }

    [Fact]
    public void Another_Send_discards_a_payment_that_was_not_for_it()
    {
        var handoff = new PaymentHandoff();
        handoff.Set(Link("dgb1qshared", 4m), "ms1");
        Assert.Null(handoff.Take());          // the personal Send
        Assert.Null(handoff.Take("ms1"));     // gone: it never turns up later

        handoff.Set(Link("dgb1qpersonal", 1m));
        Assert.Null(handoff.Take("ms2"));
        Assert.Null(handoff.Take());

        handoff.Set(Link("dgb1qshared", 4m), "ms1");
        Assert.Null(handoff.Take("MS1"));     // ids are compared exactly
    }

    [Fact]
    public void A_blank_shared_wallet_id_means_the_personal_Send()
    {
        var handoff = new PaymentHandoff();
        handoff.Set(Link("dgb1qpersonal", 1m), " ");

        Assert.Equal("dgb1qpersonal", handoff.Take()?.Address);
    }
}
