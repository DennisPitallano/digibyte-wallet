using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

public class SponsorListParserTests
{
    private const string Base = "https://raw.example.test/sponsors/data/";

    private static SponsorListParseResult Parse(string json) => SponsorListParser.Parse(json, Base);

    [Fact]
    public void ValidList_ReturnsEntriesNewestFirst()
    {
        var r = Parse("""{"version":1,"sponsors":[{"name":"Old"},{"name":"Mid"},{"name":"New"}]}""");
        Assert.True(r.Readable);
        Assert.Equal(["New", "Mid", "Old"], r.Entries.Select(e => e.Name));
    }

    [Fact]
    public void NameOnlyEntry_HasNoImageOrLink()
    {
        var r = Parse("""{"version":1,"sponsors":[{"name":"Solo"}]}""");
        var e = Assert.Single(r.Entries);
        Assert.Null(e.ImageUrl);
        Assert.Null(e.Link);
    }

    [Fact]
    public void ImageAndLink_AreResolved()
    {
        var r = Parse("""{"version":1,"sponsors":[{"name":"A","image":"abc123def456.webp","link":"https://a.example/x"}]}""");
        var e = Assert.Single(r.Entries);
        Assert.Equal(Base + "images/abc123def456.webp", e.ImageUrl);
        Assert.Equal("https://a.example/x", e.Link);
    }

    [Fact]
    public void InvalidEntries_AreSkippedIndividually()
    {
        var r = Parse("""
            {"version":1,"sponsors":[
              {"name":"Good"},
              {"name":""},
              {"link":"https://no-name.example"},
              {"name":"BadLink","link":"http://insecure.example"},
              {"name":"BadImage","image":"../x.png"},
              "not-an-object",
              42,
              {"name":"AlsoGood"}
            ]}
            """);
        Assert.True(r.Readable);
        // Bad link / bad image are degraded (entry kept, field dropped); empty name, missing name and non-objects are skipped.
        Assert.Equal(["AlsoGood", "BadImage", "BadLink", "Good"], r.Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Null(r.Entries.Single(e => e.Name == "BadLink").Link);
        Assert.Null(r.Entries.Single(e => e.Name == "BadImage").ImageUrl);
        Assert.Equal(4, r.SkippedCount);
    }

    [Fact]
    public void Duplicates_SameNameLinkImage_ShownOnce_FirstOccurrenceWins()
    {
        var r = Parse("""
            {"version":1,"sponsors":[
              {"name":"Alice","link":"https://a.example"},
              {"name":"alice","link":"https://a.example"},
              {"name":"Alice","link":"https://b.example"}
            ]}
            """);
        Assert.Equal(2, r.Entries.Count);
        Assert.Equal(1, r.SkippedCount);
        Assert.Equal(["https://b.example", "https://a.example"], r.Entries.Select(e => e.Link));
        Assert.Equal("Alice", r.Entries[1].Name); // the first occurrence's spelling is kept
    }

    [Theory]
    [InlineData("""{"version":2,"sponsors":[]}""")]
    [InlineData("""{"sponsors":[]}""")]
    [InlineData("""{"version":"1","sponsors":[]}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"sponsors":{}}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    public void WrongVersionOrShape_IsUnreadable(string json)
    {
        var r = Parse(json);
        Assert.False(r.Readable);
        Assert.Empty(r.Entries);
    }

    [Fact]
    public void EmptyList_IsReadableAndEmpty()
    {
        var r = Parse("""{"version":1,"sponsors":[]}""");
        Assert.True(r.Readable);
        Assert.Empty(r.Entries);
    }

    [Fact]
    public void OptionalIdField_IsAcceptedAndIgnored()
    {
        var r = Parse("""{"version":1,"sponsors":[{"id":"0123456789ab","name":"A"}]}""");
        Assert.Equal("A", Assert.Single(r.Entries).Name);
    }

    [Fact]
    public void UnknownFields_AreIgnored_AndNeverExposed()
    {
        var r = Parse("""{"version":1,"sponsors":[{"name":"A","amount":500,"tier":"gold","date":"2026-01-01"}]}""");
        var e = Assert.Single(r.Entries);
        Assert.DoesNotContain(typeof(SponsorEntry).GetProperties(), p => new[] { "Amount", "Tier", "Date" }.Contains(p.Name));
        Assert.Equal("A", e.Name);
    }

    [Fact]
    public void MoreThan1000Entries_KeepsTheNewest1000()
    {
        var items = string.Join(",", Enumerable.Range(1, 1100).Select(i => $$"""{"name":"N{{i}}"}"""));
        var r = Parse($$"""{"version":1,"sponsors":[{{items}}]}""");
        Assert.Equal(1000, r.Entries.Count);
        Assert.Equal("N1100", r.Entries[0].Name);
        Assert.Equal("N101", r.Entries[^1].Name);
    }

    [Fact]
    public void MarkupInName_IsKeptAsPlainText()
    {
        var r = Parse("""{"version":1,"sponsors":[{"name":"<b>Bold</b>"}]}""");
        Assert.Equal("<b>Bold</b>", Assert.Single(r.Entries).Name);
    }

    [Fact]
    public void RemovingAnEntry_MeansItIsNoLongerReturned()
    {
        var before = Parse("""{"version":1,"sponsors":[{"name":"A"},{"name":"B"}]}""");
        var after = Parse("""{"version":1,"sponsors":[{"name":"A"}]}""");
        Assert.Contains(before.Entries, e => e.Name == "B");
        Assert.DoesNotContain(after.Entries, e => e.Name == "B");
    }

    [Fact]
    public void BaseUrl_WithoutTrailingSlash_StillResolves()
    {
        var r = SponsorListParser.Parse("""{"version":1,"sponsors":[{"name":"A","image":"a.png"}]}""", "https://raw.example.test/x/data");
        Assert.Equal("https://raw.example.test/x/data/images/a.png", Assert.Single(r.Entries).ImageUrl);
    }
}
