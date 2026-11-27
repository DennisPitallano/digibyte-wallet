using DigiByte.Pay.Api.Data;
using DigiByte.Pay.Api.Endpoints;
using DigiByte.Pay.Api.Services.Payouts;
using Microsoft.EntityFrameworkCore;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// The currencies a buyer-choice payment can be paid in (specs/033 US3, research R1–R5). A buyer-choice session keeps
/// one <see cref="PaySessionOption"/> per currency; its own Currency/Address/amount fields always describe the currency
/// in effect, and <see cref="LockTo"/> switches them to the option the buyer's funds arrived on.
/// </summary>
public sealed class SessionOptionService(
    DigiPayDbContext db,
    DigiDollarNodeClient ddNode,
    DgbPriceService prices,
    DdSurfacesGate gate,
    DdAvailability availability,
    ILogger<SessionOptionService> log)
{
    public const long MinCents = 100;
    public const long MaxCents = 10_000_000;

    /// <summary>A node call made while a payment is being created never waits longer than this (SC-002).</summary>
    public static readonly TimeSpan CreationNodeTimeout = TimeSpan.FromSeconds(3);

    public enum Failure { None, NotFound, NoChoice, Locked, NotPending, Expired, DdDisabled, NoRate, OutOfRange, NodeUnavailable }

    public sealed record Result(PaySessionOption? Option, Failure Failure)
    {
        public static Result Of(Failure f) => new(null, f);
    }

    /// <summary>
    /// Research R5: buyer choice is honoured only when the request asks for it, the store has DigiDollar and buyer choice
    /// on, the DD surfaces switch is on, and the payment is a DGB payment priced in a regular currency.
    /// </summary>
    public bool Allowed(CreateSessionRequest body, PayStore store, string currency) =>
        body.BuyerChoice == true
        && store.DigiDollarEnabled
        && store.BuyerChoiceEnabled
        && gate.Enabled
        && currency == "DGB"
        && !string.IsNullOrWhiteSpace(body.FiatCurrency)
        && body.FiatAmount is > 0;

    /// <summary>True when the store can offer DD right now as far as configuration goes (not the node's health).</summary>
    public bool DdOffered(PayStore store) => store.DigiDollarEnabled && gate.Enabled;

    /// <summary>The DGB option: the address and amount the session was created with.</summary>
    public static PaySessionOption DgbOption(PaySession s) => new()
    {
        SessionId = s.Id,
        Currency = "DGB",
        Address = s.Address,
        AmountSatoshis = s.AmountSatoshis,
    };

    /// <summary>
    /// DD cents for a fiat price: USD at 1:1, anything else at DigiPay's USD rate, rounded up to the cent (research R3).
    /// </summary>
    public async Task<(long Cents, decimal Rate, Failure Failure)> QuoteAsync(string? fiatCurrency, decimal? fiatAmount, CancellationToken ct = default)
    {
        if (fiatAmount is not > 0) return (0, 0, Failure.NoRate);
        var rate = await prices.GetUsdRateAsync(fiatCurrency, ct);
        if (rate is not > 0) return (0, 0, Failure.NoRate);
        var cents = (long)Math.Ceiling(fiatAmount.Value * 100m / rate.Value);
        if (cents is < MinCents or > MaxCents) return (cents, rate.Value, Failure.OutOfRange);
        return (cents, rate.Value, Failure.None);
    }

    /// <summary>
    /// For a store whose default currency is DD: makes the DD option while the session is being created, so the buyer
    /// starts on DD. Skips the node when it's reported down and gives it <see cref="CreationNodeTimeout"/> at most;
    /// any failure returns null and the session starts on DGB (research R5).
    /// </summary>
    public async Task<PaySessionOption?> TryCreateAtCreationAsync(PaySession session, PayStore store, CancellationToken ct = default)
    {
        if (!DdOffered(store)) return null;
        if (!await availability.IsAvailableAsync(ct)) return null;
        var (cents, rate, failure) = await QuoteAsync(session.FiatCurrency, session.FiatAmount, ct);
        if (failure != Failure.None) return null;
        string address;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CreationNodeTimeout);
            address = await ddNode.GenerateAddressAsync(cts.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogInformation(ex, "DD option not created for {SessionId}; it starts on DGB", session.Id);
            return null;
        }
        var option = NewDdOption(session, address, cents, rate);
        db.SessionOptions.Add(option);
        await DdAddressRegistry.RegisterAsync(db, address, session.MerchantId, store.Id, DdAddress.KindPayment, session.Id, store.Network, ct);
        return option;
    }

    /// <summary>
    /// The buyer's first switch to DD (POST /v1/pay/sessions/{id}/options/dd): returns the session's DD option, creating
    /// it when there is none. At most one per session (unique SessionId+Currency); the amount never changes afterwards.
    /// </summary>
    public async Task<Result> GetOrCreateDdOptionAsync(string sessionId, DateTime now, CancellationToken ct = default)
    {
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return Result.Of(Failure.NotFound);
        if (!session.BuyerChoice) return Result.Of(Failure.NoChoice);
        if (session.LockedAt is not null) return Result.Of(Failure.Locked);
        if (session.Status != PaySessionStatus.Pending) return Result.Of(Failure.NotPending);
        if (now > session.ExpiresAt) return Result.Of(Failure.Expired);

        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == session.StoreId, ct);
        if (store is null || !DdOffered(store)) return Result.Of(Failure.DdDisabled);

        var existing = await db.SessionOptions.AsNoTracking().FirstOrDefaultAsync(o => o.SessionId == session.Id && o.Currency == "DD", ct);
        if (existing is not null) return new(existing, Failure.None);

        var (cents, rate, failure) = await QuoteAsync(session.FiatCurrency, session.FiatAmount, ct);
        if (failure != Failure.None) return Result.Of(failure);

        string address;
        try { address = await ddNode.GenerateAddressAsync(ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogInformation(ex, "DD option for {SessionId}: node unavailable", session.Id);
            return Result.Of(Failure.NodeUnavailable);
        }

        var option = NewDdOption(session, address, cents, rate);
        db.SessionOptions.Add(option);
        await DdAddressRegistry.RegisterAsync(db, address, session.MerchantId, store.Id, DdAddress.KindPayment, session.Id, store.Network, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(option, Failure.None);
        }
        catch (DbUpdateException)
        {
            // A concurrent switch made the option first; the address made here is simply unused.
            db.ChangeTracker.Clear();
            var winner = await db.SessionOptions.AsNoTracking().FirstOrDefaultAsync(o => o.SessionId == session.Id && o.Currency == "DD", ct);
            return winner is not null ? new(winner, Failure.None) : throw new InvalidOperationException("DD option conflict without a winner");
        }
    }

    private static PaySessionOption NewDdOption(PaySession session, string address, long cents, decimal rate) => new()
    {
        SessionId = session.Id,
        Currency = "DD",
        Address = address,
        AmountSatoshis = 0,
        AmountCentsDd = cents,
        UsdRate = rate,
        RateAt = DateTime.UtcNow,
    };

    /// <summary>Points the session's live fields at <paramref name="option"/> without locking (a DD-default start).</summary>
    public static void StartOn(PaySession session, PaySessionOption option)
    {
        session.Currency = option.Currency;
        session.Address = option.Address;
        session.AmountSatoshis = option.AmountSatoshis;
        session.AmountCentsDd = option.AmountCentsDd;
    }

    /// <summary>Locks the session to the option the buyer's funds arrived on (FR-024).</summary>
    public static void LockTo(PaySession session, PaySessionOption option, DateTime now)
    {
        StartOn(session, option);
        session.LockedAt = now;
    }
}
