using DigiByte.NodeApi.Endpoints;
using DigiByte.NodeApi.Middleware;
using DigiByte.NodeApi.RpcClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T005 / S1: the wallet-scoped DigiDollar routes (everything that touches the node's shared
/// wallet) must require X-NodeApi-Key, including the GET routes, and must fail closed when no key
/// is configured. Wallet-facing read routes stay public.
/// </summary>
public class DigiDollarWalletScopedAuthTests
{
    private const string Key = "s3cret-key";

    private static async Task<(int Status, bool NextCalled)> Run(
        string method, string path, string? header, string? configuredKey, string environment = "Production")
    {
        var nextCalled = false;
        var middleware = new WriteKeyAuthMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new DigiDollarConfig { Enabled = true, WriteKey = configuredKey },
            new FakeEnv(environment),
            NullLogger<WriteKeyAuthMiddleware>.Instance);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (header is not null) ctx.Request.Headers["X-NodeApi-Key"] = header;
        ctx.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(ctx);
        return (ctx.Response.StatusCode, nextCalled);
    }

    public static IEnumerable<object[]> WalletScopedRequests() =>
    [
        ["POST", "/api/digidollar/address/new"],
        ["GET", "/api/digidollar/addresses"],
        ["GET", "/api/digidollar/balance"],
        ["GET", "/api/digidollar/balance/DD1abc"],
        ["GET", "/api/digidollar/unspent"],
        ["GET", "/api/digidollar/transactions"],
        ["POST", "/api/digidollar/send"],
        ["POST", "/api/digidollar/send-many"],
        ["POST", "/api/digidollar/mint"],
        ["POST", "/api/digidollar/redeem"],
        ["GET", "/api/digidollar/positions"],
        ["GET", "/api/digidollar/positions/abc/redemption"],
    ];

    [Theory, MemberData(nameof(WalletScopedRequests))]
    public async Task WalletScoped_WithoutKey_Is401(string method, string path)
    {
        var (status, next) = await Run(method, path, header: null, configuredKey: Key);
        Assert.Equal(401, status);
        Assert.False(next);
    }

    [Theory, MemberData(nameof(WalletScopedRequests))]
    public async Task WalletScoped_WithWrongKey_Is401(string method, string path)
    {
        var (status, next) = await Run(method, path, header: "nope", configuredKey: Key);
        Assert.Equal(401, status);
        Assert.False(next);
    }

    [Theory, MemberData(nameof(WalletScopedRequests))]
    public async Task WalletScoped_WithCorrectKey_PassesThrough(string method, string path)
    {
        var (_, next) = await Run(method, path, header: Key, configuredKey: Key);
        Assert.True(next);
    }

    [Theory, MemberData(nameof(WalletScopedRequests))]
    public async Task WalletScoped_NoKeyConfigured_FailsClosedInProduction(string method, string path)
    {
        var (status, next) = await Run(method, path, header: null, configuredKey: null);
        Assert.Equal(503, status);
        Assert.False(next);
    }

    [Fact]
    public async Task WalletScoped_NoKeyConfigured_AllowedInDevelopment()
    {
        var (_, next) = await Run("GET", "/api/digidollar/balance", header: null, configuredKey: null, environment: "Development");
        Assert.True(next);
    }

    public static IEnumerable<object[]> PublicRequests() =>
    [
        ["GET", "/api/digidollar/health"],
        ["GET", "/api/digidollar/oracle/price"],
        ["GET", "/api/digidollar/protection"],
        ["GET", "/api/digidollar/stats"],
        ["GET", "/api/digidollar/deployment"],
        ["POST", "/api/digidollar/address/validate"],
        ["POST", "/api/digidollar/addresses/utxos"],
        ["POST", "/api/digidollar/addresses/history"],
        ["GET", "/api/digidollar/tx/abcdef/status"],
        ["GET", "/api/blockchain/info"],
    ];

    [Theory, MemberData(nameof(PublicRequests))]
    public async Task WalletFacingRoutes_StayPublic_EvenWithKeyConfigured(string method, string path)
    {
        var (_, next) = await Run(method, path, header: null, configuredKey: Key);
        Assert.True(next);
    }

    /// <summary>
    /// Forces a conscious public-versus-wallet-scoped decision for every DigiDollar route.
    /// Adding a route to MapDigiDollarEndpoints without classifying it fails this test.
    /// </summary>
    [Fact]
    public void EveryDigiDollarRoute_IsClassified()
    {
        var builder = WebApplication.CreateBuilder();
        // Same registrations as Program.cs so endpoint parameters resolve from DI instead of being inferred as bodies.
        builder.Services.AddSingleton(new NodeConfig());
        builder.Services.AddSingleton(new DigiDollarConfig());
        builder.Services.AddHttpClient<DigiByteRpcClient>();
        builder.Services.AddSingleton<DigiByteRpcClient>();
        builder.Services.AddSingleton<DigiByte.NodeApi.DigiDollar.IDdIndex>(new FakeDdIndex());
        builder.Services.AddSingleton<DigiByte.NodeApi.DigiDollar.DdIndexStatus>();
        var app = builder.Build();
        app.MapGroup("/api/digidollar").MapDigiDollarEndpoints();

        var dataSource = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>();
        var unclassified = new List<string>();
        foreach (var e in dataSource)
        {
            var template = e.RoutePattern.RawText ?? "";
            if (!template.StartsWith("/api/digidollar", StringComparison.OrdinalIgnoreCase)) continue;
            var rel = template["/api/digidollar".Length..];
            if (!WalletScopedRoutes.IsKnownWalletScopedTemplate(rel) && !WalletScopedRoutes.IsKnownPublicTemplate(rel))
                unclassified.Add(rel);
        }
        Assert.True(unclassified.Count == 0, "Unclassified DigiDollar routes: " + string.Join(", ", unclassified));

        // FR-023: addresses travel only in POST bodies, so no public route may take an address in its URL.
        Assert.DoesNotContain(WalletScopedRoutes.PublicTemplates,
            t => t.Contains("{address", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
