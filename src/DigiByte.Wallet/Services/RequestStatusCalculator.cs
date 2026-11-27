using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>What one check of a request found (spec 036).</summary>
/// <param name="Status">The status to show.</param>
/// <param name="Payments">Every transaction paying the request's address, with its first-seen time and late flag.</param>
/// <param name="ArrivingSatoshis">Unconfirmed money shown as a "+… arriving" note next to another status; 0 otherwise.</param>
/// <param name="BecamePaid">Paid or overpaid, and not yet announced: the paid moment is due (FR-010).</param>
/// <param name="BecameArriving">The money seen first reached the amount asked on this check: "Payment arriving" is due.</param>
public sealed record RequestCheck(
    PaymentRequestStatus Status,
    IReadOnlyList<RequestPayment> Payments,
    long ArrivingSatoshis,
    bool BecamePaid,
    bool BecameArriving);

/// <summary>
/// Works out a request's status from the transactions paying its own address (spec 036, data-model "Status rules").
/// Everything is recomputed from the current list on each check, so a transaction that drops out simply stops counting
/// (FR-009) and nothing can drift. Pure.
/// </summary>
public static class RequestStatusCalculator
{
    private const decimal SatoshisPerCoin = 100_000_000m;

    public static RequestCheck Compute(PaymentRequest request, IEnumerable<TransactionInfo> txsToAddress, DateTime nowUtc)
    {
        var previous = request.Payments.ToDictionary(p => p.TxId);
        var payments = new List<RequestPayment>();
        foreach (var tx in txsToAddress)
        {
            var amount = tx.Outputs.Where(o => o.Address == request.Address).Sum(o => o.AmountSatoshis);
            if (amount <= 0) continue;
            var firstSeen = previous.TryGetValue(tx.TxId, out var known) ? known.FirstSeenAt : nowUtc;
            payments.Add(new RequestPayment
            {
                TxId = tx.TxId,
                AmountSatoshis = amount,
                Confirmed = tx.Confirmations >= 1,
                FirstSeenAt = firstSeen,
                Late = (request.ExpiresAt is { } exp && firstSeen > exp) || (request.CancelledAt is { } c && firstSeen > c),
            });
        }

        if (!request.Tracked)
            return new RequestCheck(PaymentRequestStatus.NotTracked, payments, 0, false, false);

        var confirmed = payments.Where(p => p.Confirmed).Sum(p => p.AmountSatoshis);
        var pending = payments.Where(p => !p.Confirmed).Sum(p => p.AmountSatoshis);
        long? asked = request.AmountDgb is { } dgb ? (long)Math.Round(dgb * SatoshisPerCoin, MidpointRounding.AwayFromZero) : null;
        var cancelled = request.CancelledAt is not null;
        var expired = request.ExpiresAt is { } expiresAt && nowUtc >= expiresAt;

        var status = StatusOf(confirmed, pending, asked, cancelled, expired);
        var arriving = status != PaymentRequestStatus.PaymentArriving && pending > 0 ? pending : 0;
        var paid = status is PaymentRequestStatus.Paid or PaymentRequestStatus.Overpaid;

        var before = previous.Values.Sum(p => p.AmountSatoshis);
        var now = confirmed + pending;
        var becameArriving = !paid && pending > 0 && (asked is { } a ? before < a && now >= a : before == 0);

        return new RequestCheck(status, payments, arriving, paid && request.PaidAnnouncedAt is null, becameArriving);
    }

    private static PaymentRequestStatus StatusOf(long confirmed, long pending, long? asked, bool cancelled, bool expired)
    {
        if (asked is null)
        {
            if (confirmed > 0) return PaymentRequestStatus.Paid;
            if (cancelled) return PaymentRequestStatus.Cancelled;
            return pending > 0 ? PaymentRequestStatus.PaymentArriving : PaymentRequestStatus.Waiting;
        }

        // Money that arrived wins over expiry and cancelling: a paid request never reads as expired (FR-020).
        if (confirmed > asked) return PaymentRequestStatus.Overpaid;
        if (confirmed == asked) return PaymentRequestStatus.Paid;
        if (confirmed > 0)
        {
            if (cancelled) return PaymentRequestStatus.Cancelled;
            return expired ? PaymentRequestStatus.PartlyPaidExpired : PaymentRequestStatus.PartlyPaid;
        }
        if (cancelled) return PaymentRequestStatus.Cancelled;
        if (expired) return PaymentRequestStatus.Expired;
        return pending > 0 ? PaymentRequestStatus.PaymentArriving : PaymentRequestStatus.Waiting;
    }
}
