using DigiByte.Pay.Web.Components.Shared.Ui;

namespace DigiByte.Pay.Web.Tests;

// Mirrors src/DigiByte.Pay.Web/tests/js/theme.test.mjs (data-model §1–§2).
public class ThemePrecedenceTests
{
    [Theory]
    [InlineData("dark", false, "dark")]
    [InlineData("light", true, "light")]
    public void A_saved_light_or_dark_choice_is_kept(string saved, bool deviceDark, string expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve("/", null, saved, null, deviceDark));

    [Theory]
    [InlineData(true, "dark")]
    [InlineData(false, "light")]
    public void No_saved_choice_follows_the_device(bool deviceDark, string expected)
    {
        Assert.Equal(expected, ThemeResolver.Resolve("/", null, null, null, deviceDark));
        Assert.Equal("system", ThemeResolver.Mode("/", null, null));
    }

    [Fact]
    public void An_invalid_saved_value_is_treated_as_system()
    {
        Assert.Equal("system", ThemeResolver.Mode("/merchant", "purple", null));
        Assert.Equal("dark", ThemeResolver.Resolve("/merchant", null, "purple", null, deviceDark: true));
    }

    [Fact]
    public void Shop_theme_on_a_pay_page_wins_over_a_saved_dark_choice() =>
        Assert.Equal("light", ThemeResolver.Resolve("/pay/ses_x", "?theme=light", "dark", null, deviceDark: true));

    [Theory]
    [InlineData("/pay/ses_x/receipt", "?theme=DARK")]
    [InlineData("/pay/button", "to=dgb1abc&theme=dark")]
    public void Shop_theme_is_case_insensitive_and_works_on_receipt_and_donation_page(string path, string query) =>
        Assert.Equal("dark", ThemeResolver.Resolve(path, query, null, null, deviceDark: false));

    [Fact]
    public void Shop_theme_with_another_value_is_ignored() =>
        Assert.Equal("dark", ThemeResolver.Resolve("/pay/ses_x", "?theme=purple", null, null, deviceDark: true));

    [Fact]
    public void Shop_theme_outside_pay_pages_is_ignored() =>
        Assert.Equal("light", ThemeResolver.Resolve("/merchant", "?theme=dark", null, null, deviceDark: false));

    [Fact]
    public void The_pos_screen_starts_dark_and_uses_its_own_key()
    {
        Assert.Equal("dark", ThemeResolver.Resolve("/pos", null, null, null, deviceDark: false));
        Assert.Equal("light", ThemeResolver.Resolve("/pos", null, null, "light", deviceDark: true));
        Assert.Equal("dark", ThemeResolver.Resolve("/pos", null, "light", null, deviceDark: false));
    }

    [Fact]
    public void Shop_theme_does_not_apply_on_the_pos_screen() =>
        Assert.Equal("dark", ThemeResolver.Resolve("/pos", "?theme=light", null, null, deviceDark: false));
}
