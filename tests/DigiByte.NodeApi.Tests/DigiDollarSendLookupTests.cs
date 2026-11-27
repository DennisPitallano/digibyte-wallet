using System.Text.Json;
using DigiByte.NodeApi.Endpoints;
using DigiByte.NodeApi.Middleware;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// specs/034 contract C4: <c>POST /send</c> returns a stable <c>{ txid }</c>, and <c>GET /sends</c> lists recent sends
/// with their comment so DigiPay can tell whether a payout reached the network.
/// </summary>
public class DigiDollarSendLookupTests
{
    private const string Txid = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Send_result_can_be_a_bare_txid()
    {
        Assert.Equal(Txid, DigiDollarEndpoints.ParseSendTxid(Parse($"\"{Txid}\"")));
    }

    [Fact]
    public void Send_result_can_be_an_object_with_txid()
    {
        Assert.Equal(Txid, DigiDollarEndpoints.ParseSendTxid(Parse($$"""{ "txid": "{{Txid.ToUpperInvariant()}}", "fee": 0.1 }""")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"not-a-txid\"")]
    [InlineData("{ \"hash\": \"abc\" }")]
    [InlineData("[]")]
    public void Anything_else_is_unparsed(string json)
    {
        Assert.Null(DigiDollarEndpoints.ParseSendTxid(Parse(json)));
    }

    [Fact]
    public void Sends_are_mapped_newest_first_with_positive_cents()
    {
        var txs = Parse($$"""
            [
              { "txid": "{{Txid}}", "address": "TD1old", "amount": -450, "comment": "dpo_old", "time": 1000, "confirmations": 9 },
              { "txid": "ff{{Txid[2..]}}", "address": "TD1new", "amount": -100, "time": 2000, "confirmations": 0 },
              { "address": "no-txid", "amount": -1, "time": 3000 }
            ]
            """);

        var items = DigiDollarEndpoints.MapSends(txs, since: 0);

        Assert.Equal(2, items.Count);
        Assert.Equal("TD1new", items[0].Address);
        Assert.Null(items[0].Comment);
        Assert.Equal(100, items[0].AmountCents);
        Assert.Equal("dpo_old", items[1].Comment);
        Assert.Equal(450, items[1].AmountCents);
        Assert.Equal(9, items[1].Confirmations);
    }

    [Fact]
    public void Sends_older_than_since_are_dropped_and_wrapped_arrays_are_read()
    {
        var txs = Parse($$"""{ "transactions": [ { "txid": "{{Txid}}", "address": "TD1", "amount_cents": 500, "time": 1000 } ] }""");

        Assert.Single(DigiDollarEndpoints.MapSends(txs, since: 999));
        Assert.Empty(DigiDollarEndpoints.MapSends(txs, since: 1001));
    }

    [Fact]
    public void Sends_route_is_wallet_scoped()
    {
        Assert.True(WalletScopedRoutes.IsWalletScoped("/api/digidollar/sends"));
    }
}
