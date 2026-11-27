using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static DigiByte.Pay.Api.Tests.BuyerChoiceTestKit;

namespace DigiByte.Pay.Api.Tests;

/// <summary>specs/033 US3 (T036): when POST /v1/pay/sessions honours buyerChoice, and how it starts.</summary>
public class BuyerChoiceSessionTests
{
    [Fact]
    public async Task A_fiat_priced_payment_at_a_DD_store_gets_the_choice_with_a_DGB_option()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);

        var session = await CreateChoice(client, key, storeId);

        Assert.True(session.GetProperty("buyerChoice").GetBoolean());
        Assert.Equal("DGB", session.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("lockedAt").ValueKind);
        var dgb = Assert.Single(session.GetProperty("options").EnumerateArray());
        Assert.Equal(session.GetProperty("address").GetString(), Address(dgb));
        Assert.StartsWith("digibyte:", dgb.GetProperty("uri").GetString());
        Assert.DoesNotContain(factory.DdNode.Requests, r => r.EndsWith("/address/new")); // no DD address until the buyer picks DD
    }

    public static TheoryData<string, bool, bool, bool, object> Ignored => new()
    {
        { "switch off", false, true, true, new { amount = 2500m, fiatCurrency = "USD", fiatAmount = 25m, buyerChoice = true } },
        { "DD off", true, false, true, new { amount = 2500m, fiatCurrency = "USD", fiatAmount = 25m, buyerChoice = true } },
        { "choice off", true, true, false, new { amount = 2500m, fiatCurrency = "USD", fiatAmount = 25m, buyerChoice = true } },
        { "typed in DGB", true, true, true, new { amount = 25m, buyerChoice = true } },
        { "not asked for", true, true, true, new { amount = 2500m, fiatCurrency = "USD", fiatAmount = 25m } },
    };

    [Theory]
    [MemberData(nameof(Ignored))]
    public async Task Buyer_choice_is_ignored_without_an_error_unless_every_condition_holds(string why, bool surfaces, bool ddOn, bool choiceOn, object body)
    {
        _ = why;
        using var factory = Factory(surfaces);
        var (client, key, storeId) = await DdStore(factory, ddOn);
        if (!choiceOn)
            (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { buyerChoiceEnabled = false })))
                .EnsureSuccessStatusCode();

        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["storeId"] = storeId;
        var response = await Create(client, key, json);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var session = await Json(response);
        Assert.False(session.GetProperty("buyerChoice").GetBoolean());
        Assert.Empty(session.GetProperty("options").EnumerateArray());
        Assert.Equal("DGB", session.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_store_whose_default_is_DD_starts_its_buyers_on_DD_without_locking()
    {
        using var factory = Factory();
        factory.DdNode.Addresses.Enqueue("DDstart0001");
        var (client, key, storeId) = await DdStore(factory);
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { defaultCurrency = "DD" })))
            .EnsureSuccessStatusCode();

        var session = await CreateChoice(client, key, storeId);

        Assert.Equal("DD", session.GetProperty("currency").GetString());
        Assert.Equal("DDstart0001", session.GetProperty("address").GetString());
        Assert.Equal(2500, session.GetProperty("amountCentsDd").GetInt64());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("lockedAt").ValueKind);
        Assert.Equal(2, session.GetProperty("options").GetArrayLength());
        await factory.WithDb(async db =>
            Assert.True(await db.DdAddresses.AnyAsync(a => a.Address == "DDstart0001" && a.Kind == "payment")));
    }

    [Fact]
    public async Task A_DD_default_store_starts_on_DGB_without_asking_the_node_when_it_is_down()
    {
        using var factory = Factory();
        var (client, key, storeId) = await DdStore(factory);
        (await client.SendAsync(PayFactory.Authed(HttpMethod.Patch, $"/v1/pay/stores/{storeId}", key, new { defaultCurrency = "DD" })))
            .EnsureSuccessStatusCode();
        factory.DdNode.Healthy = false;

        var session = await CreateChoice(client, key, storeId);

        Assert.Equal("DGB", session.GetProperty("currency").GetString());
        Assert.Single(session.GetProperty("options").EnumerateArray());
        Assert.DoesNotContain(factory.DdNode.Requests, r => r.EndsWith("/address/new"));
    }
}
