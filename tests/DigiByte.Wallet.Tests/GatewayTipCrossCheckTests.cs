using System.Net;
using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>Comparing the wallet verified chain with an independent explorer (T054, T067, spec FR-013).</summary>
public class GatewayTipCrossCheckTests
{
    private const int Cp = 24563100;

    private sealed class MemoryStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _data = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value) { _data[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _data.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_data.ContainsKey(key));
        public Task ClearAsync() { _data.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => Task.FromResult(_data.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private static readonly Lazy<Dictionary<int, byte[]>> HeaderBytes = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private static async Task<GatewayHeaderStore> StoreAsync(int upTo = Cp + 250)
    {
        var store = new GatewayHeaderStore(new MemoryStorage(), Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await store.InitializeAsync();
        await store.AppendRangeAsync(Enumerable.Range(Cp + 1, upTo - Cp).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        return store;
    }

    /// <summary>An explorer that knows the real chain, except where a test changes or hides a height.</summary>
    private sealed class Explorer(GatewayHeaderStore headers) : IExplorerBlockHashSource
    {
        public HashSet<int> Hidden { get; } = [];
        public Dictionary<int, string> Different { get; } = [];
        public List<int> Asked { get; } = [];

        public Task<string?> GetBlockHashAsync(int height, CancellationToken cancellationToken = default)
        {
            Asked.Add(height);
            if (Hidden.Contains(height))
                return Task.FromResult<string?>(null);
            if (Different.TryGetValue(height, out var other))
                return Task.FromResult<string?>(other);
            return Task.FromResult(headers.HeaderAt(height)?.Hash.ToString());
        }
    }

    private static readonly string Other = new('1', 64);

    [Fact]
    public async Task An_explorer_that_sees_the_same_chain_agrees_and_is_asked_at_the_tip_and_three_depths()
    {
        var store = await StoreAsync();
        var explorer = new Explorer(store);

        var result = await new GatewayTipCrossCheck(store, explorer).CheckAsync();

        Assert.Equal(CrossCheckResult.Agrees, result);
        Assert.Equal([Cp + 250, Cp + 249, Cp + 240, Cp + 150], explorer.Asked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public async Task A_different_block_at_a_settled_height_is_a_mismatch(int offset)
    {
        var store = await StoreAsync();
        var explorer = new Explorer(store);
        explorer.Different[Cp + 250 - offset] = Other;

        Assert.Equal(CrossCheckResult.Mismatch, await new GatewayTipCrossCheck(store, explorer).CheckAsync());
    }

    [Fact]
    public async Task A_difference_at_the_tip_alone_is_a_race_between_two_honest_views_not_a_mismatch()
    {
        var store = await StoreAsync();
        var explorer = new Explorer(store);
        explorer.Different[Cp + 250] = Other;

        Assert.Equal(CrossCheckResult.Inconclusive, await new GatewayTipCrossCheck(store, explorer).CheckAsync());
    }

    [Fact]
    public async Task An_explorer_a_block_behind_has_no_answer_for_the_tip_and_the_rest_still_decide()
    {
        var store = await StoreAsync();
        var explorer = new Explorer(store);
        explorer.Hidden.Add(Cp + 250);

        Assert.Equal(CrossCheckResult.Agrees, await new GatewayTipCrossCheck(store, explorer).CheckAsync());
    }

    [Fact]
    public async Task No_explorer_answer_at_all_is_inconclusive_so_the_gateway_is_neither_promoted_nor_demoted()
    {
        var store = await StoreAsync();
        var explorer = new Explorer(store);
        foreach (var h in new[] { 0, 1, 10, 100 })
            explorer.Hidden.Add(Cp + 250 - h);

        Assert.Equal(CrossCheckResult.Inconclusive, await new GatewayTipCrossCheck(store, explorer).CheckAsync());
    }

    [Fact]
    public async Task Heights_below_the_wallet_headers_are_not_asked_about()
    {
        var store = await StoreAsync(upTo: Cp + 30);
        var explorer = new Explorer(store);

        var result = await new GatewayTipCrossCheck(store, explorer).CheckAsync();

        Assert.Equal(CrossCheckResult.Agrees, result);
        Assert.DoesNotContain(Cp + 30 - 100, explorer.Asked);
    }

    // ---- the explorer hash source ----

    private sealed class Handler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request.RequestUri.ToString()));
        }
    }

    private static readonly string Hash = new string('a', 32) + new string('B', 32);

    private static readonly ExplorerHashEndpoint[] Two =
    [
        new("https://esplora.example.org/api", ExplorerHashKind.Esplora),
        new("https://blockbook.example.org", ExplorerHashKind.Blockbook),
    ];

    [Fact]
    public async Task The_urls_follow_each_explorers_own_form()
    {
        Assert.Equal("https://esplora.example.org/api/block-height/42", Two[0].UrlFor(42));
        Assert.Equal("https://blockbook.example.org/api/v2/block-index/42", Two[1].UrlFor(42));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_first_explorer_that_answers_is_used_and_the_hash_comes_back_in_lowercase()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Hash + "\n") });
        var source = new ExplorerBlockHashSource(new HttpClient(handler), Two);

        var hash = await source.GetBlockHashAsync(42);

        Assert.Equal(Hash.ToLowerInvariant(), hash);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task A_failing_explorer_is_skipped_and_the_next_one_is_asked()
    {
        var handler = new Handler(url => url.Contains("esplora")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"blockHash\":\"" + Hash + "\"}") });
        var source = new ExplorerBlockHashSource(new HttpClient(handler), Two);

        Assert.Equal(Hash.ToLowerInvariant(), await source.GetBlockHashAsync(42));
        Assert.Equal(2, handler.Urls.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("<html>oops</html>")]
    [InlineData("{\"blockHash\":42}")]
    [InlineData("{}")]
    public async Task An_answer_that_is_not_a_hash_is_no_answer(string body)
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var source = new ExplorerBlockHashSource(new HttpClient(handler), Two);

        Assert.Null(await source.GetBlockHashAsync(42));
    }

    [Fact]
    public async Task A_connection_error_is_no_answer_and_never_an_exception()
    {
        var source = new ExplorerBlockHashSource(new HttpClient(new Handler(_ => throw new HttpRequestException("down"))), Two);

        Assert.Null(await source.GetBlockHashAsync(42));
    }

    [Fact]
    public async Task A_slow_explorer_runs_out_of_the_budget_and_is_no_answer()
    {
        var slow = new SlowHandler();
        var source = new ExplorerBlockHashSource(new HttpClient(slow), Two, TimeSpan.FromMilliseconds(200));
        var start = DateTime.UtcNow;

        Assert.Null(await source.GetBlockHashAsync(42));
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(3));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
