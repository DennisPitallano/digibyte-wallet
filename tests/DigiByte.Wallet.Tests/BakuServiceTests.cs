using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

public class BakuServiceTests
{
    private static (BakuService svc, FakeStorageJs js) Create(Dictionary<string, string>? seed = null)
    {
        var js = new FakeStorageJs(seed);
        return (new BakuService(js), js);
    }

    // ── Priority and arbitration (FR-016) ──

    [Fact]
    public void Current_IsNull_WhenNothingRequested()
    {
        var (svc, _) = Create();
        Assert.Null(svc.Current);
    }

    [Fact]
    public void Current_PrefersHigherSeverity()
    {
        var (svc, _) = Create();
        svc.Request("home.tip");          // Tip
        svc.Request("send.new-address");  // Warning
        svc.Request("backup.why");        // Info
        Assert.Equal("send.new-address", svc.Current!.Id);
    }

    [Fact]
    public void Current_CriticalBeatsWarning()
    {
        var (svc, _) = Create();
        svc.Request("send.new-address");
        svc.Request("seed.reveal");
        Assert.Equal("seed.reveal", svc.Current!.Id);
    }

    [Fact]
    public void Current_TieGoesToMostRecentRequest()
    {
        var (svc, _) = Create();
        svc.Request("home.tip");
        svc.Request("receive.tip");
        Assert.Equal("receive.tip", svc.Current!.Id);
        svc.Request("home.tip"); // re-request makes it the most recent
        Assert.Equal("home.tip", svc.Current!.Id);
    }

    [Fact]
    public void Clear_RemovesMessage_AndFallsBackToNext()
    {
        var (svc, _) = Create();
        svc.Request("home.tip");
        svc.Request("send.new-address");
        svc.Clear("send.new-address");
        Assert.Equal("home.tip", svc.Current!.Id);
    }

    [Fact]
    public void Request_UnknownId_IsIgnored()
    {
        var (svc, _) = Create();
        svc.Request("does.not.exist");
        Assert.Null(svc.Current);
    }

    // ── Mode filtering (FR-010, FR-011) ──

    [Theory]
    [InlineData(BakuMode.GuardianOnly)]
    [InlineData(BakuMode.Off)]
    public async Task NonFullModes_HideInfoAndTip(BakuMode mode)
    {
        var (svc, _) = Create();
        await svc.SetModeAsync(mode);
        svc.Request("home.tip");
        svc.Request("backup.why");
        Assert.Null(svc.Current);
    }

    [Theory]
    [InlineData(BakuMode.Full)]
    [InlineData(BakuMode.GuardianOnly)]
    [InlineData(BakuMode.Off)]
    public async Task AllModes_ShowWarningAndCritical(BakuMode mode)
    {
        var (svc, _) = Create();
        await svc.SetModeAsync(mode);
        svc.Request("send.new-address");
        Assert.Equal("send.new-address", svc.Current!.Id);
        svc.Request("seed.reveal");
        Assert.Equal("seed.reveal", svc.Current!.Id);
    }

    [Theory]
    [InlineData(BakuMode.Full, true)]
    [InlineData(BakuMode.GuardianOnly, true)]
    [InlineData(BakuMode.Off, false)]
    public async Task ShowsImage_FollowsMode(BakuMode mode, bool expected)
    {
        var (svc, _) = Create();
        await svc.SetModeAsync(mode);
        Assert.Equal(expected, svc.ShowsImage);
    }

    // ── Dismissal (FR-009) ──

    [Fact]
    public async Task DismissAsync_HidesTip_Permanently()
    {
        var (svc, _) = Create();
        svc.Request("home.tip");
        await svc.DismissAsync("home.tip");
        Assert.Null(svc.Current);
        svc.Request("home.tip"); // requested again after dismissal
        Assert.Null(svc.Current);
    }

