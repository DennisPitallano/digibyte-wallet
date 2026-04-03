using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DigiByte.NodeApi.RpcClient;

/// <summary>
/// Generic JSON-RPC 1.0 client for communicating with digibyted.
/// All 87 RPC methods go through CallAsync{T}.
/// </summary>
public class DigiByteRpcClient
{
    private readonly HttpClient _http;
    private readonly NodeConfig _config;
    private readonly ILogger<DigiByteRpcClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public DigiByteRpcClient(HttpClient http, NodeConfig config, ILogger<DigiByteRpcClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;

        // Set base address and auth header
        _http.BaseAddress = new Uri(config.RpcUrl);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.RpcUser}:{config.RpcPassword}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _http.Timeout = TimeSpan.FromMinutes(5); // Mining can take a while
    }

    /// <summary>
    /// Call any RPC method and deserialize the result.
    /// </summary>
    public async Task<T> CallAsync<T>(string method, params object?[] args)
    {
        var request = new RpcRequest
        {
            Method = method,
            Params = args.Where(a => a != null).ToArray()
        };

        var json = JsonSerializer.Serialize(request, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("RPC → {Method}({Args})", method, string.Join(", ", args.Select(a => a?.ToString() ?? "null")));

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync("", content);
        }
        catch (HttpRequestException ex)
        {
            throw new RpcException(-1, $"Cannot connect to DigiByte node at {_config.RpcUrl}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new RpcException(-1, $"RPC request to {method} timed out");
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(responseBody))
        {
            throw new RpcException((int)response.StatusCode,
                $"HTTP {(int)response.StatusCode} from node: {response.ReasonPhrase}");
        }

        var rpcResponse = JsonSerializer.Deserialize<RpcResponse<T>>(responseBody, JsonOpts);

        if (rpcResponse?.Error != null)
        {
            throw new RpcException(rpcResponse.Error.Code, rpcResponse.Error.Message);
        }

        return rpcResponse!.Result!;
    }

    /// <summary>
    /// Call an RPC method that returns no meaningful result (e.g., stop, ping).
    /// </summary>
    public async Task CallVoidAsync(string method, params object?[] args)
    {
        await CallAsync<object?>(method, args);
    }

    /// <summary>
    /// Call an RPC method and return the raw JSON string.
    /// </summary>
    public async Task<string> CallRawAsync(string method, params object?[] args)
    {
        var request = new RpcRequest
        {
            Method = method,
            Params = args.Where(a => a != null).ToArray()
        };

        var json = JsonSerializer.Serialize(request, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("", content);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Check if the node is reachable.
    /// </summary>
    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            await CallAsync<int>("getblockcount");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
