using DigiByte.Pay.Web.Components;
using DigiByte.Pay.Web.Components.Pages;
using DigiByte.Pay.Web.Components.Shared;

namespace DigiByte.Pay.Web.Tests;

// /use-cases: one card per way of getting paid, from the same list the home page uses.
public class UseCasesTests : BunitContext
{
    [Fact]
    public void The_page_shows_every_use_case_with_its_anchor_points_and_call_to_action()
    {
        var cut = Render<UseCasesPage>();

        var cards = cut.FindAll("article.dp-uc-card");
        Assert.Equal(UseCases.All.Count, cards.Count);
        foreach (var uc in UseCases.All)
        {
            var card = cut.Find($"article#{uc.Key}");
            Assert.Contains(uc.Title, card.QuerySelector("h2")!.TextContent);
            Assert.Equal(uc.Points.Length, card.QuerySelectorAll(".dp-uc-points li").Length);
            Assert.NotNull(card.QuerySelector($"a.dp-uc-cta[href='{uc.CtaHref}']"));
            // The jump list links to each card.
            Assert.NotNull(cut.Find($".dp-uc-jump a[href='/use-cases#{uc.Key}']"));
        }
    }

    [Fact]
    public void Use_case_keys_are_unique_and_every_link_stays_on_the_site()
    {
        Assert.Equal(UseCases.All.Count, UseCases.All.Select(u => u.Key).Distinct().Count());
        var hrefs = UseCases.All.SelectMany(u => u.Tools.Select(t => t.Href).Append(u.CtaHref));
        Assert.All(hrefs, h => Assert.StartsWith("/", h));
    }

    [Fact]
    public void Use_cases_is_a_public_page_with_its_own_menu_entry()
    {
        Assert.True(App.IsPublicPage("/use-cases"));
        Assert.Equal("/use-cases", SiteNav.ActiveHref("/use-cases"));
        Assert.Contains(SiteNav.Links, l => l.Href == "/use-cases");
    }
}
