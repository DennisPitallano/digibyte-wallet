using DigiByte.Web.Layout;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Which menu a page gets: the personal one, or the shared-wallet one with the tab that is current
/// (spec 026, FR-002 and FR-006). Decided from the address alone, so the menu and the page cannot disagree.
/// </summary>
public class ShellContextTests
{
    [Theory]
    [InlineData("multisig/abc123", SharedSection.Overview)]
    [InlineData("multisig/abc123/activity", SharedSection.Activity)]
    [InlineData("multisig/abc123/pending", SharedSection.Pending)]
    [InlineData("multisig/abc123/pending/p-77", SharedSection.Pending)]
    [InlineData("multisig/abc123/send", SharedSection.None)]
    public void Pages_of_one_shared_wallet_get_the_shared_menu(string path, SharedSection section)
    {
        var context = ShellContext.Resolve(path);

        Assert.Equal(ShellKind.Shared, context.Kind);
        Assert.Equal("abc123", context.SharedWalletId);
        Assert.Equal(section, context.Section);
    }

    [Theory]
    [InlineData("multisig")]
    [InlineData("multisig/create")]
    [InlineData("multisig/import")]
    [InlineData("multisig/room")]
    [InlineData("multisig/sign-room")]
    [InlineData("multisig/Create")]
    [InlineData("multisig/SIGN-ROOM")]
    [InlineData("help/multisig")]
    [InlineData("")]
    [InlineData("transactions")]
    [InlineData("payments")]
    [InlineData("settings")]
    [InlineData("send")]
    public void Every_other_page_gets_the_personal_menu(string path)
    {
        var context = ShellContext.Resolve(path);

        Assert.Equal(ShellKind.Personal, context.Kind);
        Assert.Null(context.SharedWalletId);
        Assert.Equal(SharedSection.None, context.Section);
    }

    [Fact]
    public void A_missing_path_is_personal()
    {
        Assert.Equal(ShellKind.Personal, ShellContext.Resolve(null).Kind);
    }

    [Theory]
    [InlineData("/multisig/abc123/")]
    [InlineData("multisig/abc123/")]
    [InlineData("/multisig/abc123")]
    [InlineData("MULTISIG/abc123")]
    public void Slashes_and_the_case_of_route_words_do_not_matter(string path)
    {
        var context = ShellContext.Resolve(path);

        Assert.Equal(ShellKind.Shared, context.Kind);
        Assert.Equal("abc123", context.SharedWalletId);
        Assert.Equal(SharedSection.Overview, context.Section);
    }

    [Theory]
    [InlineData("multisig/abc123/Pending", SharedSection.Pending)]
    [InlineData("multisig/abc123/ACTIVITY", SharedSection.Activity)]
    public void Section_words_match_in_any_case(string path, SharedSection section)
    {
        Assert.Equal(section, ShellContext.Resolve(path).Section);
    }

    [Fact]
    public void The_shared_wallet_id_keeps_its_own_case()
    {
        Assert.Equal("AbC-123", ShellContext.Resolve("multisig/AbC-123/pending").SharedWalletId);
    }

    [Fact]
    public void An_escaped_id_is_unescaped()
    {
        Assert.Equal("a b", ShellContext.Resolve("multisig/a%20b").SharedWalletId);
    }

    [Theory]
    [InlineData("multisig/abc123?scanned=2")]
    [InlineData("multisig/abc123/pending?scanned=2#top")]
    public void A_query_or_fragment_is_ignored(string path)
    {
        var context = ShellContext.Resolve(path);

        Assert.Equal(ShellKind.Shared, context.Kind);
        Assert.Equal("abc123", context.SharedWalletId);
    }

    [Fact]
    public void An_unknown_page_under_a_shared_wallet_still_belongs_to_it()
    {
        var context = ShellContext.Resolve("multisig/abc123/something-new");

        Assert.Equal(ShellKind.Shared, context.Kind);
        Assert.Equal(SharedSection.None, context.Section);
    }

    [Fact]
    public void The_personal_context_is_one_shared_value()
    {
        Assert.Equal(ShellContext.Personal, ShellContext.Resolve("transactions"));
    }
}

/// <summary>The shared-wallet menu's own words exist in every language the menu is offered in (spec 026, FR-037).</summary>
public class SharedMenuTextTests
{
    private static readonly string[] Keys =
    [
        "nav.overview", "nav.overview.aria", "nav.pending", "nav.pending.aria",
        "nav.count.pending.one", "nav.count.pending.other",
        "nav.shared.activity.aria", "nav.shared.label", "nav.shared.switch", "scan.leaving_shared",
    ];

    private static Dictionary<string, string> Load(string locale)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, "src", "DigiByte.Web", "wwwroot", "locales", locale + ".json");
            if (File.Exists(file))
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not find the locale file for " + locale);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Every_shared_menu_string_is_translated(string locale)
    {
        var strings = Load(locale);
        foreach (var key in Keys)
            Assert.True(strings.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{locale} has no text for '{key}'");
    }

    [Fact]
    public void Both_languages_keep_the_same_placeholders()
    {
        var en = Load("en");
        var es = Load("es");
        foreach (var key in Keys)
        {
            foreach (var placeholder in new[] { "{0}", "{1}" })
                Assert.Equal(en[key].Contains(placeholder), es[key].Contains(placeholder));
        }
    }

    [Fact]
    public void Spanish_does_not_fall_back_to_the_English_words()
    {
        var en = Load("en");
        var es = Load("es");
        Assert.NotEqual(en["nav.overview"], es["nav.overview"]);
        Assert.NotEqual(en["nav.pending"], es["nav.pending"]);
    }
}
