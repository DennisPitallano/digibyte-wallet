using System.Net.Http.Json;
using System.Text.Json;
using DigiByte.Crypto.Addresses;
using DigiByte.NodeApi.DigiDollar;
using DigiByte.NodeApi.Endpoints;
using DigiByte.NodeApi.RpcClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static DigiByte.NodeApi.Tests.DdTestTx;

namespace DigiByte.NodeApi.Tests;

/// <summary>A configurable <see cref="IDdIndex"/> that records what the handlers asked for.</summary>
internal sealed class FakeDdIndex : IDdIndex
{
    public List<DdUtxo> Utxos { get; } = [];
    public DdHistoryPage History { get; set; } = new([], null);
    public ChainTxStatus Status { get; set; } = new(ChainTxState.Unknown, 0);
    public int? LastMinConf { get; private set; }
    public int? LastCount { get; private set; }
    public string? LastBefore { get; private set; }
    public string? LastTxid { get; private set; }

    public Task<IReadOnlyList<DdUtxo>> GetUtxosAsync(IReadOnlyCollection<byte[]> keys, int minConf, CancellationToken ct)
    {
        LastMinConf = minConf;
        var hex = keys.Select(Convert.ToHexString).ToHashSet();
        return Task.FromResult<IReadOnlyList<DdUtxo>>(Utxos.Where(u => hex.Contains(Convert.ToHexString(u.OutputKey))).ToList());
    }

    public Task<DdHistoryPage> GetHistoryAsync(IReadOnlyCollection<byte[]> keys, int count, string? before, CancellationToken ct)
    {
        LastCount = count;
        LastBefore = before;
        return Task.FromResult(History);
    }

    public Task<ChainTxStatus> GetTxStatusAsync(string txid, CancellationToken ct)
    {
        LastTxid = txid;
        return Task.FromResult(Status);
    }
}

/// <summary>T014: wallet-facing, address-scoped read endpoints (contracts/nodeapi-digidollar-read.md).</summary>
public class DigiDollarReadEndpointsTests
{
    private readonly FakeDdIndex _index = new();
    private readonly DdIndexStatus _status = new();
    private readonly NodeConfig _node = new() { IsTestnet = true };
    private DigiDollarConfig _config = new() { Enabled = true };

    public DigiDollarReadEndpointsTests() => _status.Update(tip: 1000, networkHeight: 1000, lastIndexed: 1000);

    private static string Addr(int seed, string network = "testnet") => DigiDollarAddressValidator.Encode(Key(seed), network);

