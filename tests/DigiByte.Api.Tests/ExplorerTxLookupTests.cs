using System.Net;
using System.Text;
using DigiByte.Api.Services;

namespace DigiByte.Api.Tests;

/// <summary>The two explorer lookups read the answers real explorers give (shapes taken from digiexplorer.info and Blockbook).</summary>
public class ExplorerTxLookupTests
{
    private static readonly string Tx = new('a', 64);
    private const string Project = "dgb1qvc2d6umstxzrs5xypmvw5wt33mchec4z7ejqaa";

    private static HttpClient Client(params (string PathContains, HttpStatusCode Status, string Body)[] routes) =>
        new(new Routed(routes)) { BaseAddress = null };

    // ── Esplora ──

    [Fact]
    public async Task Esplora_ConfirmedTx_ComputesConfirmationsFromTheTipAndReadsOutputs()
    {
        var http = Client(
            ($"/tx/{Tx}", HttpStatusCode.OK, $$"""{"txid":"{{Tx}}","status":{"confirmed":true,"block_height":1000},"vout":[{"scriptpubkey_address":"{{Project}}","value":8550000000},{"scriptpubkey_address":"dgb1qother","value":42}]}"""),
            ("/blocks/tip/height", HttpStatusCode.OK, "1004\n"));
        var tx = await new EsploraTxLookup(http, "https://esplora.test/api").LookupAsync(Tx, default);
        Assert.NotNull(tx);
        Assert.Equal(5, tx!.Confirmations); // 1004 - 1000 + 1
        Assert.Equal(8550000000L, tx.Outputs.Single(o => o.Address == Project).Satoshis);
    }

    [Fact]
    public async Task Esplora_UnconfirmedTx_HasZeroConfirmations_WithoutAskingForTheTip()
    {
        var http = Client(($"/tx/{Tx}", HttpStatusCode.OK, $$"""{"txid":"{{Tx}}","status":{"confirmed":false},"vout":[{"scriptpubkey_address":"{{Project}}","value":100}]}"""));
        var tx = await new EsploraTxLookup(http, "https://esplora.test/api").LookupAsync(Tx, default);
        Assert.Equal(0, tx!.Confirmations);
    }

    [Fact]
    public async Task Esplora_OutputWithoutAnAddress_IsKeptAsEmptyAddress()
    {
        var http = Client(($"/tx/{Tx}", HttpStatusCode.OK, $$"""{"txid":"{{Tx}}","status":{"confirmed":false},"vout":[{"scriptpubkey_type":"op_return","value":0}]}"""));
        var tx = await new EsploraTxLookup(http, "https://esplora.test/api").LookupAsync(Tx, default);
        Assert.Equal("", Assert.Single(tx!.Outputs).Address);
    }

    [Fact]
    public async Task Esplora_404_IsADefiniteNotFound()
    {
        var http = Client(($"/tx/{Tx}", HttpStatusCode.NotFound, "Transaction not found"));
        Assert.Null(await new EsploraTxLookup(http, "https://esplora.test/api").LookupAsync(Tx, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Esplora_OtherErrors_ThrowSoTheCheckNeverPassesOrSkips(HttpStatusCode status)
    {
        var http = Client(($"/tx/{Tx}", status, "oops"));
        await Assert.ThrowsAsync<HttpRequestException>(() => new EsploraTxLookup(http, "https://esplora.test/api").LookupAsync(Tx, default));
    }

    [Fact]
    public async Task Esplora_UnreadableAnswer_Throws()
    {
        var http = Client(($"/tx/{Tx}", HttpStatusCode.OK, "<html>captive portal</html>"));
        await Assert.ThrowsAnyAsync<Exception>(() => new EsploraTxLookup(http, "https://esplora.test/api").LookupAsync(Tx, default));
    }

    // ── Blockbook ──

    [Fact]
    public async Task Blockbook_ReadsConfirmationsAndOutputs()
    {
        var http = Client(($"/api/v2/tx/{Tx}", HttpStatusCode.OK,
            $$"""{"txid":"{{Tx}}","confirmations":12,"vout":[{"value":"8550000000","n":0,"addresses":["{{Project}}"]},{"value":"42","n":1,"addresses":["dgb1qother"]},{"value":"0","n":2,"hex":"6a00"}]}"""));
        var tx = await new BlockbookTxLookup(http, "https://blockbook.test").LookupAsync(Tx, default);
        Assert.Equal(12, tx!.Confirmations);
        Assert.Equal(8550000000L, tx.Outputs.Single(o => o.Address == Project).Satoshis);
        Assert.Equal(3, tx.Outputs.Count);
    }

    [Fact]
    public async Task Blockbook_ErrorBodyOrNotFound_IsADefiniteNotFound()
    {
        var withError = Client(($"/api/v2/tx/{Tx}", HttpStatusCode.OK, """{"error":"Transaction 'x' not found"}"""));
        Assert.Null(await new BlockbookTxLookup(withError, "https://blockbook.test").LookupAsync(Tx, default));
        var notFound = Client(($"/api/v2/tx/{Tx}", HttpStatusCode.NotFound, "{}"));
        Assert.Null(await new BlockbookTxLookup(notFound, "https://blockbook.test").LookupAsync(Tx, default));
    }

    [Fact]
    public async Task Blockbook_502_Throws()
    {
        var http = Client(($"/api/v2/tx/{Tx}", HttpStatusCode.BadGateway, "bad gateway"));
        await Assert.ThrowsAsync<HttpRequestException>(() => new BlockbookTxLookup(http, "https://blockbook.test").LookupAsync(Tx, default));
    }

    private sealed class Routed((string PathContains, HttpStatusCode Status, string Body)[] routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            foreach (var r in routes)
                if (url.Contains(r.PathContains))
                    return Task.FromResult(new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
    }
}
