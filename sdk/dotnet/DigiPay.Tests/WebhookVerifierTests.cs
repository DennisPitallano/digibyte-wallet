using System.Security.Cryptography;
using System.Text;
using DigiPay;

namespace DigiPay.Tests;

/// <summary>
/// Signature-verification smoke tests. Same cases as the Node + Python
/// SDKs so the three stay behaviour-equivalent.
/// </summary>
public class WebhookVerifierTests
{
    private const string Secret = "test-secret-123";
    // Matches the Node test payload exactly — kept simple so a human can
    // eyeball round-trip parity across the three SDKs.
    private const string Payload = """{"event":"session.paid","timestamp":"2026-04-22T00:00:00Z","session":{"id":"ses_abc","storeId":"sto_x","merchantId":"mer_x","status":"paid","address":"dgb1q","amountSatoshis":500000000,"amount":5.0,"receivedSatoshis":500000000,"confirmations":1,"createdAt":"2026-04-22T00:00:00Z","expiresAt":"2026-04-22T00:30:00Z","uri":"digibyte:dgb1q","checkoutUrl":"https://pay.dgbwallet.app/pay/ses_abc"}}""";

    private static string Sign(string body, string secret = Secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return "sha256=" + hex;
    }

    [Fact]
    public void Verify_AcceptsCorrectlySignedPayload()
    {
        var evt = WebhookVerifier.Verify(Payload, Sign(Payload), Secret);
        Assert.Equal("session.paid", evt.Event);
        Assert.Equal("ses_abc", evt.Session.Id);
        Assert.Equal(5.0m, evt.Session.Amount);
    }

    [Fact]
    public void Verify_AcceptsBytesBody()
    {
        var evt = WebhookVerifier.Verify(Encoding.UTF8.GetBytes(Payload), Sign(Payload), Secret);
        Assert.Equal("session.paid", evt.Event);
    }

    [Fact]
    public void Verify_ToleratesMissingSha256Prefix()
    {
        var sig = Sign(Payload).Replace("sha256=", "");
        var evt = WebhookVerifier.Verify(Payload, sig, Secret);
        Assert.Equal("session.paid", evt.Event);
    }

    [Fact]
    public void Verify_RejectsWrongSignature()
    {
        var ex = Assert.Throws<DigiPayError>(
            () => WebhookVerifier.Verify(Payload, Sign(Payload, "not-the-real-secret"), Secret));
        Assert.Equal(401, ex.Status);
    }

    [Fact]
    public void Verify_RejectsMissingHeader()
    {
        var ex = Assert.Throws<DigiPayError>(
            () => WebhookVerifier.Verify(Payload, signature: null, Secret));
        Assert.Equal(401, ex.Status);
    }

    [Fact]
    public void Verify_RejectsEmptySecret()
    {
        var ex = Assert.Throws<DigiPayError>(
            () => WebhookVerifier.Verify(Payload, Sign(Payload), secret: ""));
        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public void Verify_RejectsTamperedBody()
    {
        // Sign the original, then tamper. Signature is valid for the
        // original but the tampered body won't round-trip.
        var validSigForOriginal = Sign(Payload);
        var tampered = Payload.Replace("\"amount\":5.0", "\"amount\":5000.0");
        var ex = Assert.Throws<DigiPayError>(
            () => WebhookVerifier.Verify(tampered, validSigForOriginal, Secret));
        Assert.Equal(401, ex.Status);
    }

    [Fact]
    public void Verify_RejectsJunkJsonAfterValidSignature()
    {
        var bad = "{not json";
        var ex = Assert.Throws<DigiPayError>(
            () => WebhookVerifier.Verify(bad, Sign(bad), Secret));
        Assert.Equal(400, ex.Status);
    }
}
