using DigiByte.P2P.Shared.Models;

namespace DigiByte.Api.Endpoints;

public static class P2PEndpoints
{
    public static RouteGroupBuilder MapP2PEndpoints(this RouteGroupBuilder group)
    {
        // Orders
        group.MapGet("/orders", GetOrders).WithName("GetOrders");
        group.MapGet("/orders/{id:guid}", GetOrder).WithName("GetOrder");
        group.MapPost("/orders", CreateOrder).WithName("CreateOrder");

        // Trades
        group.MapGet("/trades", GetTrades).WithName("GetTrades");
        group.MapGet("/trades/{id:guid}", GetTrade).WithName("GetTrade");
        group.MapPost("/trades", InitiateTrade).WithName("InitiateTrade");

        // Reputation
        group.MapGet("/users/{userId:guid}/reputation", GetReputation).WithName("GetReputation");

        return group;
    }

    // Placeholder implementations — will be backed by real services + EF Core
    private static IResult GetOrders(string? fiatCurrency, string? type, int skip = 0, int take = 20)
    {
        return Results.Ok(Array.Empty<P2POrder>());
    }

    private static IResult GetOrder(Guid id)
    {
        return Results.NotFound();
    }

    private static IResult CreateOrder(P2POrder order)
    {
        order.Id = Guid.NewGuid();
        order.CreatedAt = DateTime.UtcNow;
        return Results.Created($"/api/p2p/orders/{order.Id}", order);
    }

    private static IResult GetTrades(Guid? userId, int skip = 0, int take = 20)
    {
        return Results.Ok(Array.Empty<Trade>());
    }

    private static IResult GetTrade(Guid id)
    {
        return Results.NotFound();
    }

    private static IResult InitiateTrade(Trade trade)
    {
        trade.Id = Guid.NewGuid();
        trade.CreatedAt = DateTime.UtcNow;
        return Results.Created($"/api/p2p/trades/{trade.Id}", trade);
    }

    private static IResult GetReputation(Guid userId)
    {
        return Results.Ok(new UserReputation
        {
            UserId = userId,
            Username = "unknown",
            MemberSince = DateTime.UtcNow
        });
    }
}
