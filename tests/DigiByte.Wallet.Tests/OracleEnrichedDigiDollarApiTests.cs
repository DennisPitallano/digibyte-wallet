using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using Microsoft.Extensions.Time.Testing;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The wrapper that adds the oracle reading to the explorer-backed DigiDollar health (specs/014-digibyte-io-oracle): it never waits for the
/// oracle, adds the reading only to an available health on mainnet, and passes every other call straight through.
/// </summary>
public class OracleEnrichedDigiDollarApiTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeFeed : IOracleFeed
    {
        public OracleReading? Current { get; set; }
        public bool IsLoading { get; set; }
        public int Refreshes { get; private set; }
        public event Action? Changed;
        public void RequestRefresh() => Refreshes++;
        public void Raise() => Changed?.Invoke();
    }

    private static OracleReading Reading(bool err = false, bool freeze = false, bool minting = false, bool allOps = false, bool stale = false) => new()
    {
        PriceMicroUsd = 2725,
        Height = 995,
        Time = Now.ToUnixTimeSeconds() - 20,
        ReportedStale = stale,
        VolatilityFreeze = freeze,
        MintingRestricted = minting,
        AllOperationsRestricted = allOps,
        ErrActive = err,
        FetchedAt = Now.ToUnixTimeSeconds(),
    };

    private static NetworkHealth Plain() => new() { Available = true, DeploymentActive = true, NodeHeight = 1000, NetworkHeight = 1000 };

    private static OracleEnrichedDigiDollarApi Wrapper(FakeDdNodeApi inner, IOracleFeed feed, bool mainnet = true) => new(inner, feed, () => mainnet);

    // ---------- merging ----------

    [Fact]
    public async Task The_reading_is_merged_into_the_inner_health_on_mainnet()
    {
        var inner = new FakeDdNodeApi { Health = Plain() };
        var feed = new FakeFeed { Current = Reading(err: true, freeze: true, minting: true, allOps: true, stale: true) };

        var h = await Wrapper(inner, feed).GetHealthAsync(default);

        Assert.True(h.Available);
        Assert.Equal(2725, h.OraclePriceMicroUsd);
        Assert.Equal(995, h.OracleHeight);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Now.ToUnixTimeSeconds() - 20), h.OracleUpdatedAt);
        Assert.True(h.OracleReportedStale);
        Assert.True(h.ErrActive);
        Assert.True(h.VolatilityFreeze);
        Assert.True(h.MintingRestricted);
        Assert.True(h.AllOperationsRestricted);
        Assert.False(h.OracleLoading);
        Assert.Equal(1000, h.NodeHeight); // everything else from the explorer is untouched
        Assert.Equal(1, feed.Refreshes);
    }

    [Fact]
    public async Task Without_a_reading_the_inner_health_is_untouched_apart_from_the_loading_flag()
    {
        var inner = new FakeDdNodeApi { Health = Plain() };

        var loading = await Wrapper(inner, new FakeFeed { Current = null, IsLoading = true }).GetHealthAsync(default);
        var settled = await Wrapper(inner, new FakeFeed { Current = null, IsLoading = false }).GetHealthAsync(default);

        Assert.True(loading.OracleLoading);
        Assert.Null(loading.OraclePriceMicroUsd);
        Assert.False(settled.OracleLoading);
        Assert.Null(settled.OraclePriceMicroUsd);
        Assert.Equal(1000, settled.NodeHeight);
    }

    [Fact]
    public async Task An_unavailable_inner_health_stays_unavailable_and_carries_no_oracle_even_when_a_reading_is_held()
    {
        var inner = new FakeDdNodeApi { Down = true };
        var feed = new FakeFeed { Current = Reading() };

        var h = await Wrapper(inner, feed).GetHealthAsync(default);

        Assert.False(h.Available);
        Assert.Null(h.OraclePriceMicroUsd);
        Assert.False(h.OracleLoading);
        Assert.Equal(0, feed.Refreshes); // no fetch is started for a health that is unavailable
    }

    [Fact]
    public async Task On_a_test_network_the_health_is_untouched_and_nothing_is_requested()
    {
        var inner = new FakeDdNodeApi { Health = Plain() };
        var feed = new FakeFeed { Current = Reading(), IsLoading = true };

        var h = await Wrapper(inner, feed, mainnet: false).GetHealthAsync(default);

        Assert.Null(h.OraclePriceMicroUsd);
        Assert.False(h.OracleLoading);
        Assert.Equal(0, feed.Refreshes);
    }

    // ---------- Merge, used by the wrapper and by the screens' view model ----------

    [Fact]
    public void Merge_changes_nothing_on_a_test_network_or_an_unavailable_health()
    {
        var feed = new FakeFeed { Current = Reading(), IsLoading = true };
        var available = Plain();
        var down = new NetworkHealth { Available = false };

        Assert.Same(available, OracleEnrichedDigiDollarApi.Merge(available, feed, isMainnet: false));
        Assert.Same(down, OracleEnrichedDigiDollarApi.Merge(down, feed, isMainnet: true));
    }

    [Fact]
    public void Merge_fills_the_oracle_fields_on_an_available_mainnet_health()
    {
        var merged = OracleEnrichedDigiDollarApi.Merge(Plain(), new FakeFeed { Current = Reading(minting: true) }, isMainnet: true);

        Assert.Equal(2725, merged.OraclePriceMicroUsd);
        Assert.True(merged.MintingRestricted);
    }

    // ---------- no request when the feature is off (SC-007) ----------

    [Theory]
    [InlineData(true, true, "https://api.example.org/api/oracle", true)]
    [InlineData(false, true, "https://api.example.org/api/oracle", false)] // OracleEnabled off
    [InlineData(true, false, "https://api.example.org/api/oracle", false)] // node backend
    [InlineData(true, true, "http://api.example.org/api/oracle", false)]   // not https
    [InlineData(true, true, null, false)]
    public void The_feed_is_used_only_when_the_switch_is_on_the_backend_is_the_explorer_and_the_address_is_usable(bool on, bool explorer, string? url, bool expected) =>
        Assert.Equal(expected, OracleDataServiceClient.IsEnabled(on, explorer, url));

    [Fact]
    public async Task A_feed_without_an_address_makes_no_request_however_often_it_is_asked()
    {
        var stub = new OracleDataServiceClientTests.Stub();
        var feed = new OracleDataServiceClient(new HttpClient(stub), null, new FakeTimeProvider(Now));
        var wrapper = Wrapper(new FakeDdNodeApi { Health = Plain() }, feed);

        for (var i = 0; i < 5; i++) await wrapper.GetHealthAsync(default);
        await Task.Delay(50);

        Assert.Equal(0, stub.Count);
        Assert.False(feed.IsLoading);
    }

    // ---------- every other call passes straight through ----------

    [Fact]
    public async Task Every_other_call_goes_to_the_inner_backend_unchanged()
    {
        var inner = new FakeDdNodeApi { Health = Plain() };
        inner.Statuses["abc"] = new DdTxStatusResult(DdTxState.Confirmed, 3);
        var wrapper = Wrapper(inner, new FakeFeed());

        var utxos = await wrapper.GetUtxosAsync(["DAddress1"], 1, default);
        var history = await wrapper.GetHistoryAsync(["DAddress1"], 10, null, default);
        var status = await wrapper.GetTxStatusAsync("abc", default);
        var txid = await wrapper.BroadcastAsync("deadbeef", default);

        Assert.Contains("DAddress1", utxos.Keys);
        Assert.Empty(history.Items);
        Assert.Equal(DdTxState.Confirmed, status.State);
        Assert.Contains("deadbeef", inner.Broadcasts);
        Assert.False(string.IsNullOrEmpty(txid));
    }

    // ---------- every failure leaves the oracle fields empty (SC-003) ----------

    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(-1)] // network error
    public async Task A_failing_data_service_leaves_the_oracle_unavailable_and_the_health_available(int status)
    {
        var clock = new FakeTimeProvider(Now);
        var stub = new OracleDataServiceClientTests.Stub
        {
            Answer = (_, _) => status < 0
                ? throw new HttpRequestException("no network")
                : Task.FromResult(OracleDataServiceClientTests.Json("{}", (System.Net.HttpStatusCode)status)),
        };
        var feed = new OracleDataServiceClient(new HttpClient(stub), OracleDataServiceClientTests.Url, clock);
        var wrapper = Wrapper(new FakeDdNodeApi { Health = Plain() }, feed);

        await wrapper.GetHealthAsync(default);
        var done = new TaskCompletionSource();
        feed.Changed += () => done.TrySetResult();
        await Task.WhenAny(done.Task, Task.Delay(500));
        var h = await wrapper.GetHealthAsync(default);

        Assert.True(h.Available);
        Assert.Null(h.OraclePriceMicroUsd);
        Assert.False(h.OracleLoading);
        Assert.False(h.OraclePriceAvailable);
    }

    [Fact]
    public async Task An_answer_older_than_five_minutes_is_no_longer_merged_as_current()
    {
        var clock = new FakeTimeProvider(Now);
        var stub = new OracleDataServiceClientTests.Stub { Answer = (_, _) => Task.FromResult(OracleDataServiceClientTests.Json(OracleDataServiceClientTests.Good(clock.GetUtcNow()))) };
        var feed = new OracleDataServiceClient(new HttpClient(stub), OracleDataServiceClientTests.Url, clock);
        var wrapper = Wrapper(new FakeDdNodeApi { Health = Plain() }, feed);

        var done = new TaskCompletionSource();
        feed.Changed += () => done.TrySetResult();
        await wrapper.GetHealthAsync(default);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2725, (await wrapper.GetHealthAsync(default)).OraclePriceMicroUsd);

        stub.Answer = (_, _) => Task.FromResult(OracleDataServiceClientTests.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable));
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var later = await wrapper.GetHealthAsync(default);

        Assert.Null(later.OraclePriceMicroUsd);
        Assert.False(later.OraclePriceAvailable);
    }

    // ---------- a hanging oracle never delays the health ----------

    [Fact]
    public async Task A_hanging_data_service_never_delays_the_health_call()
    {
        var clock = new FakeTimeProvider(Now);
        var stub = new OracleDataServiceClientTests.Stub { Answer = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return OracleDataServiceClientTests.Json("{}"); } };
        var feed = new OracleDataServiceClient(new HttpClient(stub), OracleDataServiceClientTests.Url, clock);
        var wrapper = Wrapper(new FakeDdNodeApi { Health = Plain() }, feed);

        // First load: the health comes back at once, with the oracle still loading.
        var first = await wrapper.GetHealthAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(first.Available);
        Assert.True(first.OracleLoading);
        Assert.Null(first.OraclePriceMicroUsd);

        // After the 5-second limit the oracle gives up: still no delay, and "loading" has turned into "unavailable".
        var done = new TaskCompletionSource();
        feed.Changed += () => done.TrySetResult();
        // the request must be in flight (its 5-second limit started) before time moves
        for (var i = 0; i < 200 && stub.Count == 0; i++) await Task.Delay(10);
        clock.Advance(TimeSpan.FromSeconds(5.5));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var later = await wrapper.GetHealthAsync(default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(later.Available);
        Assert.False(later.OracleLoading);
        Assert.Null(later.OraclePriceMicroUsd);
        Assert.False(later.OraclePriceAvailable);
    }
}
