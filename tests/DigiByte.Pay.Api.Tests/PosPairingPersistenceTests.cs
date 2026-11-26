using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Tests;

/// <summary>
/// POS pairing codes live in the database (PosPairings): they survive a restart, work across
/// API instances, are single-use, expire, and never store the code or a key secret.
/// </summary>
public class PosPairingPersistenceTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static PayFactory OnDb(string dbPath) =>
        PayFactory.Create(settings: new Dictionary<string, string?> { ["DigiPay:DbPath"] = dbPath });

    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"digipay-pairing-{Guid.NewGuid():N}.db");

    private static void DeleteDb(string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(dbPath); } catch { /* best effort */ }
    }

    /// <summary>Makes a pairing code; returns the code and the key prefix the response promised.</summary>
    private static async Task<(string Code, string Prefix)> CreatePairing(HttpClient client, string merchantKey, string storeId)
    {
        var response = await client.SendAsync(PayFactory.Authed(HttpMethod.Post, "/v1/pay/pos/pairings", merchantKey, new { storeId, deviceLabel = "Till 1" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        return (body.GetProperty("code").GetString()!, body.GetProperty("prefix").GetString()!);
    }

    [Fact]
    public async Task A_code_survives_an_api_restart_and_the_key_gets_the_promised_prefix()
    {
        var dbPath = NewDbPath();
        try
        {
            string merchantKey, storeId, code, prefix;
            using (var first = OnDb(dbPath))
            {
                var client = first.CreateClient();
                (merchantKey, storeId) = await first.RegisterMerchant(client);
                (code, prefix) = await CreatePairing(client, merchantKey, storeId);
            }

            // A fresh host on the same database — as after a restart, or another instance.
            using var second = OnDb(dbPath);
            var claim = await second.CreateClient().PostAsJsonAsync("/v1/pay/pos/claim", new { code });
            Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
            var body = await Json(claim);

            var apiKey = body.GetProperty("apiKey").GetString()!;
            Assert.Equal(prefix, body.GetProperty("prefix").GetString());
            Assert.StartsWith(prefix + "_", apiKey);
            Assert.Equal(storeId, body.GetProperty("storeId").GetString());
            Assert.Equal("Test Shop", body.GetProperty("merchantName").GetString());
            Assert.Equal("POS — Till 1", body.GetProperty("deviceLabel").GetString());
            Assert.False(string.IsNullOrEmpty(body.GetProperty("merchantId").GetString()));
            Assert.False(string.IsNullOrEmpty(body.GetProperty("storeName").GetString()));

            // The claimed key works.
            var me = await second.CreateClient().SendAsync(PayFactory.Authed(HttpMethod.Get, "/v1/pay/pos/shift-report?storeId=" + storeId, apiKey));
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task A_sqlite_dev_db_made_before_the_table_existed_gets_it_on_startup()
    {
        var dbPath = NewDbPath();
        try
        {
            string merchantKey, storeId;
            using (var old = OnDb(dbPath))
            {
                var client = old.CreateClient();
                (merchantKey, storeId) = await old.RegisterMerchant(client);
                // Simulate a dev DB from before AddPosPairings (EnsureCreated won't add tables later).
                await old.WithDb(db => db.Database.ExecuteSqlRawAsync("DROP TABLE \"PosPairings\""));
            }

            using var upgraded = OnDb(dbPath);
            var upgradedClient = upgraded.CreateClient();
            var (code, prefix) = await CreatePairing(upgradedClient, merchantKey, storeId);
            var claim = await upgradedClient.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
            Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
            Assert.Equal(prefix, (await Json(claim)).GetProperty("prefix").GetString());
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task A_code_can_only_be_claimed_once()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (merchantKey, storeId) = await factory.RegisterMerchant(client);
        var (code, _) = await CreatePairing(client, merchantKey, storeId);

        var first = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal("expired", (await Json(second)).GetProperty("reason").GetString());

        await factory.WithDb(async db => Assert.Equal(0, await db.PosPairings.CountAsync()));
    }

    [Fact]
    public async Task An_expired_code_is_refused_and_swept()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (merchantKey, storeId) = await factory.RegisterMerchant(client);
        var (code, _) = await CreatePairing(client, merchantKey, storeId);

        await factory.WithDb(async db =>
        {
            var row = await db.PosPairings.SingleAsync();
            // Made with the 10-minute TTL.
            Assert.InRange(row.ExpiresAt - row.CreatedAt, TimeSpan.FromMinutes(9.9), TimeSpan.FromMinutes(10.1));
            row.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        });

        var claim = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
        Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode);
        var body = await Json(claim);
        Assert.Equal("expired", body.GetProperty("reason").GetString());
        Assert.Contains("expired or already used", body.GetProperty("error").GetString());

        await factory.WithDb(async db =>
        {
            Assert.Equal(0, await db.PosPairings.CountAsync());
            Assert.False(await db.ApiKeys.AnyAsync(k => k.Kind == ApiKeyKind.Pos));
        });
    }

    [Fact]
    public async Task A_malformed_code_is_reported_as_malformed()
    {
        using var factory = new PayFactory();
        var claim = await factory.CreateClient().PostAsJsonAsync("/v1/pay/pos/claim", new { code = "ABC" });
        Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode);
        Assert.Equal("malformed", (await Json(claim)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_stored_row_holds_neither_the_code_nor_a_key_secret()
    {
        using var factory = new PayFactory();
        var client = factory.CreateClient();
        var (merchantKey, storeId) = await factory.RegisterMerchant(client);
        var (code, prefix) = await CreatePairing(client, merchantKey, storeId);

        PayPosPairing row = null!;
        await factory.WithDb(async db => row = await db.PosPairings.AsNoTracking().SingleAsync());
        var stored = typeof(PayPosPairing).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string)p.GetValue(row)!)
            .ToList();

        Assert.DoesNotContain(stored, v => v.Contains(code, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(MerchantAuthenticator.HashSecret(code), row.CodeHash);
        Assert.Equal(prefix, row.KeyPrefix);

        var claim = await client.PostAsJsonAsync("/v1/pay/pos/claim", new { code });
        var apiKey = (await Json(claim)).GetProperty("apiKey").GetString()!;
        var secret = apiKey[(prefix.Length + 1)..];

        // The secret didn't exist until the claim, so the pairing row never saw it.
        Assert.DoesNotContain(stored, v => v.Contains(secret, StringComparison.Ordinal));
        await factory.WithDb(async db =>
        {
            var key = await db.ApiKeys.SingleAsync(k => k.Prefix == prefix);
            Assert.Equal(MerchantAuthenticator.HashSecret(secret), key.Hash);
        });
    }

    [Fact]
    public async Task Two_simultaneous_claims_on_two_instances_yield_exactly_one_key()
    {
        var dbPath = NewDbPath();
        try
        {
            using var a = OnDb(dbPath);
            using var b = OnDb(dbPath);
            var clientA = a.CreateClient();
            var clientB = b.CreateClient();
            var (merchantKey, storeId) = await a.RegisterMerchant(clientA);

            for (var round = 0; round < 5; round++)
            {
                var (code, prefix) = await CreatePairing(clientA, merchantKey, storeId);

                var results = await Task.WhenAll(
                    clientA.PostAsJsonAsync("/v1/pay/pos/claim", new { code }),
                    clientB.PostAsJsonAsync("/v1/pay/pos/claim", new { code }));

                Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
                Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NotFound));
                await a.WithDb(async db => Assert.Equal(1, await db.ApiKeys.CountAsync(k => k.Prefix == prefix)));
            }
        }
        finally
        {
            DeleteDb(dbPath);
        }
    }
}
