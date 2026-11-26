using System.Text.Json;
using DigiByte.Pay.Web.Components;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared;
using DigiByte.Pay.Web.Components.Shared.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DigiByte.Pay.Web.Tests;

// Public website + developer docs: heading anchors, menu highlighting, prerendered pages,
// valid JSON samples and "On this page" links that land somewhere.
public class DocsSiteTests : BunitContext
{
    public DocsSiteTests()
    {
        Services.AddSingleton(new ToastService());
        Services.AddSingleton(new PayApiPublicUrl("http://localhost:5008"));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void GoTo(string path) => Services.GetRequiredService<NavigationManager>().NavigateTo(path);

    // ----- DocsHeading -----

    [Fact]
    public void A_heading_with_an_id_is_a_link_target_with_a_hash_link_to_itself()
    {
        GoTo("/docs/api");
        var cut = Render<DocsHeading>(p => p
            .Add(x => x.Id, "webhooks")
            .AddChildContent("Webhooks"));

        var h2 = cut.Find("h2");
        Assert.Equal("webhooks", h2.GetAttribute("id"));
        Assert.Contains("dp-docs-h", h2.ClassList);
        var anchor = h2.QuerySelector("a.dp-docs-anchor")!;
        // The page path is included: with <base href="/"> a bare "#webhooks" would go to the home page.
        Assert.Equal("/docs/api#webhooks", anchor.GetAttribute("href"));
        Assert.Equal("Link to this section", anchor.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_heading_without_an_id_has_no_anchor()
    {
        var cut = Render<DocsHeading>(p => p.AddChildContent("Next"));
        var h2 = cut.Find("h2");
        Assert.False(h2.HasAttribute("id"));
        Assert.Empty(cut.FindAll("a"));
    }

    [Fact]
    public void Level_three_renders_a_sub_heading()
    {
        GoTo("/docs");
        var cut = Render<DocsHeading>(p => p.Add(x => x.Id, "qs-key").Add(x => x.Level, 3).AddChildContent("1. Get an API key"));
        Assert.Equal("qs-key", cut.Find("h3").GetAttribute("id"));
        Assert.Equal("/docs#qs-key", cut.Find("h3 a").GetAttribute("href"));
    }

    // ----- Site menu -----

    [Theory]
    [InlineData("/features", "/features")]
    [InlineData("/docs", "/docs")]
    [InlineData("/docs/", "/docs")]
    [InlineData("/docs/api", "/docs")]
    [InlineData("/docs/woocommerce", "/docs")]
    [InlineData("/sdks", "/docs")]
    [InlineData("/docs/pos", "/docs/pos")]     // the longer match wins over Developers
    [InlineData("/DOCS/API", "/docs")]
    [InlineData("/about", "/about")]
    [InlineData("/", null)]                     // home lights up nothing
    [InlineData("/featuresque", null)]          // a prefix of a word isn't a sub-page
    [InlineData("/merchant", null)]
    public void Sub_pages_light_up_their_parent_in_the_menu(string path, string? expected) =>
        Assert.Equal(expected, SiteNav.ActiveHref(path));

    [Fact]
    public void The_rendered_menu_marks_developers_on_the_api_reference()
    {
        GoTo("/docs/api");
        var cut = Render<SiteNav>();
        var current = cut.FindAll("nav.dp-sitenav a[aria-current='page']");
        var link = Assert.Single(current);
        Assert.Equal("/docs", link.GetAttribute("href"));
        Assert.Equal("Developers", link.TextContent.Trim());
    }

    [Fact]
    public void The_menu_names_the_developer_section_once_and_consistently()
    {
        var labels = SiteNav.Links.Select(l => l.Label).ToArray();
        Assert.Equal(new[] { "Features", "Use cases", "How it works", "POS", "Developers", "About" }, labels);
    }

    // ----- Prerendering -----

    [Theory]
    [InlineData("/", true)]
    [InlineData("/features", true)]
    [InlineData("/about", true)]
    [InlineData("/sdks", true)]
    [InlineData("/docs", true)]
    [InlineData("/docs/", true)]
    [InlineData("/docs/api", true)]
    [InlineData("/docs/woocommerce", true)]
    [InlineData("/pay/ses_abc", true)]          // shopper pages were already prerendered
    [InlineData("/merchant", false)]            // dashboard reads browser storage first
    [InlineData("/merchant/signin", false)]
    [InlineData("/pos", false)]
    [InlineData("/docsx", false)]
    [InlineData(null, false)]
    public void Public_pages_are_prerendered_and_the_app_is_not(string? path, bool prerendered) =>
        Assert.Equal(prerendered, App.UsesPrerendering(path));

    // ----- Code samples -----

    public static TheoryData<string> DocsPages => new() { "docs", "api" };

    [Theory]
    [MemberData(nameof(DocsPages))]
    public void Every_json_sample_is_valid_json(string page)
    {
        var samples = Render(Page(page)).FindComponents<CodeBlock>()
            .Where(c => c.Instance.Lang == "json")
            .Select(c => c.Find("code").TextContent)
            .ToList();

        Assert.NotEmpty(samples);
        foreach (var json in samples)
        {
            var ex = Record.Exception(() => JsonDocument.Parse(json));
            Assert.True(ex is null, $"Invalid JSON sample on /{page}: {ex?.Message}\n{json}");
        }
    }

    [Theory]
    [MemberData(nameof(DocsPages))]
    public void Code_samples_carry_no_inline_comments_in_json(string page)
    {
        // The old samples had `// optional` notes inside JSON bodies; notes now live in parameter lists.
        foreach (var block in Render(Page(page)).FindComponents<CodeBlock>().Where(c => c.Instance.Lang == "json"))
            Assert.DoesNotContain("//", block.Find("code").TextContent.Replace("https://", "").Replace("http://", ""));
    }

    [Fact]
    public void The_api_reference_documents_the_endpoints_integrators_need()
    {
        var markup = Render(Page("api")).Markup;
        foreach (var path in new[]
        {
            "/v1/pay/sessions/{id}/refund", "/v1/pay/sessions/{id}/void", "/v1/pay/me/audit",
            "/v1/pay/stores/{id}/analytics", "/v1/pay/stores/{id}/webhook-secret/rotate",
            "/v1/pay/public/stats", "/v1/pay/public/price",
            "/v1/pay/test/demo-session", "/v1/pay/test/sessions/{id}/advance",
        })
        {
            Assert.Contains(path, markup);
        }
        foreach (var evt in new[] { "session.seen", "session.paid", "session.confirmed", "session.underpaid", "session.expired", "session.refunded", "session.voided", "webhook.test" })
            Assert.Contains(evt, markup);
        Assert.Contains("digipay-checkout-status", markup);
        Assert.DoesNotContain("checkout_url", markup);
    }

    [Fact]
    public void The_embed_callbacks_and_messages_are_documented()
    {
        var api = Render(Page("api")).Markup;
        foreach (var name in new[] { "onStatus", "onSuccess", "closeDelay", "digipay-checkout-status", "digipay-checkout-return", "digipay-checkout-escape" })
            Assert.Contains(name, api);
        var guide = Render(Page("docs")).Markup;
        Assert.Contains("onSuccess", guide);
        Assert.Contains("closeDelay", guide);
    }

    [Fact]
    public void The_quickstart_runs_end_to_end_on_a_testnet_store()
    {
        var guide = Render(Page("docs")).Markup;
        Assert.Contains("\"network\": \"testnet\"", System.Net.WebUtility.HtmlDecode(guide));
        Assert.Contains("/v1/pay/test/sessions/$SESSION_ID/advance", guide);
        Assert.Contains("advance only works on testnet or regtest stores", guide);
        Assert.Contains("Sign in as dev merchant", guide);
        Assert.Contains("DigiPay:DevSignIn=true", guide);
        // The old workaround (advance refuses every non-demo id) is gone.
        Assert.DoesNotContain("refuses any id", guide);

        var api = Render(Page("api")).Markup;
        Assert.Contains("advance only works on testnet or regtest stores", api);
        Assert.DoesNotContain("never touch real sessions", api);
    }

    // ----- "On this page" -----

    public static TheoryData<string> AllDocsPages => new() { "docs", "api", "pos", "woocommerce" };

    [Theory]
    [MemberData(nameof(AllDocsPages))]
    public void Every_on_this_page_link_lands_on_an_element_and_ids_are_unique(string page)
    {
        var cut = Render(Page(page));
        var ids = cut.FindAll("[id]").Select(e => e.GetAttribute("id")!).ToList();
        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, $"Duplicate ids on /{page}: {string.Join(", ", duplicates)}");

        var tocLinks = cut.FindAll(".dp-docs-side .dp-docs-toc a");
        Assert.NotEmpty(tocLinks);
        foreach (var link in tocLinks)
        {
            var target = link.GetAttribute("href")!.Split('#')[1];
            Assert.Contains(target, ids);
        }
    }

    [Theory]
    [MemberData(nameof(AllDocsPages))]
    public void Phones_get_a_collapsed_on_this_page_menu(string page)
    {
        var details = Render(Page(page)).Find("details.dp-docs-mobile");
        Assert.False(details.HasAttribute("open"));
        Assert.Contains("On this page", details.QuerySelector("summary")!.TextContent);
    }

    private RenderFragment Page(string page)
    {
        var (path, type) = page switch
        {
            "docs" => ("/docs", typeof(Docs)),
            "api" => ("/docs/api", typeof(ApiDocs)),
            "pos" => ("/docs/pos", typeof(PosDocs)),
            _ => ("/docs/woocommerce", typeof(WooCommerceDocs)),
        };
        GoTo(path);
        return b => { b.OpenComponent(0, type); b.CloseComponent(); };
    }
}