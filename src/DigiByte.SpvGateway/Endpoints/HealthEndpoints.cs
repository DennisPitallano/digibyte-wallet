using DigiByte.SpvGateway.Health;
using DigiByte.SpvGateway.Sync;

namespace DigiByte.SpvGateway.Endpoints;

public static class HealthEndpoints
{
    /// <summary>GET /v1/health: answers 200 for every state it can report on, like the node health endpoint in feature 005.</summary>
    public static void MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/health", (SpvGatewayOptions options, HeaderStore store, IPeerStatusSource peers) =>
            Results.Json(HealthReporter.Build(options, store, peers.Snapshot()), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    }
}
