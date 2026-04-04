using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Tracks network connectivity and exposes online/offline state to components.
/// </summary>
public sealed class NetworkStatusService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<NetworkStatusService>? _selfRef;
    private bool _initialized;

    public bool IsOnline { get; private set; } = true;
    public event Action? OnStatusChanged;

    public NetworkStatusService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            IsOnline = await _js.InvokeAsync<bool>("networkStatus.isOnline");
            _selfRef = DotNetObjectReference.Create(this);
            await _js.InvokeVoidAsync("networkStatus.initialize", _selfRef);
        }
        catch { /* JS interop may fail during prerender */ }
    }

    [JSInvokable]
    public void OnNetworkStatusChanged(bool isOnline)
    {
        if (IsOnline == isOnline) return;
        IsOnline = isOnline;
        OnStatusChanged?.Invoke();
    }

    // Offline data cache helpers
    public async Task CacheDataAsync(string key, string jsonData)
    {
        try { await _js.InvokeVoidAsync("offlineCache.save", key, jsonData); }
        catch { }
    }

    public async Task<string?> GetCachedDataAsync(string key, int maxAgeMs = 0)
    {
        try { return await _js.InvokeAsync<string?>("offlineCache.load", key, maxAgeMs); }
        catch { return null; }
    }

    public async Task<long> GetCacheAgeAsync(string key)
    {
        try { return await _js.InvokeAsync<long>("offlineCache.getAge", key); }
        catch { return -1; }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _js.InvokeVoidAsync("networkStatus.dispose"); }
        catch { }
        _selfRef?.Dispose();
    }
}