    [Theory]
    [InlineData("send.new-address")] // Warning
    [InlineData("seed.reveal")]      // Critical
    public async Task DismissAsync_IsNoOp_ForWarningAndCritical(string id)
    {
        var (svc, js) = Create();
        svc.Request(id);
        await svc.DismissAsync(id);
        Assert.Equal(id, svc.Current!.Id);
        Assert.DoesNotContain(js.Store, kv => kv.Key == "dgb-baku-dismissed" && kv.Value.Contains(id));
    }

    [Fact]
    public async Task ResetTipsAsync_ClearsDismissals()
    {
        var (svc, js) = Create();
        svc.Request("home.tip");
        await svc.DismissAsync("home.tip");
        await svc.ResetTipsAsync();
        svc.Request("home.tip");
        Assert.Equal("home.tip", svc.Current!.Id);
        Assert.Equal("", js.Store.GetValueOrDefault("dgb-baku-dismissed", ""));
    }

    // ── Persistence (data stays on device, nothing sensitive) ──

    [Fact]
    public async Task SetModeAsync_PersistsMode()
    {
        var (svc, js) = Create();
        await svc.SetModeAsync(BakuMode.GuardianOnly);
        Assert.Equal("guardian", js.Store["dgb-baku-mode"]);
    }

    [Fact]
    public async Task InitializeAsync_RestoresModeAndDismissals()
    {
        var (svc, _) = Create(new()
        {
            ["dgb-baku-mode"] = "off",
            ["dgb-baku-dismissed"] = "home.tip,receive.tip",
        });
        await svc.InitializeAsync();
        Assert.Equal(BakuMode.Off, svc.Mode);
        Assert.True(svc.IsDismissed("home.tip"));
        Assert.True(svc.IsDismissed("receive.tip"));
        Assert.False(svc.IsDismissed("dd.tip"));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    public async Task InitializeAsync_FallsBackToFull_OnBadStoredMode(string stored)
    {
        var (svc, _) = Create(new() { ["dgb-baku-mode"] = stored, ["dgb-baku-dismissed"] = ",,  ," });
        await svc.InitializeAsync();
        Assert.Equal(BakuMode.Full, svc.Mode);
        Assert.False(svc.IsDismissed(""));
    }

    [Fact]
    public async Task InitializeAsync_WhenStorageThrows_UsesDefaults()
    {
        var js = new FakeStorageJs { ThrowOnInvoke = true };
        var svc = new BakuService(js);
        await svc.InitializeAsync();
        Assert.Equal(BakuMode.Full, svc.Mode);
    }

    [Fact]
    public async Task OnChanged_FiresOnRequestModeAndDismiss()
    {
        var (svc, _) = Create();
        var count = 0;
        svc.OnChanged += () => count++;
        svc.Request("home.tip");
        await svc.SetModeAsync(BakuMode.GuardianOnly);
        await svc.SetModeAsync(BakuMode.Full);
        await svc.DismissAsync("home.tip");
        Assert.True(count >= 4);
    }

    // ── Guardian confirmation (FR-007) ──

    [Fact]
    public async Task ConfirmAsync_ReturnsTrue_WhenUserContinues()
    {
        var (svc, _) = Create();
        var task = svc.ConfirmAsync("seed.reveal");
        Assert.Equal("seed.reveal", svc.PendingConfirmation!.Id);
        svc.ResolveConfirmation(true);
        Assert.True(await task);
        Assert.Null(svc.PendingConfirmation);
    }

    [Fact]
    public async Task ConfirmAsync_ReturnsFalse_WhenUserGoesBack()
    {
        var (svc, _) = Create();
        var task = svc.ConfirmAsync("send.new-address");
        svc.ResolveConfirmation(false);
        Assert.False(await task);
    }

    [Fact]
    public async Task ConfirmAsync_ShowsInEveryMode_EvenWhenBakuIsOff()
    {
        var (svc, _) = Create();
        await svc.SetModeAsync(BakuMode.Off);
        var task = svc.ConfirmAsync("send.new-and-large");
        Assert.NotNull(svc.PendingConfirmation);
        svc.ResolveConfirmation(true);
        Assert.True(await task);
    }

    [Fact]
    public async Task ConfirmAsync_NewRequest_CancelsEarlierOne()
    {
        var (svc, _) = Create();
        var first = svc.ConfirmAsync("send.new-address");
        var second = svc.ConfirmAsync("send.large-amount");
        Assert.False(await first);
        svc.ResolveConfirmation(true);
        Assert.True(await second);
    }

    [Fact]
    public async Task ConfirmAsync_UnknownId_ReturnsTrue_SoLegitimateActionsAreNeverBlocked()
    {
        var (svc, _) = Create();
        Assert.True(await svc.ConfirmAsync("does.not.exist"));
    }

    // ── Send risk -> one message (A1: single combined dialog) ──

    [Theory]
    [InlineData(true, true, "send.new-and-large")]
    [InlineData(true, false, "send.new-address")]
    [InlineData(false, true, "send.large-amount")]
    public async Task ConfirmSendRiskAsync_PicksOneCombinedMessage(bool isNew, bool isLarge, string expectedId)
    {
        var (svc, _) = Create();
        var task = svc.ConfirmSendRiskAsync(new DigiByte.Wallet.Services.SendRiskAssessment(isLarge, isNew));
        Assert.Equal(expectedId, svc.PendingConfirmation!.Id);
        svc.ResolveConfirmation(true);
        Assert.True(await task);
    }

    [Fact]
    public async Task ConfirmSendRiskAsync_NoRisk_DoesNotShowADialog()
    {
        var (svc, _) = Create();
        Assert.True(await svc.ConfirmSendRiskAsync(new DigiByte.Wallet.Services.SendRiskAssessment(false, false)));
        Assert.Null(svc.PendingConfirmation);
    }

    // ── Catalog sanity (data-model.md Message Catalog) ──

    [Fact]
    public void Catalog_WarningAndCritical_AreNeverDismissible()
    {
        foreach (var m in BakuCatalog.All)
        {
            if (m.Severity is BakuSeverity.Warning or BakuSeverity.Critical)
                Assert.False(m.Dismissible, m.Id);
        }
    }

    [Fact]
    public void Catalog_ConfirmationMessages_AreWarningOrCritical()
    {
        foreach (var m in BakuCatalog.All.Where(m => m.RequiresConfirmation))
            Assert.True(m.Severity is BakuSeverity.Warning or BakuSeverity.Critical, m.Id);
    }

    [Fact]
    public void Catalog_ReferencedPoses_ExistOnDisk()
    {
        var root = FindWebRoot();
        foreach (var m in BakuCatalog.All)
            Assert.True(File.Exists(Path.Combine(root, "wwwroot", BakuCatalog.PosePath(m.PoseId))), $"{m.Id} -> {m.PoseId}");
    }

    private static string FindWebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DigiByte.Web")))
            dir = dir.Parent;
        return dir == null
            ? throw new DirectoryNotFoundException("repo root not found")
            : Path.Combine(dir.FullName, "src", "DigiByte.Web");
    }

    private sealed class FakeStorageJs : IJSRuntime
    {
        public Dictionary<string, string> Store { get; }
        public bool ThrowOnInvoke { get; set; }

        public FakeStorageJs(Dictionary<string, string>? seed = null) =>
            Store = seed != null ? new(seed) : new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (ThrowOnInvoke) throw new JSException("storage unavailable");
            switch (identifier)
            {
                case "localStorage.getItem":
                    var key = (string)args![0]!;
                    object? value = Store.TryGetValue(key, out var v) ? v : null;
                    return new ValueTask<TValue>((TValue)value!);
                case "localStorage.setItem":
                    Store[(string)args![0]!] = (string)args[1]!;
                    break;
            }
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
