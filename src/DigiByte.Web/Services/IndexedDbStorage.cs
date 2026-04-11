using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// ISecureStorage implementation backed by browser IndexedDB via JS interop.
/// </summary>
public class IndexedDbStorage : ISecureStorage
{
    private readonly IJSRuntime _js;

    public IndexedDbStorage(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string?> GetAsync(string key)
    {
        return await _js.InvokeAsync<string?>("secureStorage.get", key);
    }

    public async Task SetAsync(string key, string value)
    {
        await _js.InvokeVoidAsync("secureStorage.set", key, value);
    }

    public async Task RemoveAsync(string key)
    {
        await _js.InvokeVoidAsync("secureStorage.remove", key);
    }

    public async Task<bool> ContainsKeyAsync(string key)
    {
        return await _js.InvokeAsync<bool>("secureStorage.containsKey", key);
    }

    public async Task ClearAsync()
    {
        await _js.InvokeVoidAsync("secureStorage.clear");
    }

    public async Task<List<string>> GetKeysWithPrefixAsync(string prefix)
    {
        var keys = await _js.InvokeAsync<string[]>("secureStorage.getKeysWithPrefix", prefix);
        return keys?.ToList() ?? [];
    }
}
