using System.Text.Json.Serialization;

namespace DigiByte.NodeApi.RpcClient;

/// <summary>
/// JSON-RPC 1.0 request envelope.
/// </summary>
public class RpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "1.0";
    [JsonPropertyName("id")] public string Id { get; set; } = "dgb";
    [JsonPropertyName("method")] public required string Method { get; set; }
    [JsonPropertyName("params")] public object?[] Params { get; set; } = [];
}

/// <summary>
/// JSON-RPC 1.0 response envelope.
/// </summary>
public class RpcResponse<T>
{
    [JsonPropertyName("result")] public T? Result { get; set; }
    [JsonPropertyName("error")] public RpcError? Error { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public class RpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public class RpcException : Exception
{
    public int Code { get; }
    public RpcException(int code, string message) : base(message) => Code = code;
}

/// <summary>
/// Configuration for connecting to a digibyted node.
/// </summary>
public class NodeConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int MainnetPort { get; set; } = 14022;
    public int TestnetPort { get; set; } = 14023;
    public string RpcUser { get; set; } = "dgbrpc";
    public string RpcPassword { get; set; } = "changeme";
    public bool IsTestnet { get; set; } = true;
    public bool FaucetEnabled { get; set; } = true;
    public decimal FaucetMaxAmount { get; set; } = 100;
    public int FaucetCooldownMinutes { get; set; } = 60;

    public int ActivePort => IsTestnet ? TestnetPort : MainnetPort;
    public string RpcUrl => $"http://{Host}:{ActivePort}/";
}
