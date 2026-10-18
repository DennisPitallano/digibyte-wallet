using DigiByte.NodeApi.RpcClient;

namespace DigiByte.NodeApi.Middleware;

/// <summary>
/// Guards the wallet-scoped DigiDollar routes (anything that reads or spends the node's shared
/// DigiDollar wallet: send, mint, redeem, address/new, balance, unspent, transactions, positions;
/// see <see cref="WalletScopedRoutes"/>) with a shared secret, for GET as well as POST.
///
/// The secret is set via DigiDollarConfig.WriteKey (env var NODEAPI_WRITE_KEY in prod).
/// Callers must send: X-NodeApi-Key: {secret}
/// Failed auth: 401 Unauthorized, no body detail echoed back.
///
/// Fails closed: when no secret is configured, wallet-scoped routes return 503, except when the
/// host environment is Development (local dev convenience). Wallet-facing read routes under
/// /api/digidollar (health, batch UTXOs/history, tx status) are public and never need the key.
/// </summary>
public class WriteKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DigiDollarConfig _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<WriteKeyAuthMiddleware> _logger;

    public WriteKeyAuthMiddleware(
        RequestDelegate next,
        DigiDollarConfig config,
        IHostEnvironment env,
        ILogger<WriteKeyAuthMiddleware> logger)
    {
        _next = next;
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!WalletScopedRoutes.IsWalletScoped(path))
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
                _logger.LogError("Refused wallet-scoped DigiDollar route {Path}: NODEAPI_WRITE_KEY is not configured", path);
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { error = "write_key_not_configured" });
                return;

            default:
                _logger.LogWarning("Rejected wallet-scoped DigiDollar route {Path}: missing/invalid {Header}", path, OperatorKey.HeaderName);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "unauthorized",
                    message = $"Missing or invalid {OperatorKey.HeaderName} header",
                });
                return;
        }
    }
}
