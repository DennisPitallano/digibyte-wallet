namespace DigiByte.Wallet.Services;

/// <summary>
/// Reads <c>{ApiUrl}/api/health</c>: one GET, a short timeout, no retries, and no exception ever reaches the caller.
/// Use an <see cref="HttpClient"/> without a resilience handler (the node API client's retries would turn a dead node into a slow probe).
/// </summary>
public sealed class NodeHealthHttpSource : INodeHealthSource
{
    private readonly HttpClient _http;
    private readonly NodeSourceOptions _options;

    public NodeHealthHttpSource(HttpClient http, NodeSourceOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<NodeHealthResult> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ApiUrl is not { } baseUrl)
            return NodeHealthResult.Unreachable;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.HealthTimeout);

            using var response = await _http.GetAsync($"{baseUrl}/api/health", timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return NodeReadiness.ParseHealth((int)response.StatusCode, body);
        }
        catch (Exception)
        {
            // Timeout, cancellation, network error, a browser block or anything else: the node counts as unreachable.
            // Details are deliberately not surfaced to the user.
            return NodeHealthResult.Unreachable;
        }
    }
}
