using DigiByte.NodeApi.Middleware;
using DigiByte.NodeApi.RpcClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T008: outside Development, the node-control, key, wallet, mining and other costly or dangerous routes
/// need the operator key (X-NodeApi-Key) and fail closed when none is configured. The routes the wallet
/// and platform actually use stay open (see research R6a).
/// </summary>
public class OperatorRouteGateTests
{
    private const string Key = "s3cret-key";

    private static async Task<(int Status, bool NextCalled)> Run(
        string method, string path, string? header, string? configuredKey, string environment = "Production")
    {
        var nextCalled = false;
        var middleware = new OperatorRouteGate(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new DigiDollarConfig { WriteKey = configuredKey },
            new FakeEnv(environment),
            NullLogger<OperatorRouteGate>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (header is not null) ctx.Request.Headers["X-NodeApi-Key"] = header;
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);
        return (ctx.Response.StatusCode, nextCalled);
    }

    public static IEnumerable<object[]> GatedRequests() =>
    [
        ["GET", "/api/keys/dump/DAbc"],
        ["POST", "/api/keys/import/privkey"],
        ["POST", "/api/keys/multisig"],
        ["GET", "/api/wallet/info"],
        ["POST", "/api/wallet/send"],
        ["POST", "/api/wallet/sign"],
        ["POST", "/api/wallet/backup"],
        ["GET", "/api/mining/info"],
        ["POST", "/api/mining/generate/10"],
        ["POST", "/api/faucet/send"],
        ["POST", "/api/psbt/create"],
        ["POST", "/api/descriptor/scan"],
        ["POST", "/api/descriptor/derive"],
        ["POST", "/api/util/stop"],
        ["POST", "/api/util/rescan"],
        ["POST", "/api/util/utxo/lock"],
        ["POST", "/api/util/utxo/unlock"],
        ["GET", "/api/util/txoutset"],
        ["POST", "/api/blockchain/block/invalidate"],
        ["POST", "/api/blockchain/block/reconsider"],
        ["POST", "/api/tx/create"],
        ["POST", "/api/tx/fund"],
        ["GET", "/api/network/mempool/raw"],
        ["GET", "/api/network/peers"],
        ["POST", "/api/network/ping"],
        // Case and trailing slash must not be a way around the gate.
        ["POST", "/API/Wallet/Send"],
        ["GET", "/api/network/peers/"],
    ];

    public static IEnumerable<object[]> OpenRequests() =>
    [
        ["POST", "/api/tx/broadcast"],
        ["GET", "/api/tx/abcdef0123"],
        ["GET", "/api/tx/abcdef0123/raw"],
        ["POST", "/api/tx/decode"],
        ["POST", "/api/tx/testmempool"],
        ["GET", "/api/blockchain/height"],
        ["GET", "/api/blockchain/info"],
        ["GET", "/api/blockchain/block/100"],
        ["GET", "/api/address/DAbc/balance"],
        ["GET", "/api/address/DAbc/utxos"],
        ["POST", "/api/address/balances"],
        ["POST", "/api/address/utxos"],
        ["GET", "/api/network/info"],
        ["GET", "/api/network/fee/6"],
        ["GET", "/api/network/mempool"],
        ["GET", "/api/util/version"],
        ["GET", "/api/util/uptime"],
        ["GET", "/api/health"],
        ["POST", "/api/digidollar/addresses/utxos"],
        ["GET", "/api/digidollar/health"],
        // A path that only starts with a gated word is not gated.
        ["GET", "/api/walletish/x"],
    ];

    [Theory, MemberData(nameof(GatedRequests))]
    public async Task Gated_WithoutKey_Is401(string method, string path)
    {
        var (status, next) = await Run(method, path, header: null, configuredKey: Key);
        Assert.Equal(401, status);
        Assert.False(next);
    }

    [Theory, MemberData(nameof(GatedRequests))]
    public async Task Gated_WithWrongKey_Is401(string method, string path)
    {
        var (status, next) = await Run(method, path, header: "nope", configuredKey: Key);
        Assert.Equal(401, status);
        Assert.False(next);
    }

    [Theory, MemberData(nameof(GatedRequests))]
    public async Task Gated_WithCorrectKey_PassesThrough(string method, string path)
    {
        var (_, next) = await Run(method, path, header: Key, configuredKey: Key);
        Assert.True(next);
    }

    [Theory, MemberData(nameof(GatedRequests))]
    public async Task Gated_NoKeyConfigured_FailsClosedWith503(string method, string path)
    {
        var (status, next) = await Run(method, path, header: null, configuredKey: null);
        Assert.Equal(503, status);
        Assert.False(next);
    }

    [Theory, MemberData(nameof(GatedRequests))]
    public async Task Gated_NoKeyConfigured_InDevelopment_PassesThrough(string method, string path)
    {
        var (_, next) = await Run(method, path, header: null, configuredKey: null, environment: "Development");
        Assert.True(next);
    }

    [Theory, MemberData(nameof(OpenRequests))]
    public async Task Open_NeverNeedsTheKey(string method, string path)
    {
        var (_, next) = await Run(method, path, header: null, configuredKey: Key);
        Assert.True(next);
    }

    [Theory, MemberData(nameof(OpenRequests))]
    public async Task Open_StaysOpenWhenNoKeyIsConfigured(string method, string path)
    {
        var (_, next) = await Run(method, path, header: null, configuredKey: null);
        Assert.True(next);
    }

    [Fact]
    public async Task Preflight_IsNeverGated()
    {
        var (_, next) = await Run("OPTIONS", "/api/wallet/send", header: null, configuredKey: Key);
        Assert.True(next);
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
