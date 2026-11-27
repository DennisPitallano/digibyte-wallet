using DigiByte.Crypto.KeyGeneration;
using DigiByte.Crypto.Networks;
using DigiByte.Pay.Api.Auth;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace DigiByte.Pay.Api.Endpoints;

/// <summary>
/// Local-testing sign-in: lets someone open the dashboard without a Digi-ID wallet.
/// Program.cs maps this group ONLY when the host environment is Development AND
/// <c>DigiPay:DevSignIn</c> is true (default false; appsettings.Development.json turns it on).
/// It is never part of the OpenAPI description.
///
///   GET  /v1/pay/dev/status  → { enabled: true }   (the web sign-in page asks before showing the button)
///   POST /v1/pay/dev/signin  → same body as a finished Digi-ID poll: { status, merchantId, displayName, token }
/// </summary>
public static class DevSignInEndpoints
{
    public const string ConfigKey = "DigiPay:DevSignIn";
    public const string MerchantId = "mer_devmerchant000001";
    public const string StoreId = "sto_devmerchant000001";
    public const string MerchantName = "Dev merchant";

    // The BIP39 test-vector mnemonic ("abandon" ×11 + "about") that every wallet test
    // suite uses. Its keys are public, so its testnet BIP84 account xpub (m/84'/20'/0')
    // gives the dev store valid per-session testnet addresses. Testnet only — anything
    // sent to these addresses can be taken by anyone.
    private const string TestMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    // A testnet DigiDollar payout address from the same public test mnemonic, so the dev store can turn DigiDollar on
    // without a Digi-ID re-sign-in (spec 034 FR-031) in local and end-to-end runs. Testnet only, keys are public.
    private static readonly Lazy<string> DevDdPayoutAddress = new(() =>
        DigiByte.Crypto.Addresses.DigiDollarAddressValidator.Encode(
            new Mnemonic(TestMnemonic).DeriveExtKey().Derive(new KeyPath("m/86'/1'/0'/0/0")).PrivateKey.PubKey.TaprootInternalKey.ToBytes(),
            "testnet"));

    private static readonly Lazy<string> DevXpub = new(() =>
        new HdKeyDerivation(new Mnemonic(TestMnemonic), "", DigiByteNetwork.Testnet)
            .GetAccountExtPubKey()
            .ToString(DigiByteNetwork.Testnet));

    public static bool IsEnabled(IHostEnvironment env, IConfiguration config) =>
        env.IsDevelopment() && config.GetValue<bool>(ConfigKey);

    public static RouteGroupBuilder MapDevSignInEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/status", () => Results.Ok(new { enabled = true }));

        group.MapPost("/signin", async (DigiPayDbContext db) =>
        {
            var merchant = await EnsureDevMerchantAsync(db);

            // Same per-browser session token a Digi-ID sign-in mints (AuthEndpoints /callback).
            var (prefix, secret, hash) = MerchantAuthenticator.GenerateSessionToken();
            db.MerchantSessions.Add(new MerchantSession
            {
                Id = $"sess_dev{Guid.NewGuid():N}"[..21],
                MerchantId = merchant.Id,
                TokenPrefix = prefix,
                TokenHash = hash,
                ExpiresAt = DateTime.UtcNow.Add(MerchantAuthenticator.SessionLifetime),
            });
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                status = "signed",
                merchantId = merchant.Id,
                displayName = merchant.DisplayName,
                token = $"{prefix}_{secret}",
            });
        });

        return group;
    }

    /// <summary>
    /// Creates (or reuses) the fixed dev merchant and makes sure its default store is a
    /// testnet store with a receive xpub, so sessions can be created straight away and
    /// moved along with the sandbox advance endpoint.
    /// </summary>
    public static async Task<PayMerchant> EnsureDevMerchantAsync(DigiPayDbContext db)
    {
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Id == MerchantId);
        if (merchant is null)
        {
            merchant = new PayMerchant
            {
                Id = MerchantId,
                DisplayName = MerchantName,
                // Legacy columns; no API key is minted (same as a Digi-ID merchant).
                ApiKeyPrefix = "legacy_unused_devmerchant",
                ApiKeyHash = "unused",
            };
            db.Merchants.Add(merchant);
        }

        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == StoreId);
        if (store is null)
        {
            db.Stores.Add(new PayStore
            {
                Id = StoreId,
                MerchantId = MerchantId,
                Name = "Dev store (testnet)",
                Network = "testnet",
                Xpub = DevXpub.Value,
                DigiDollarEnabled = true,
            });
        }
        else if (store.Network is not ("testnet" or "regtest")
            || (string.IsNullOrEmpty(store.Xpub) && string.IsNullOrEmpty(store.ReceiveAddress)))
        {
            // Someone switched the dev store to mainnet or cleared its receive: put it back.
            store.Network = "testnet";
            store.Xpub = DevXpub.Value;
            store.ReceiveAddress = null;
        }

        await db.SaveChangesAsync();
        return merchant;
    }
}
