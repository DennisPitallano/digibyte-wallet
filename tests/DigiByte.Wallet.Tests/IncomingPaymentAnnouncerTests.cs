using DigiByte.Wallet.Models;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The "+N DGB received" notice is said once per payment for the whole app, not once per page visit. Home and Receive
/// share one announcer, and each visit seeds it again from saved history that can be older than what was already
/// announced; that must not bring the notice back.
/// </summary>
public class IncomingPaymentAnnouncerTests
{
    private static TransactionRecord Tx(string id, int confirmations = 0) => new()
    {
        TxId = id,
        Direction = TransactionDirection.Received,
        AmountSatoshis = 1_000_000_000,
        FeeSatoshis = 0,
        Timestamp = DateTime.UtcNow,
        Confirmations = confirmations,
    };

    private static (IncomingPaymentAnnouncer Announcer, NotificationService Toasts, List<string> Shown) Make()
    {
        var toasts = new NotificationService();
        var shown = new List<string>();
        toasts.OnChange += () =>
        {
            foreach (var t in toasts.Active.ToList())
            {
                if (!shown.Contains(t.Id)) shown.Add(t.Id);
                toasts.Dismiss(t.Id); // off screen at once, so a repeat would not be dropped as a duplicate
            }
        };
        return (new IncomingPaymentAnnouncer(toasts, new NullJs()), toasts, shown);
    }

    [Fact]
    public async Task APayment_IsAnnouncedOnce()
    {
        var (announcer, _, shown) = Make();
        await announcer.AnnounceAsync("w", [Tx("old", 50)], hideAmounts: false);
        await announcer.AnnounceAsync("w", [Tx("new"), Tx("old", 50)], hideAmounts: false);
        Assert.Single(shown);
    }

    [Fact]
    public async Task ComingBackToAPage_WithOlderSavedHistory_DoesNotAnnounceTheSamePaymentAgain()
    {
        var (announcer, _, shown) = Make();
        await announcer.AnnounceAsync("w", [Tx("old", 50)], hideAmounts: false);
        await announcer.AnnounceAsync("w", [Tx("new"), Tx("old", 50)], hideAmounts: false);

        // Home is left and opened again: it seeds from saved history that does not have the new payment yet.
        announcer.Seed("w", [Tx("old", 50)]);
        await announcer.AnnounceAsync("w", [Tx("new", 1), Tx("old", 50)], hideAmounts: false);

        Assert.Single(shown);
    }

    [Fact]
    public async Task AnotherWallet_StartsAgain_WithoutAnnouncingItsHistory()
    {
        var (announcer, _, shown) = Make();
        await announcer.AnnounceAsync("w", [Tx("old", 50)], hideAmounts: false);
        await announcer.AnnounceAsync("other", [Tx("x"), Tx("y", 1)], hideAmounts: false);
        Assert.Empty(shown);
    }

    private sealed class NullJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }
}
