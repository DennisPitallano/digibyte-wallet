using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Middleware;

/// <summary>
/// Requires the operator key (<c>X-NodeApi-Key</c>, see <see cref="OperatorKey"/>) on the routes that control the
/// node, touch keys or a node wallet, mine, or are costly or leak peer details. The public NodeApi serves
/// the browser wallet, so everything it does not need is closed by default outside Development. The list
/// comes from the consumer audit in specs/004-railway-digibyte-node/research.md (R6a).
///
/// Fails closed: with no key configured these routes answer 503 outside Development. Matching ignores
/// case and a trailing slash and works on whole path segments, so <c>/api/walletish</c> is not gated.
/// CORS preflight (OPTIONS) is left alone.
/// </summary>
public class OperatorRouteGate
{
    /// <summary>Path prefixes (whole segments) that need the operator key.</summary>
    public static readonly string[] GatedPrefixes =
    [
        "/api/keys",
        "/api/wallet",
        "/api/mining",
        "/api/faucet",
        "/api/psbt",
        "/api/descriptor",
        "/api/util/stop",
        "/api/util/rescan",
        "/api/util/utxo",
        "/api/util/txoutset",
        "/api/blockchain/block/invalidate",
        "/api/blockchain/block/reconsider",
        "/api/tx/create",
        "/api/tx/fund",
        "/api/network/mempool/raw",
        "/api/network/peers",
        "/api/network/ping",
    ];

    private readonly RequestDelegate _next;
    private readonly DigiDollarConfig _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<OperatorRouteGate> _logger;

    public OperatorRouteGate(
        RequestDelegate next,
        DigiDollarConfig config,
        IHostEnvironment env,
        ILogger<OperatorRouteGate> logger)
    {
        _next = next;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public static bool IsGated(string path)
    {
        var normalized = path.TrimEnd('/');
        foreach (var prefix in GatedPrefixes)
        {
            if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (HttpMethods.IsOptions(context.Request.Method) || !IsGated(path))
        {
            await _next(context);
            return;
        }

        var verdict = OperatorKey.Check(
            _config.WriteKey, context.Request.Headers[OperatorKey.HeaderName].ToString(), _env.IsDevelopment());

        switch (verdict)
        {
            case OperatorKey.Result.Allowed:
                await _next(context);
                return;

            case OperatorKey.Result.NotConfigured:
                _logger.LogError("Refused operator-only route {Path}: NODEAPI_WRITE_KEY is not configured", path);
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { error = "operator_key_not_configured" });
                return;

            default:
                _logger.LogWarning("Rejected operator-only route {Path}: missing/invalid {Header}", path, OperatorKey.HeaderName);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                return;
        }
    }
}
