using System.Text.Json;
using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Moves requests from the old device-wide list (<c>payment_requests</c>) to one wallet's own list
/// (spec 036, data-model "Migration"). Pure: the caller reads and writes storage.
/// </summary>
public static class RequestMigration
{
    /// <summary>The outcome of one split.</summary>
    /// <param name="Moved">Requests that belong to this wallet, marked untracked.</param>
    /// <param name="RemainingJson">What the legacy key should now hold; null means remove the key.</param>
    /// <param name="Changed">False when nothing should be written.</param>
    public sealed record Result(IReadOnlyList<PaymentRequest> Moved, string? RemainingJson, bool Changed);

    /// <summary>
    /// Splits the legacy list into the requests whose address is one of <paramref name="walletAddresses"/> and the
    /// rest. Moved requests are untracked: they shared an address, so their payments can't be told apart (FR-006).
    /// A list that can't be read is left exactly as it is.
    /// </summary>
    public static Result Split(string? legacyJson, IReadOnlySet<string> walletAddresses)
    {
        if (string.IsNullOrWhiteSpace(legacyJson)) return new Result([], legacyJson, false);

        List<PaymentRequest>? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<List<PaymentRequest>>(legacyJson, PaymentRequestService.JsonOptions);
        }
        catch (JsonException)
        {
            return new Result([], legacyJson, false);
        }
        if (legacy is null) return new Result([], legacyJson, false);

        var moved = legacy.Where(r => r.Address is not null && walletAddresses.Contains(r.Address)).ToList();
        if (moved.Count == 0) return new Result([], legacyJson, false);

        foreach (var r in moved)
        {
            r.Tracked = false;
            r.Status = PaymentRequestStatus.NotTracked;
        }
        var rest = legacy.Except(moved).ToList();
        var remaining = rest.Count == 0 ? null : JsonSerializer.Serialize(rest, PaymentRequestService.JsonOptions);
        return new Result(moved, remaining, true);
    }
}
