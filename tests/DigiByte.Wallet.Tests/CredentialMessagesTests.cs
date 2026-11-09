using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>What the user is told when a credential check does not succeed: right word for the credential, and an
/// environmental problem is never described as a wrong credential.</summary>
public class CredentialMessagesTests
{
    [Theory]
    [InlineData(CredentialKind.Pin, "PIN")]
    [InlineData(CredentialKind.Passphrase, "passphrase")]
    public void The_word_matches_the_credential(CredentialKind kind, string word)
    {
        Assert.Equal(word, CredentialMessages.Word(kind));
    }

    [Theory]
    [InlineData(CredentialKind.Pin, "Incorrect PIN.")]
    [InlineData(CredentialKind.Passphrase, "Incorrect passphrase.")]
    public void A_wrong_credential_names_the_right_thing(CredentialKind kind, string expected)
    {
        Assert.Equal(expected, CredentialMessages.Incorrect(kind));
        Assert.Equal(expected, CredentialMessages.ForFailure(kind, CryptoFailure.WrongCredential));
    }

    [Theory]
    [InlineData(CredentialKind.Pin)]
    [InlineData(CredentialKind.Passphrase)]
    public void A_newer_format_asks_for_an_update_and_does_not_blame_the_credential(CredentialKind kind)
    {
        var message = CredentialMessages.ForFailure(kind, CryptoFailure.UnsupportedVersion);

        Assert.Contains("newer version", message);
        Assert.Contains("Update", message);
        Assert.DoesNotContain("Incorrect", message);
    }

    [Theory]
    [InlineData(CredentialKind.Pin, "PIN")]
    [InlineData(CredentialKind.Passphrase, "passphrase")]
    public void Running_out_of_memory_says_so_and_that_it_was_not_counted(CredentialKind kind, string word)
    {
        var message = CredentialMessages.ForFailure(kind, CryptoFailure.ResourceExhausted);

        Assert.Contains("memory", message);
        Assert.Contains(word, message);
        Assert.Contains("did not count", message);
        Assert.DoesNotContain("Incorrect", message);
    }

    [Fact]
    public void Unreadable_data_says_nothing_was_changed_and_does_not_blame_the_credential()
    {
        var message = CredentialMessages.ForFailure(CredentialKind.Pin, CryptoFailure.Unreadable);

        Assert.Contains("could not be read", message);
        Assert.Contains("Nothing was changed", message);
        Assert.DoesNotContain("Incorrect", message);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(CryptoFailure.WrongCredential, true)]
    [InlineData(CryptoFailure.Unreadable, false)]
    [InlineData(CryptoFailure.UnsupportedVersion, false)]
    [InlineData(CryptoFailure.ResourceExhausted, false)]
    public void Only_a_wrong_credential_counts_as_an_attempt(CryptoFailure? failure, bool counts)
    {
        Assert.Equal(counts, CredentialMessages.CountsAsAttempt(failure));
    }

    [Theory]
    [InlineData(CredentialKind.Pin, "Biometric failed. Enter your PIN.")]
    [InlineData(CredentialKind.Passphrase, "Biometric failed. Enter your passphrase.")]
    public void A_failed_biometric_asks_for_the_right_credential(CredentialKind kind, string expected)
    {
        Assert.Equal(expected, CredentialMessages.BiometricFailed(kind));
    }
}
