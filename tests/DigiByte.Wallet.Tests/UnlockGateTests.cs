using System.Text.RegularExpressions;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// A page that needs the wallet shows only over an open wallet session. <see cref="AppState.IsWalletUnlocked"/> is UI
/// state and can say "unlocked" while the session (and its keys) is gone: after a reload, or after a lock. The Request
/// page rendered in that gap and crashed into the error page on the first address it handed out.
/// </summary>
public class UnlockGateTests
{
    private static AppState NewAppState(WalletService wallet) =>
        new(new NullJs(), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DefaultNetwork"] = "mainnet" })
            .Build(), () => wallet);

    private static GateRoute Gate(WalletService wallet, bool anyWallet = true) => UnlockGate.Decide(anyWallet, wallet.IsUnlocked);

    [Theory]
    [InlineData(false, false, GateRoute.Welcome)]
    [InlineData(false, true, GateRoute.Welcome)]
    [InlineData(true, false, GateRoute.Unlock)]
    [InlineData(true, true, GateRoute.Show)]
    public void Decide_FollowsWalletsAndSession(bool anyWallet, bool sessionUnlocked, GateRoute expected)
    {
        Assert.Equal(expected, UnlockGate.Decide(anyWallet, sessionUnlocked));
    }

    [Fact]
    public void RedirectFor_SendsALockedWalletToUnlock()
    {
        Assert.Equal("/unlock", UnlockGate.RedirectFor(GateRoute.Unlock));
        Assert.Equal("/welcome", UnlockGate.RedirectFor(GateRoute.Welcome));
        Assert.Null(UnlockGate.RedirectFor(GateRoute.Show));
    }

    [Fact]
    public async Task AfterLock_WithTheAppFlagStillSayingUnlocked_TheGateSendsToUnlock()
    {
        var env = new ChangeAddressTests.Env();
        var wallet = await env.CreateFundedAsync();
        var app = NewAppState(wallet);
        app.IsWalletUnlocked = true;
        Assert.Equal(GateRoute.Show, Gate(wallet));

        app.LockNow();
        app.IsWalletUnlocked = true; // the mismatch: UI state left behind, session gone

        Assert.False(wallet.IsUnlocked);
        Assert.Equal(GateRoute.Unlock, Gate(wallet));
    }

    [Fact]
    public async Task AfterReload_StoredWalletsButNoSession_TheGateSendsToUnlock()
    {
        var env = new ChangeAddressTests.Env();
        await env.CreateFundedAsync();
        var reloaded = env.NewService(); // a page reload: wallets on the device, nothing decrypted
        var app = NewAppState(reloaded);
        app.IsWalletUnlocked = true;

        Assert.NotEmpty(await reloaded.GetAllWalletsAsync());
        Assert.False(reloaded.IsUnlocked);
        Assert.Equal(GateRoute.Unlock, Gate(reloaded));
    }

    [Fact]
    public async Task CreatingARequestWhileLocked_ThrowsTheLockedErrorThePageCatches()
    {
        // Payments.CreateFromKeypad turns exactly this into a trip to the unlock screen instead of the error page.
        var env = new ChangeAddressTests.Env();
        var wallet = await env.CreateFundedAsync();
        var walletId = wallet.ActiveWallet!.Id;
        var requests = new PaymentRequestService(env.Storage, wallet);
        var app = NewAppState(wallet);
        app.IsWalletUnlocked = true;
        app.LockNow();
        Assert.False(wallet.IsUnlocked);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => requests.CreateAsync(walletId, new NewRequest(5m, null, null)));
        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── The pages ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string WebDir = Path.Combine(FindRepoRoot(), "src", "DigiByte.Web");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "src.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository root (src/src.sln).");
    }

    [Fact]
    public void AuthGuard_ShowsThePageFromTheWalletSession_NotTheAppFlag()
    {
        var guard = File.ReadAllText(Path.Combine(WebDir, "Components", "Shared", "AuthGuard.razor"));
        Assert.Contains("else if (WalletService.IsUnlocked)", guard);
        Assert.DoesNotContain("else if (AppState.IsWalletUnlocked)", guard);
        Assert.Contains("AppState.OnChange += OnAppStateChanged", guard);
    }

    [Theory]
    [InlineData("Payments.razor")]
    [InlineData("Receive.razor")]
    public void PagesThatHandOutAddresses_AreBehindAuthGuard(string page)
    {
        var text = File.ReadAllText(Path.Combine(WebDir, "Pages", page));
        Assert.Matches(new Regex(@"^<AuthGuard>\s*$", RegexOptions.Multiline), text);
        Assert.Matches(new Regex(@"^</AuthGuard>\s*$", RegexOptions.Multiline), text);
    }

    [Fact]
    public void EveryPageThatLeansOnAuthGuard_IsWrappedInIt()
    {
        // A page whose code says <AuthGuard> handles the locked case must actually have one (Payments did not).
        var leaning = Directory.GetFiles(Path.Combine(WebDir, "Pages"), "*.razor")
            .Where(f => File.ReadAllText(f).Contains("if (!WalletService.IsUnlocked) return;"))
            .ToList();
        Assert.NotEmpty(leaning);
        var unguarded = leaning.Where(f => !File.ReadAllText(f).Contains("<AuthGuard>")).Select(Path.GetFileName).ToList();
        Assert.Empty(unguarded);
    }

    private sealed class NullJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }
}
