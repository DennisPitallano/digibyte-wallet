namespace DigiByte.SpvGateway.Endpoints;

public sealed record FeeResponse(decimal SatoshisPerVbyte, string Basis);

public static class FeeEndpoints
{
    /// <summary>
    /// DigiByte minimum relay fee: 100,000 satoshis per kilobyte, which is 100 satoshis per virtual byte. A transaction below it is not
    /// relayed, so it is the floor any estimate must respect.
    /// </summary>
    public const decimal MinimumRelaySatoshisPerVbyte = 100m;

    /// <summary>
    /// GET /v1/fee: advisory only (spec FR-010). The gateway cannot work out what recent transactions paid, because a block holds the
    /// outputs a transaction creates but not the amounts of the outputs it spends, and the gateway does not keep a transaction index
    /// to look them up. So it reports the network minimum and says so in <c>basis</c>; the wallet keeps its existing fee source as the
    /// authority and never raises a fee from this.
    /// </summary>
    public static void MapFee(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/fee", () => Results.Ok(new FeeResponse(MinimumRelaySatoshisPerVbyte, "network-minimum")));
    }
}
