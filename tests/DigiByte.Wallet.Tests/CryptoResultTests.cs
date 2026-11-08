using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

public class CryptoResultTests
{
    private sealed class ScriptedCrypto : LegacyCryptoBase
    {
        public CryptoResult<byte[]> Next { get; set; } = CryptoResult<byte[]>.Fail(CryptoFailure.WrongCredential);
        public override Task<byte[]> EncryptAsync(byte[] plaintext, string pin) => Task.FromResult(plaintext);
        public override Task<byte[]?> DecryptAsync(byte[] ciphertext, string pin) => Task.FromResult<byte[]?>(null);
        public override Task<CryptoResult<byte[]>> DecryptAsync(string stored, string credential) => Task.FromResult(Next);
    }

    [Theory]
    [InlineData(CryptoFailure.WrongCredential)]
    [InlineData(CryptoFailure.Unreadable)]
    [InlineData(CryptoFailure.UnsupportedVersion)]
    [InlineData(CryptoFailure.ResourceExhausted)]
    public async Task Each_failure_is_surfaced_distinctly(CryptoFailure failure)
    {
        var crypto = new ScriptedCrypto { Next = CryptoResult<byte[]>.Fail(failure) };

        var result = await crypto.DecryptAsync("dgb2$x", "123456");

        Assert.False(result.Success);
        Assert.Equal(failure, result.Failure);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Success_carries_the_value()
    {
        var crypto = new ScriptedCrypto { Next = CryptoResult<byte[]>.Ok([1, 2, 3]) };

        var result = await crypto.DecryptAsync("dgb2$x", "123456");

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Only_wrong_credential_counts_toward_the_throttle()
    {
        Assert.True(CryptoResult<byte[]>.Fail(CryptoFailure.WrongCredential).CountsAsWrongCredential);
        Assert.False(CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable).CountsAsWrongCredential);
        Assert.False(CryptoResult<byte[]>.Fail(CryptoFailure.UnsupportedVersion).CountsAsWrongCredential);
        Assert.False(CryptoResult<byte[]>.Fail(CryptoFailure.ResourceExhausted).CountsAsWrongCredential);
        Assert.False(CryptoResult<byte[]>.Ok([1]).CountsAsWrongCredential);
    }
}
