using System.Text.Json;
using DigiByte.NodeApi.RpcClient;
using DigiByte.NodeApi.Services;

namespace DigiByte.NodeApi.Tests;

/// <summary>
/// T012 / T027: the health model in specs/004-railway-digibyte-node/contracts/nodeapi-health.md.
/// </summary>
public class NodeHealthTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static NodeRpcCall Node(
        string chain = """{"blocks":100,"headers":100,"initialblockdownload":false}""",
        string network = """{"subversion":"/DigiByte:9.26.7/","connections":8}""",
        string index = """{"txindex":{"synced":true,"best_block_height":100}}""",
        Exception? chainError = null,
        Exception? indexError = null) =>
        method => method switch
        {
            "getblockchaininfo" => chainError is null ? Task.FromResult(Json(chain)) : Task.FromException<JsonElement>(chainError),
            "getnetworkinfo" => Task.FromResult(Json(network)),
            "getindexinfo" => indexError is null ? Task.FromResult(Json(index)) : Task.FromException<JsonElement>(indexError),
            _ => throw new InvalidOperationException($"unexpected call {method}"),
        };

    [Fact]
    public async Task CurrentNode_IsOk_With200()
    {
        var health = await new NodeHealthService(Node(), null).GetAsync();

        Assert.Equal("ok", health.Status);
        Assert.Equal("9.26.7", health.NodeVersion);
        Assert.Equal(100, health.Blocks);
        Assert.Equal(100, health.Headers);
        Assert.Equal(0, health.Lag);
        Assert.Equal(8, health.Peers);
        Assert.True(health.Txindex);
        Assert.Equal(200, health.HttpStatus);
    }

    [Fact]
    public async Task Lag_IsHeadersMinusBlocks_AndIsSyncing()
    {
        var chain = """{"blocks":1000,"headers":1500,"initialblockdownload":false}""";

        var health = await new NodeHealthService(Node(chain: chain), null).GetAsync();

        Assert.Equal(500, health.Lag);
        Assert.Equal("syncing", health.Status);
        Assert.Equal(200, health.HttpStatus);
    }

    [Fact]
    public async Task InitialBlockDownload_IsSyncing_EvenWithNoLag()
    {
        var chain = """{"blocks":100,"headers":100,"initialblockdownload":true}""";

        var health = await new NodeHealthService(Node(chain: chain), null).GetAsync();

        Assert.Equal("syncing", health.Status);
    }

    [Fact]
    public async Task SmallLag_WithinThreeBlocks_IsStillOk()
    {
        var chain = """{"blocks":97,"headers":100,"initialblockdownload":false}""";

        var health = await new NodeHealthService(Node(chain: chain), null).GetAsync();

        Assert.Equal("ok", health.Status);
        Assert.Equal(3, health.Lag);
    }

    [Fact]
    public async Task TxindexStillBuilding_IsSyncing()
    {
        var index = """{"txindex":{"synced":false,"best_block_height":10}}""";

        var health = await new NodeHealthService(Node(index: index), null).GetAsync();

        Assert.Equal("syncing", health.Status);
        Assert.False(health.Txindex);
    }

    [Fact]
    public async Task NoTxindexInfo_IsReportedAsNotReady()
    {
        var health = await new NodeHealthService(Node(index: "{}"), null).GetAsync();

        Assert.False(health.Txindex);
        Assert.Equal("syncing", health.Status);
    }

    [Fact]
    public async Task LoadingBlockIndex_IsStarting_With200_NotUnhealthy()
    {
        var error = new RpcException(-28, "Loading block index...");

        var health = await new NodeHealthService(Node(chainError: error), null).GetAsync();

        Assert.Equal("starting", health.Status);
        Assert.Equal(200, health.HttpStatus);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Unreachable_OrRejectedCredentials_IsUnhealthy_With503(int code)
    {
        var error = new RpcException(code, "Cannot connect to DigiByte node at http://secret-host:14022/");

        var health = await new NodeHealthService(Node(chainError: error), null).GetAsync();

        Assert.Equal("unhealthy", health.Status);
        Assert.Equal(503, health.HttpStatus);
    }

    [Fact]
    public async Task Response_NeverContainsCredentialsHostsOrPaths()
    {
        var error = new RpcException(-1, "Cannot connect to DigiByte node at http://user:hunter2@secret-host:14022/ C:\\data");

        var health = await new NodeHealthService(Node(chainError: error), null).GetAsync();
        var json = JsonSerializer.Serialize(health);

        Assert.DoesNotContain("hunter2", json);
        Assert.DoesNotContain("secret-host", json);
        Assert.DoesNotContain("C:\\", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("9.26.7", "9.26.7", true)]
    [InlineData("9.26.8", "9.26.7", true)]
    [InlineData("9.26.6", "9.26.7", false)]
    [InlineData("8.26.2", "9.26.7", false)]
    public async Task VersionOk_ComparesRunningVersionToRecommended(string running, string recommended, bool expected)
    {
        var network = $$"""{"subversion":"/DigiByte:{{running}}/","connections":8}""";

        var health = await new NodeHealthService(Node(network: network), recommended).GetAsync();

        Assert.Equal(recommended, health.RecommendedVersion);
        Assert.Equal(expected, health.VersionOk);
    }

    [Fact]
    public async Task NoRecommendedVersion_MeansVersionOk_AndNoRecommendedVersionField()
    {
        var health = await new NodeHealthService(Node(), null).GetAsync();

        Assert.True(health.VersionOk);
        Assert.Null(health.RecommendedVersion);
    }

    [Fact]
    public async Task UnparsableVersion_WithRecommendedVersion_IsNotOk()
    {
        var network = """{"subversion":"/Weird/","connections":8}""";

        var health = await new NodeHealthService(Node(network: network), "9.26.7").GetAsync();

        Assert.False(health.VersionOk);
    }
}
