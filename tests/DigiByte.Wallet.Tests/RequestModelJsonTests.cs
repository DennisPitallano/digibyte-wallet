using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The stored shape of a payment request (spec 036, data-model "PaymentRequest"). Requests saved before this
/// feature must still read, as untracked, and every new field must survive a save.
/// </summary>
public class RequestModelJsonTests
{
    // What the wallet wrote before spec 036: default serializer options, status as a number (Pending = 0).
    private const string LegacyJson = """
        [{"Id":"abc","Address":"dgb1qlegacy","AmountDgb":1.5,"Label":"Lunch","Message":"Thanks",
          "CreatedAt":"2026-10-01T12:00:00Z","ExpiresAt":null,"Status":0}]
        """;

    [Fact]
    public void LegacyRequest_ReadsAsUntracked_WithEmptyNewFields()
    {
        var list = JsonSerializer.Deserialize<List<PaymentRequest>>(LegacyJson, PaymentRequestService.JsonOptions)!;

        var r = Assert.Single(list);
        Assert.Equal("dgb1qlegacy", r.Address);
        Assert.Equal(1.5m, r.AmountDgb);
        Assert.False(r.Tracked);
        Assert.Null(r.AddressIndex);
        Assert.Empty(r.Payments);
        Assert.False(r.OpenedSincePaid);
        Assert.Null(r.PaidAnnouncedAt);
        Assert.Null(r.LockNotifiedAt);
    }

    [Fact]
    public void LegacyStatusNumberZero_ReadsAsWaiting()
    {
        // The raw value only: a legacy record is untracked, so the calculator shows it as NotTracked.
        var r = JsonSerializer.Deserialize<List<PaymentRequest>>(LegacyJson, PaymentRequestService.JsonOptions)![0];

        Assert.Equal(PaymentRequestStatus.Waiting, r.Status);
    }

    [Fact]
    public void RequestWithEveryNewField_RoundTrips()
    {
        var created = new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc);
        var original = new PaymentRequest
        {
            Id = "r1",
            Address = "dgb1qrequest",
            AddressIndex = 41,
            Tracked = true,
            AmountDgb = 123.45678901m,
            FiatAmount = 12.50m,
            FiatCurrency = "USD",
            FiatPrice = 0.10125m,
            Label = "Coffee",
            Message = "Table 4",
            CreatedAt = created,
            ExpiresAt = created.AddMinutes(15),
            CancelledAt = created.AddMinutes(20),
            RenewedFromId = "r0",
            Payments =
            [
                new RequestPayment { TxId = "tx1", AmountSatoshis = 5_000_000, Confirmed = true, FirstSeenAt = created.AddMinutes(1), Late = false },
                new RequestPayment { TxId = "tx2", AmountSatoshis = 7, Confirmed = false, FirstSeenAt = created.AddMinutes(30), Late = true },
            ],
            Status = PaymentRequestStatus.PartlyPaidExpired,
            PaidAnnouncedAt = created.AddMinutes(2),
            LockNotifiedAt = created.AddMinutes(3),
            OpenedSincePaid = true,
            LastCheckedAt = created.AddMinutes(4),
        };

        var json = JsonSerializer.Serialize(original, PaymentRequestService.JsonOptions);
        var back = JsonSerializer.Deserialize<PaymentRequest>(json, PaymentRequestService.JsonOptions)!;

        Assert.Equivalent(original, back, strict: true);
    }

    [Theory]
    [InlineData(PaymentRequestStatus.Waiting, "Waiting")]
    [InlineData(PaymentRequestStatus.PaymentArriving, "PaymentArriving")]
    [InlineData(PaymentRequestStatus.PartlyPaid, "PartlyPaid")]
    [InlineData(PaymentRequestStatus.Paid, "Paid")]
    [InlineData(PaymentRequestStatus.Overpaid, "Overpaid")]
    [InlineData(PaymentRequestStatus.Expired, "Expired")]
    [InlineData(PaymentRequestStatus.PartlyPaidExpired, "PartlyPaidExpired")]
    [InlineData(PaymentRequestStatus.Cancelled, "Cancelled")]
    [InlineData(PaymentRequestStatus.NotTracked, "NotTracked")]
    public void Status_SerializesByName(PaymentRequestStatus status, string name)
    {
        var json = JsonSerializer.Serialize(status, PaymentRequestService.JsonOptions);

        Assert.Equal($"\"{name}\"", json);
        Assert.Equal(status, JsonSerializer.Deserialize<PaymentRequestStatus>(json, PaymentRequestService.JsonOptions));
    }
}