    private static async Task<(int Status, JsonElement Body)> Exec(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, doc.RootElement.Clone());
    }

    private Task<IResult> Utxos(DdAddressesRequest? req) =>
        DigiDollarReadHandlers.Utxos(req, _config, _node, _index, _status, default);

    private Task<IResult> History(DdHistoryRequest? req) =>
        DigiDollarReadHandlers.History(req, _config, _node, _index, _status, default);

    // ---------- disabled ----------

    [Fact]
    public async Task EveryEndpoint_Returns503Disabled_WhenDigiDollarIsOff()
    {
        _config = new DigiDollarConfig { Enabled = false };

        var (s1, b1) = await Exec(await Utxos(new DdAddressesRequest([Addr(1)], 0)));
        var (s2, b2) = await Exec(await History(new DdHistoryRequest([Addr(1)], null, null)));
        var (s3, b3) = await Exec(await DigiDollarReadHandlers.TxStatus(Txid(1), _config, _index, default));

        Assert.All(new[] { s1, s2, s3 }, s => Assert.Equal(503, s));
        Assert.All(new[] { b1, b2, b3 }, b => Assert.Equal("digidollar_disabled", b.GetProperty("error").GetString()));
    }

    // ---------- /addresses/utxos ----------

    [Fact]
    public async Task Utxos_EmptyAddressList_Is400()
    {
        foreach (var addresses in new string[]?[] { null, [] })
        {
            var (status, body) = await Exec(await Utxos(new DdAddressesRequest(addresses, 0)));
            Assert.Equal(400, status);
            Assert.Equal("invalid_request", body.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Utxos_NullBody_Is400()
    {
        var (status, _) = await Exec(await Utxos(null));
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task Utxos_MoreThan200Addresses_Is413()
    {
        var addresses = Enumerable.Range(0, 201).Select(i => Addr(1 + i % 50)).ToArray();
        var (status, body) = await Exec(await Utxos(new DdAddressesRequest(addresses, 0)));
        Assert.Equal(413, status);
        Assert.Equal("too_many_addresses", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Utxos_Exactly200Addresses_IsAccepted()
    {
        var addresses = Enumerable.Range(1, 200).Select(i => Addr(i)).ToArray();
        var (status, _) = await Exec(await Utxos(new DdAddressesRequest(addresses, 0)));
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task Utxos_InvalidAddresses_Is400_ListingTheBadOnes()
    {
        var good = Addr(1);
        var (status, body) = await Exec(await Utxos(new DdAddressesRequest(
            [good, "not-an-address", Addr(2, "mainnet"), "dgbt1qw508d6qejxtdg4y5r3zarvary0c5xw7kxxxxxx"], 0)));

        Assert.Equal(400, status);
        Assert.Equal("invalid_address", body.GetProperty("error").GetString());
        var bad = body.GetProperty("detail").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(3, bad.Count);
        Assert.DoesNotContain(good, bad);
        Assert.Contains("not-an-address", bad);
    }

    [Fact]
    public async Task Utxos_WhenTheIndexerIsBehind_Is503()
    {
        _status.Update(tip: 1000, networkHeight: 1000, lastIndexed: 993); // lag 7
        var (status, body) = await Exec(await Utxos(new DdAddressesRequest([Addr(1)], 0)));
        Assert.Equal(503, status);
        Assert.Equal("indexer_behind", body.GetProperty("error").GetString());

        _status.Update(tip: 1000, networkHeight: 1000, lastIndexed: 994); // lag 6: still served
        Assert.Equal(200, (await Exec(await Utxos(new DdAddressesRequest([Addr(1)], 0)))).Status);
    }

    [Fact]
    public async Task Utxos_BeforeTheFirstIndexPass_Is503()
    {
        var fresh = new DdIndexStatus();
        var (status, _) = await Exec(await DigiDollarReadHandlers.Utxos(
            new DdAddressesRequest([Addr(1)], 0), _config, _node, _index, fresh, default));
        Assert.Equal(503, status);
    }

    [Fact]
    public async Task Utxos_ReturnsEveryRequestedAddress_WithTheContractShape()
    {
        _index.Utxos.Add(new DdUtxo(Txid(7), 1, Key(1), 2500, "5120" + Hex(Key(1)), 0, 3, SpentByMempool: false));
        var a1 = Addr(1);
        var a2 = Addr(2);

        var (status, body) = await Exec(await Utxos(new DdAddressesRequest([a1, a2, a1], 1)));

        Assert.Equal(200, status);
        Assert.Equal(1, _index.LastMinConf);
        Assert.Equal(2, body.EnumerateObject().Count()); // de-duplicated; both addresses present
        Assert.Empty(body.GetProperty(a2).EnumerateArray());
        var u = body.GetProperty(a1).EnumerateArray().Single();
        Assert.Equal(Txid(7), u.GetProperty("txid").GetString());
        Assert.Equal(1, u.GetProperty("vout").GetInt32());
        Assert.Equal(2500, u.GetProperty("amountCents").GetInt64());
        Assert.Equal("5120" + Hex(Key(1)), u.GetProperty("scriptPubKey").GetString());
        Assert.Equal(0, u.GetProperty("valueSats").GetInt64());
        Assert.Equal(3, u.GetProperty("confirmations").GetInt32());
        Assert.False(u.GetProperty("spentByMempool").GetBoolean());
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-5, 0)]
    [InlineData(6, 6)]
    public async Task Utxos_MinConf_IsClamped(int? requested, int expected)
    {
        await Exec(await Utxos(new DdAddressesRequest([Addr(1)], requested)));
        Assert.Equal(expected, _index.LastMinConf);
    }

    // ---------- /addresses/history ----------

    [Fact]
    public async Task History_ReturnsItemsWithAddresses_BlockTimeAndCursor()
    {
        _index.History = new DdHistoryPage(
        [
            new DdHistoryItem(Txid(5), 900, 1_700_000_000, 101, 10_000_000,
                [new DdHistoryAmount(Key(1), 2500)],
                [new DdHistoryAmount(Key(2), 1000), new DdHistoryAmount(Key(1), 1500)]),
            new DdHistoryItem(Txid(6), null, null, 0, null, [], [new DdHistoryAmount(Key(1), 99)]),
        ], Next: Txid(6));

        var (status, body) = await Exec(await History(new DdHistoryRequest([Addr(1)], 10, null)));

        Assert.Equal(200, status);
        Assert.Equal(Txid(6), body.GetProperty("next").GetString());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var confirmed = items[0];
        Assert.Equal(900, confirmed.GetProperty("blockHeight").GetInt32());
        Assert.StartsWith("2023-11-14T22:13:20", confirmed.GetProperty("blockTime").GetString());
        Assert.Equal(10_000_000, confirmed.GetProperty("feeSats").GetInt64());
        Assert.Equal(Addr(1), confirmed.GetProperty("ddInputs")[0].GetProperty("address").GetString());
        Assert.Equal(2500, confirmed.GetProperty("ddInputs")[0].GetProperty("amountCents").GetInt64());
        Assert.Equal(Addr(2), confirmed.GetProperty("ddOutputs")[0].GetProperty("address").GetString());

        var pending = items[1];
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("blockHeight").ValueKind);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("blockTime").ValueKind);
        Assert.Equal(0, pending.GetProperty("confirmations").GetInt32());
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(25, 25)]
    [InlineData(500, 200)]
    public async Task History_Count_IsClamped_AndDefaultsTo50(int? requested, int expected)
    {
        await Exec(await History(new DdHistoryRequest([Addr(1)], requested, null)));
        Assert.Equal(expected, _index.LastCount);
    }

    [Fact]
    public async Task History_PassesTheCursorThrough_AndRejectsABadOne()
    {
        await Exec(await History(new DdHistoryRequest([Addr(1)], 10, Txid(9))));
        Assert.Equal(Txid(9), _index.LastBefore);

        var (status, body) = await Exec(await History(new DdHistoryRequest([Addr(1)], 10, "not-a-txid")));
        Assert.Equal(400, status);
        Assert.Equal("invalid_cursor", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task History_InvalidAddressAndLimits_MirrorTheUtxoEndpoint()
    {
        Assert.Equal(400, (await Exec(await History(new DdHistoryRequest(["bad"], 10, null)))).Status);
        Assert.Equal(413, (await Exec(await History(new DdHistoryRequest(
            Enumerable.Range(0, 201).Select(_ => Addr(1)).ToArray(), 10, null)))).Status);
        _status.Update(1000, 1000, 900);
        Assert.Equal(503, (await Exec(await History(new DdHistoryRequest([Addr(1)], 10, null)))).Status);
    }

    // ---------- /tx/{txid}/status ----------

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef00")]
    public async Task TxStatus_InvalidTxid_Is400(string txid)
    {
        var (status, body) = await Exec(await DigiDollarReadHandlers.TxStatus(txid, _config, _index, default));
        Assert.Equal(400, status);
        Assert.Equal("invalid_txid", body.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(ChainTxState.Mempool, "mempool", 0)]
    [InlineData(ChainTxState.Confirmed, "confirmed", 4)]
    [InlineData(ChainTxState.Unknown, "unknown", 0)]
    public async Task TxStatus_MapsTheStates(ChainTxState state, string expected, int confirmations)
    {
        _index.Status = new ChainTxStatus(state, confirmations);

        var upper = Txid(255).ToUpperInvariant();
        var (status, body) = await Exec(await DigiDollarReadHandlers.TxStatus(upper, _config, _index, default));

        Assert.Equal(200, status);
        Assert.Equal(expected, body.GetProperty("status").GetString());
        Assert.Equal(confirmations, body.GetProperty("confirmations").GetInt32());
        Assert.Equal(Txid(255), _index.LastTxid); // normalised to lower case
    }

    // ---------- health mapping ----------

    [Fact]
    public void Oracle_NoPrice_IsNull()
    {
        Assert.Null(DdHealthMapper.MapOracle(0m, 30, 1000));
        Assert.Null(DdHealthMapper.MapOracle(-1m, 30, 1000));
    }

    [Fact]
    public void Oracle_PriceAndHeight_AreMapped()
    {
        var o = DdHealthMapper.MapOracle(0.00631m, ageSeconds: 45, nodeHeight: 1000)!;

        Assert.Equal(6310, o.PriceMicroUsd);
        Assert.Equal(997, o.Height); // 45 s = 3 blocks old
        Assert.Equal(1000, DdHealthMapper.MapOracle(0.01m, ageSeconds: -1, nodeHeight: 1000)!.Height);
        Assert.Equal(0, DdHealthMapper.MapOracle(0.01m, ageSeconds: 999_999, nodeHeight: 10)!.Height); // never negative
    }

    [Theory]
    [InlineData("""{"err_active":true,"volatility_freeze":false}""", true, false)]
    [InlineData("""{"errActive":false,"volatilityFreeze":true}""", false, true)]
    [InlineData("""{"err":"true","freeze_active":"true"}""", true, true)]
    [InlineData("""{}""", false, false)]
    public void Protection_AcceptsTheLikelyFieldSpellings(string json, bool err, bool freeze)
    {
        using var doc = JsonDocument.Parse(json);
        var p = DdHealthMapper.MapProtection(doc.RootElement.Clone(), 187m);

        Assert.Equal(err, p.ErrActive);
        Assert.Equal(freeze, p.VolatilityFreeze);
        Assert.Equal(187m, p.SystemCollateralPercent);
    }

    [Fact]
    public void Protection_MissingStatus_DefaultsToFalse_AndNoCollateralWhenZero()
    {
        var p = DdHealthMapper.MapProtection(null, 0m);

        Assert.False(p.ErrActive);
        Assert.False(p.VolatilityFreeze);
        Assert.Null(p.SystemCollateralPercent);
    }

    // ---------- privacy: addresses never reach a log (FR-023) ----------

    [Fact]
    public async Task NoLogLine_ContainsAnAddress_WhenTheBatchEndpointsAreCalled()
    {
        var logs = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton(_config);
        builder.Services.AddSingleton(_node);
        builder.Services.AddSingleton<IDdIndex>(_index);
        builder.Services.AddSingleton(_status);
        var app = builder.Build();
        app.MapGroup("/api/digidollar").MapPost("/addresses/utxos", DigiDollarReadHandlers.Utxos);
        app.MapGroup("/api/digidollar").MapPost("/addresses/history", DigiDollarReadHandlers.History);
        await app.StartAsync();
        try
        {
            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var addresses = new[] { Addr(1), Addr(2), Addr(3) };

            Assert.Equal(200, (int)(await http.PostAsJsonAsync("/api/digidollar/addresses/utxos", new { addresses, minConf = 0 })).StatusCode);
            Assert.Equal(200, (int)(await http.PostAsJsonAsync("/api/digidollar/addresses/history", new { addresses, count = 10 })).StatusCode);

            var all = string.Join("\n", logs.Messages);
            Assert.NotEmpty(logs.Messages); // the framework did log the requests
            foreach (var a in addresses) Assert.DoesNotContain(a, all);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages { get { lock (_messages) { return _messages.ToList(); } } }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);
        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var text = formatter(state, exception);
                if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
                    text += " " + string.Join(" ", kvs.Select(kv => $"{kv.Key}={kv.Value}"));
                lock (owner._messages) { owner._messages.Add(text); }
            }
        }
    }
}
