namespace DigiByte.SpvGateway.Endpoints;

/// <summary>The error codes of the gateway API (contracts/gateway-api.md).</summary>
public enum GatewayError
{
    BadRequest,
    WrongNetwork,
    OverLimit,
    RateLimited,
    RangeUnavailable,
    NotReady,
    NotFound,
    Internal,
}

public static class GatewayErrors
{
    public static string Code(GatewayError error) => error switch
    {
        GatewayError.BadRequest => "bad_request",
        GatewayError.WrongNetwork => "wrong_network",
        GatewayError.OverLimit => "over_limit",
        GatewayError.RateLimited => "rate_limited",
        GatewayError.RangeUnavailable => "range_unavailable",
        GatewayError.NotReady => "not_ready",
        GatewayError.NotFound => "not_found",
        _ => "internal",
    };

    public static int Status(GatewayError error) => error switch
    {
        GatewayError.BadRequest => 400,
        GatewayError.WrongNetwork => 400,
        GatewayError.OverLimit => 400,
        GatewayError.RateLimited => 429,
        GatewayError.RangeUnavailable => 404,
        GatewayError.NotReady => 503,
        GatewayError.NotFound => 404,
        _ => 500,
    };
}

/// <summary>An error the API reports to the caller with a clear reason. Never carries addresses or transaction contents.</summary>
public sealed class GatewayException : Exception
{
    public GatewayException(GatewayError error, string message, int? retryAfterSeconds = null) : base(message)
    {
        Error = error;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public GatewayError Error { get; }

    public int? RetryAfterSeconds { get; }
}
