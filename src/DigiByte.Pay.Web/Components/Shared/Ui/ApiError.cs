using System.Text.Json;

namespace DigiByte.Pay.Web.Components.Shared.Ui;

/// <summary>
/// Turns a failed Pay API response into one plain sentence for the merchant. The API's
/// <c>{ "error": "…" }</c> text is written for developers, so it is only used to pick the right
/// sentence; raw response bodies are never shown on the page.
/// </summary>
public static class ApiError
{
    public const string Network = "Something went wrong. Check your connection and try again.";

    /// <summary>Shown when the DigiDollar node can't be reached (<c>digidollar_node_unavailable</c>).</summary>
    public const string DdUnavailable = "DigiDollar is temporarily unavailable. Try again, or take this payment in DGB.";

    /// <summary>Reads the response's <c>error</c> field (if any) and returns the plain sentence for it.</summary>
    public static async Task<string> ReadAsync(HttpResponseMessage response)
    {
        string? error = null;
        string? reason = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("error", out var e)
                    && e.ValueKind == JsonValueKind.String)
                {
                    error = e.GetString();
                    if (doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                        reason = r.GetString();
                }
            }
        }
        catch (JsonException) { /* not JSON: fall back to the status code */ }
        return Plain((int)response.StatusCode, error, reason);
    }

    /// <summary>The plain sentence for an API status code, its <c>error</c> text and optional <c>reason</c>.</summary>
    /// <summary>Shown when DD can't be turned on yet for want of a payout address (specs/034 FR-031).</summary>
    public const string PayoutAddressRequired =
        "Add a DigiDollar payout address first. DigiPay holds the DD buyers pay until it's paid out to that address.";

    public static string Plain(int status, string? error, string? reason = null)
    {
        var e = error ?? "";
        bool Has(string part) => e.Contains(part, StringComparison.OrdinalIgnoreCase);

        if (e.Length > 0)
        {
            if (Has("webhookUrl")) return "That webhook address can't be used. It must be a public https:// address.";
            if (Has("cannot be refunded")) return "This payment can't be refunded in its current state. Refresh to see its latest status.";
            if (Has("cannot be voided")) return "This payment can't be cancelled any more. It may have just been paid. Refresh to see its latest status.";
            if (Has("txid")) return "Paste the full transaction ID (32 to 128 characters).";
            if (Has("cannot delete your only store")) return "You need at least one store, so your only store can't be deleted.";
            if (Has("defaultSessionExpiryMinutes")) return "Enter a number of minutes from 1 to 1440, or leave it blank for the default.";
            if (Has("no receive")) return "This store has nowhere to receive payments yet. Add a DigiByte address or xpub in Settings.";
            if (Has("no webhookUrl")) return "Save a webhook address first.";
            if (Has("test deliveries")) return "Test events can't be replayed. Send a new test event instead.";
            if (Has("original session")) return "That payment no longer exists, so its event can't be replayed.";
            if (Has("delivery not found")) return "That delivery no longer exists. Refresh the log.";
            if (Has("store not found")) return "That store no longer exists, or isn't one of yours.";
            if (Has("xpub") || Has("DigiByte address")) return "That isn't a DigiByte address or BIP84 xpub this store can use. Check it is for the store's network.";
            if (Has("network must be")) return "Choose mainnet, testnet or regtest.";
            if (Has("label must be")) return "Keep the label to 80 characters or fewer.";
            if (Has("name is required") || Has("name must be")) return "Enter a name of up to 120 characters.";
            if (Has("amount") && Has("> 0")) return "Enter an amount greater than zero.";
            if (Has("defaultCurrency")) return "Choose DGB or one of the listed currencies (such as USD, EUR or PHP), or clear the default.";
            if (Has("unsupported currency")) return "That currency isn't supported.";
            // DigiDollar (specs/033 contract C1).
            if (Has("digidollar_node_unavailable")) return DdUnavailable;
            if (Has("dd_option_unavailable"))
                return reason == "out_of_range"
                    ? "This amount is outside DigiDollar's limits ($1.00 to $100,000.00)."
                    : "DigiDollar isn't available for this payment right now.";
            if (Has("digidollar_not_enabled")) return "DigiDollar isn't turned on for this store. Turn it on in Settings.";
            // DD payouts (specs/034 contract C2).
            if (Has("payout_address_required")) return PayoutAddressRequired;
            if (Has("payout_address_network_mismatch")) return "This store's DigiDollar payout address is for its current network. Change the payout address first.";
            if (Has("dd_held")) return "This store still holds DigiDollar. Pay it out first (DigiDollar payouts), then delete the store.";
        }

        return status switch
        {
            400 => "Check what you entered and try again.",
            401 => "Your sign-in has expired. Sign in again to continue.",
            403 => "You don't have permission to do that.",
            404 => "That item no longer exists. Refresh and try again.",
            409 => "That can't be done right now because it changed. Refresh and try again.",
            429 => "Too many attempts. Wait a minute and try again.",
            _ => "DigiPay couldn't do that right now. Try again in a moment.",
        };
    }
}
