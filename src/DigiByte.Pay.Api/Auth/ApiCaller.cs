using DigiByte.Pay.Api.Data;

namespace DigiByte.Pay.Api.Auth;

/// <summary>
/// Who is calling: the merchant plus, when the Bearer token was an API key, that key's kind,
/// store and label. Built once per request by <see cref="MerchantAuthenticator.GetCallerAsync"/>.
///
/// A POS tablet key bound to a store (<see cref="IsStoreScoped"/>) can only take payments for
/// that store. Two layers enforce it:
///   • <see cref="StoreScopedKeyGuard"/> answers 403 on every endpoint not marked
///     <see cref="PosKeyEndpointExtensions.AllowPosKey{TBuilder}"/> (refunds, key/store/webhook
///     management, audit, analytics, …), so a new endpoint is closed to tablets by default.
///   • The few endpoints a tablet needs call <see cref="CanUseStore"/> and return
///     <see cref="OutsideStore"/> when the store is another one.
/// Legacy POS keys with no store keep the old account-wide access.
/// </summary>
public sealed record ApiCaller(PayMerchant Merchant, string? KeyKind, string? KeyStoreId, string? KeyLabel)
{
    public bool IsPosKey => KeyKind == ApiKeyKind.Pos;

    public bool IsStoreScoped => IsPosKey && KeyStoreId is not null;

    /// <summary>True when this caller may work with <paramref name="storeId"/>.</summary>
    public bool CanUseStore(string? storeId) =>
        !IsStoreScoped || string.Equals(storeId, KeyStoreId, StringComparison.Ordinal);

    /// <summary>
    /// PaySession.Source for sessions this caller creates: "pos" or "pos:Device name" for a
    /// tablet key (pairing labels are "POS — Device name" or "POS device — date"), else null.
    /// </summary>
    public string? SessionSource
    {
        get
        {
            if (!IsPosKey) return null;
            const string sep = "POS — ";
            if (KeyLabel is not null && KeyLabel.StartsWith(sep, StringComparison.OrdinalIgnoreCase) && KeyLabel.Length > sep.Length)
            {
                var device = KeyLabel[sep.Length..].Trim();
                // Source column is 32 chars — leave room for the "pos:" prefix.
                if (device.Length > 27) device = device[..27];
                return $"pos:{device}";
            }
            return "pos";
        }
    }

    /// <summary>403 for a tablet key reaching for another store's data.</summary>
    public static IResult OutsideStore() =>
        Results.Json(new { error = "this POS tablet can only work with the store it was paired to" }, statusCode: StatusCodes.Status403Forbidden);

    public const string NotForPosKeys = "a POS tablet key can't do this — use the dashboard or a merchant API key";
}

/// <summary>Endpoint metadata: store-scoped POS tablet keys may call this endpoint.</summary>
public sealed class PosKeyAllowed
{
    public static readonly PosKeyAllowed Instance = new();
}

public static class PosKeyEndpointExtensions
{
    /// <summary>
    /// Opens an endpoint (or group) to store-scoped POS tablet keys. The handler must still
    /// check <see cref="ApiCaller.CanUseStore"/> for the store it touches.
    /// </summary>
    public static TBuilder AllowPosKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(PosKeyAllowed.Instance);
}

/// <summary>
/// Closes every endpoint not marked <see cref="PosKeyAllowed"/> to store-scoped POS tablet
/// keys (403 with a plain error). Only looks up the key when the Bearer token is a dgp_ key.
/// </summary>
public static class StoreScopedKeyGuard
{
    public static IApplicationBuilder UseStoreScopedKeyGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint();
            if (endpoint is not null
                && endpoint.Metadata.GetMetadata<PosKeyAllowed>() is null
                && context.Request.Headers.Authorization.ToString().StartsWith("Bearer dgp_", StringComparison.OrdinalIgnoreCase))
            {
                var db = context.RequestServices.GetRequiredService<DigiPayDbContext>();
                var caller = await MerchantAuthenticator.GetCallerAsync(context.Request, db);
                if (caller is { IsStoreScoped: true })
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = ApiCaller.NotForPosKeys });
                    return;
                }
            }
            await next(context);
        });
}
