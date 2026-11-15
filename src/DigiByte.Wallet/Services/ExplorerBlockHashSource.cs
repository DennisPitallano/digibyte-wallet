using System.Text.Json;

namespace DigiByte.Wallet.Services;

/// <summary>An independent view of the chain: the hash of the block at a height. Null means "no answer", never "different".</summary>
public interface IExplorerBlockHashSource
{
    Task<string?> GetBlockHashAsync(int height, CancellationToken cancellationToken = default);
}

/// <summary>One public explorer and how to ask it for the block hash at a height.</summary>
/// <param name="BaseUrl">The explorer API base, without a trailing slash.</param>
/// <param name="Kind">How the explorer answers.</param>
public sealed record ExplorerHashEndpoint(string BaseUrl, ExplorerHashKind Kind)
{
    public string UrlFor(int height) => Kind == ExplorerHashKind.Esplora
        ? $"{BaseUrl.TrimEnd('/')}/block-height/{height}"
        : $"{BaseUrl.TrimEnd('/')}/api/v2/block-index/{height}";
}

public enum ExplorerHashKind
{
    /// <summary>Esplora: <c>GET /block-height/{h}</c> answers the hash as plain text.</summary>
    Esplora,

    /// <summary>Blockbook: <c>GET /api/v2/block-index/{h}</c> answers <c>{"blockHash":"..."}</c>.</summary>
    Blockbook,
}

/// <summary>
/// Asks public explorers for a block hash (spec FR-013, T067) so the wallet can compare it with its own headers. The first explorer that
/// gives a well-formed answer wins; a failure, a timeout, a missing block or an answer that is not a hash is simply "no answer". Never
/// throws, and the whole call has a hard time budget so it can never stretch a read.
/// </summary>
public sealed class ExplorerBlockHashSource(HttpClient http, IReadOnlyList<ExplorerHashEndpoint> endpoints, TimeSpan? budget = null) : IExplorerBlockHashSource
{
    private readonly TimeSpan _budget = budget ?? TimeSpan.FromSeconds(5);

    public async Task<string?> GetBlockHashAsync(int height, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_budget);
            foreach (var endpoint in endpoints)
            {
                try
                {
                    using var response = await http.GetAsync(endpoint.UrlFor(height), timeout.Token);
                    if (!response.IsSuccessStatusCode)
                        continue;
                    var hash = Parse(endpoint.Kind, await response.Content.ReadAsStringAsync(timeout.Token));
                    if (hash is not null)
                        return hash;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception)
                {
                    // this explorer failed; try the next one
                }
            }
        }
        catch (Exception)
        {
            // never throw into a read
        }
        return null;
    }

    /// <summary>The lowercase block hash in an explorer answer, or null when the answer is not one.</summary>
    public static string? Parse(ExplorerHashKind kind, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        string? candidate;
        if (kind == ExplorerHashKind.Esplora)
        {
            candidate = body.Trim();
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                candidate = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("blockHash", out var hash) && hash.ValueKind == JsonValueKind.String
                    ? hash.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return candidate is { Length: 64 } && candidate.All(Uri.IsHexDigit) ? candidate.ToLowerInvariant() : null;
    }
}
