using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Where the Multisig shortcut on the wallet's home leads: back to the shared wallet used last, straight into the
/// only one, or to the list (spec 026, FR-009).
/// </summary>
public class MultisigUxShortcutTests
{
    [Fact]
    public void The_shared_wallet_used_last_is_opened_again()
    {
        Assert.Equal("b", SharedWalletShortcut.Target("b", ["a", "b", "c"]));
    }

    [Fact]
    public void The_last_used_one_wins_even_when_it_is_the_only_one()
    {
        Assert.Equal("a", SharedWalletShortcut.Target("a", ["a"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gone")]
    public void With_exactly_one_shared_wallet_it_opens_directly(string? lastUsed)
    {
        Assert.Equal("only", SharedWalletShortcut.Target(lastUsed, ["only"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_several_and_nothing_remembered_the_list_opens(string? lastUsed)
    {
        Assert.Null(SharedWalletShortcut.Target(lastUsed, ["a", "b"]));
    }

    [Fact]
    public void A_remembered_wallet_that_is_no_longer_there_leads_to_the_list()
    {
        // Removed, or it belongs to another personal wallet than the one that is active now.
        Assert.Null(SharedWalletShortcut.Target("gone", ["a", "b"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("remembered")]
    public void With_no_shared_wallets_the_list_opens(string? lastUsed)
    {
        Assert.Null(SharedWalletShortcut.Target(lastUsed, []));
    }

    [Fact]
    public void Ids_are_matched_exactly()
    {
        Assert.Null(SharedWalletShortcut.Target("ABC", ["abc", "other"]));
    }

    [Fact]
    public void The_storage_key_is_per_personal_wallet()
    {
        Assert.Equal("pref:multisigLastUsed:wallet-1", SharedWalletShortcut.StorageKey("wallet-1"));
        Assert.NotEqual(SharedWalletShortcut.StorageKey("wallet-1"), SharedWalletShortcut.StorageKey("wallet-2"));
    }
}
