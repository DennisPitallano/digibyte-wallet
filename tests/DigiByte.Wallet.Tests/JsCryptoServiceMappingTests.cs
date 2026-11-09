using System.Text.Json;
using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// How the browser's crypto glue reports failures reaches the rest of the app (T056, FR-009, FR-017): a value from a newer
/// version is "update required", never a wrong-credential error, and nothing that is not a wrong credential counts as an attempt.
/// </summary>
public class JsCryptoServiceMappingTests
{
    private sealed class ScriptedInterop(object? reply) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var json = JsonSerializer.Serialize(reply);
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private static JsCryptoService Service(string reason) => new(new ScriptedInterop(new { ok = false, reason }));

    [Theory]
    [InlineData("WrongCredential", CryptoFailure.WrongCredential)]
    [InlineData("UnsupportedVersion", CryptoFailure.UnsupportedVersion)]
    [InlineData("ResourceExhausted", CryptoFailure.ResourceExhausted)]
    [InlineData("Unsupported", CryptoFailure.Unsupported)]
    [InlineData("Unreadable", CryptoFailure.Unreadable)]
    [InlineData("something-new", CryptoFailure.Unreadable)]
    public async Task Each_reason_from_the_browser_maps_to_its_own_failure(string reason, CryptoFailure expected)
    {
        var result = await Service(reason).DecryptAsync("dgb3$whatever", "123456");

        Assert.False(result.Success);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task A_value_from_a_newer_version_is_update_required_and_never_a_wrong_credential()
    {
        var result = await Service("UnsupportedVersion").DecryptAsync("dgb3$whatever", "123456");

        Assert.Equal(CryptoFailure.UnsupportedVersion, result.Failure);
        Assert.False(result.CountsAsWrongCredential);
        Assert.False(CredentialMessages.CountsAsAttempt(result.Failure));
        Assert.NotEqual(CredentialMessages.Incorrect(CredentialKind.Pin), CredentialMessages.ForFailure(CredentialKind.Pin, result.Failure!.Value));
    }

    [Fact]
    public async Task Encrypting_on_a_browser_that_cannot_run_the_protection_is_reported_as_unsupported()
    {
        var service = new JsCryptoService(new ScriptedInterop(new { ok = false, reason = "Unsupported" }));

        var result = await service.EncryptAsync([1, 2, 3], "123456", CredentialKind.Pin);

        Assert.Equal(CryptoFailure.Unsupported, result.Failure);
    }
}
