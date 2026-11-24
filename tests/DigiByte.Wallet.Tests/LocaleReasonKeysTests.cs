using System.Text.Json;
using DigiByte.Wallet.Services;
using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Screens translate wallet messages by their reason, never by matching the English sentence (spec 028 follow-up).
/// Every reason has a key in both languages, and the English text is the wording the wallet service itself uses.
/// </summary>
public class LocaleReasonKeysTests
{
    private static readonly string LocalesDir = FindLocalesDir();

    private static string FindLocalesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "src.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Could not find src/src.sln."),
            "src", "DigiByte.Web", "wwwroot", "locales");
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LocalesDir, code + ".json")))!;

    internal static Dictionary<string, string> English => Load("en");

    public static TheoryData<string> AllKeys()
    {
        var data = new TheoryData<string>();
        foreach (var r in Enum.GetValues<ReceiveAddressFailure>()) data.Add(LocaleReasonKeys.For(r));
        foreach (var b in Enum.GetValues<NodeBanner>().Where(b => b != NodeBanner.None)) data.Add(LocaleReasonKeys.For(b)!);
        foreach (var u in Enum.GetValues<UpdateCheckResult>()) data.Add(LocaleReasonKeys.For(u));
        return data;
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void EveryReason_HasAKey_InEnglishAndSpanish(string key)
    {
        Assert.True(Load("en").ContainsKey(key), $"en.json has no {key}");
        Assert.True(Load("es").ContainsKey(key), $"es.json has no {key}");
    }

    [Fact]
    public void NoBanner_HasNoKey() => Assert.Null(LocaleReasonKeys.For(NodeBanner.None));

    [Fact]
    public void EachReceiveFailure_HasItsOwnKey()
    {
        var keys = Enum.GetValues<ReceiveAddressFailure>().Select(LocaleReasonKeys.For).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BannerEnglish_IsTheWordingTheStatusUses(bool allFailed)
    {
        var view = NodeSourceStatus.Compute(NodeSourceState.NotReady, NodeNotReadyReason.ReadFailures, nodeWasActive: true, allSourcesFailed: allFailed);

        Assert.Equal(view.BannerText, English[LocaleReasonKeys.For(view.Banner)!]);
    }

    [Fact]
    public void ReceiveEnglish_IsTheWordingTheWalletUses()
    {
        var en = English;
        var limit = WalletService.ReceiveGapLimit;
        Assert.Equal("This wallet has a single address.", en[LocaleReasonKeys.For(ReceiveAddressFailure.SingleAddress)]);
        Assert.Equal("Could not save the new address. Your current address is still good to use.", en[LocaleReasonKeys.For(ReceiveAddressFailure.SaveFailed)]);
        Assert.Equal($"You have {limit} unused addresses in a row. Use one of them first: restoring from your seed only looks {limit} addresses past the last one used.",
            string.Format(en[LocaleReasonKeys.For(ReceiveAddressFailure.TooManyUnusedPastLastUsed)], limit));
        Assert.Equal($"You have {limit} unused addresses in a row. Use one of them first: the wallet only keeps looking {limit} addresses past the last one that was paid.",
            string.Format(en[LocaleReasonKeys.For(ReceiveAddressFailure.TooManyUnusedPastLastPaid)], limit));
    }

    [Fact]
    public void UpdateEnglish_KeepsItsWording()
    {
        var en = English;
        Assert.Equal("Update found. Reloading…", en[LocaleReasonKeys.For(UpdateCheckResult.Ready)]);
        Assert.Equal("Update found. Still downloading; it will be offered when ready.", en[LocaleReasonKeys.For(UpdateCheckResult.Downloading)]);
        Assert.Equal("Could not check. Try again.", en[LocaleReasonKeys.For(UpdateCheckResult.Failed)]);
        Assert.Equal("Already up to date ✓", en[LocaleReasonKeys.For(UpdateCheckResult.UpToDate)]);
    }
}
