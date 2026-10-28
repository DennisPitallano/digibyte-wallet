using DigiByte.Web.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The wallet's runtime config.json (specs/005-wallet-own-node-source/contracts/runtime-config.md): only OwnNode settings are
/// accepted, anything missing or invalid means "no overrides", and nothing ever throws.
/// </summary>
public class RuntimeConfigLoaderTests
{
    [Fact]
    public void ValidFile_IsFlattenedToConfigurationKeys()
    {
        var values = RuntimeConfigLoader.Parse("""
            { "OwnNode": { "Enabled": true, "ApiUrl": "https://node.example.org", "CheckIntervalSeconds": 20,
                           "StablePeriodSeconds": 90, "HeightTolerance": 8, "DevView": false } }
            """);

        Assert.Equal("true", values["OwnNode:Enabled"]);
        Assert.Equal("https://node.example.org", values["OwnNode:ApiUrl"]);
        Assert.Equal("20", values["OwnNode:CheckIntervalSeconds"]);
        Assert.Equal("90", values["OwnNode:StablePeriodSeconds"]);
        Assert.Equal("8", values["OwnNode:HeightTolerance"]);
        Assert.Equal("false", values["OwnNode:DevView"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("<html>404</html>")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("{ \"OwnNode\": ")]
    public void MissingOrInvalid_MeansNoOverrides_AndNeverThrows(string? json)
    {
        Assert.Empty(RuntimeConfigLoader.Parse(json));
    }

    [Fact]
    public void OnlyOwnNodeKeysAreAccepted_EverythingElseIsIgnored()
    {
        var values = RuntimeConfigLoader.Parse("""
            { "NodeApiUrl": "https://evil.example.org",
              "DigiDollar": { "Backend": "nodeapi", "SendEnabled": true },
              "PriceApiUrl": "https://evil.example.org/price",
              "OwnNode": { "Enabled": true },
              "OwnNodeExtra": { "Enabled": true } }
            """);

        Assert.Equal(["OwnNode:Enabled"], values.Keys);
    }

    [Fact]
    public void NestedObjectsAndArraysUnderOwnNode_AreNotAcceptedAsValues()
    {
        var values = RuntimeConfigLoader.Parse("""{ "OwnNode": { "Enabled": true, "Weird": { "A": 1 }, "List": [1,2], "Nothing": null } }""");

        Assert.Equal("true", values["OwnNode:Enabled"]);
        Assert.False(values.ContainsKey("OwnNode:Weird"));
        Assert.False(values.ContainsKey("OwnNode:List"));
        Assert.False(values.ContainsKey("OwnNode:Nothing"));
    }

    [Fact]
    public void KeysAreCaseInsensitiveLikeConfiguration_ButNormalisedToTheCanonicalSection()
    {
        var values = RuntimeConfigLoader.Parse("""{ "ownnode": { "Enabled": true } }""");

        Assert.Equal("true", values["OwnNode:Enabled"]);
    }

    [Fact]
    public async Task Fetch_ReturnsTheParsedFile()
    {
        var http = new HttpClient(new Handler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"OwnNode\":{\"Enabled\":true}}") }))
        { BaseAddress = new Uri("https://wallet.example.org/") };

        var values = await RuntimeConfigLoader.FetchAsync(http);

        Assert.Equal("true", values["OwnNode:Enabled"]);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.NotFound)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError)]
    public async Task Fetch_NonSuccess_MeansNoOverrides(System.Net.HttpStatusCode status)
    {
        var http = new HttpClient(new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent("{\"OwnNode\":{\"Enabled\":true}}") }))
        { BaseAddress = new Uri("https://wallet.example.org/") };

        Assert.Empty(await RuntimeConfigLoader.FetchAsync(http));
    }

    [Fact]
    public async Task Fetch_NetworkFailure_MeansNoOverrides_NeverThrows()
    {
        var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")))
        { BaseAddress = new Uri("https://wallet.example.org/") };

        Assert.Empty(await RuntimeConfigLoader.FetchAsync(http));
    }

    [Fact]
    public async Task Fetch_AsksForConfigJson_WithoutRelyingOnAnyCache()
    {
        HttpRequestMessage? seen = null;
        var http = new HttpClient(new Handler(req => { seen = req; return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") }; }))
        { BaseAddress = new Uri("https://wallet.example.org/") };

        await RuntimeConfigLoader.FetchAsync(http);

        Assert.Equal("https://wallet.example.org/config.json", seen!.RequestUri!.ToString());
        Assert.True(seen.Headers.CacheControl?.NoCache);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }
}
