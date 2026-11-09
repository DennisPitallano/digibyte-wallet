using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Cross-tab migration lock backed by the browser's Web Locks API (<c>dgbCrypto.acquireLock</c>). When the
/// browser has no Web Locks, it falls back to a storage lease (<see cref="StorageLeaseMigrationLock"/>).
/// </summary>
public sealed class WebLockMigrationLock : IMigrationLock
{
    private readonly IJSRuntime _js;
    private readonly StorageLeaseMigrationLock _fallback;

    public WebLockMigrationLock(IJSRuntime js, ISecureStorage storage)
    {
        _js = js;
        _fallback = new StorageLeaseMigrationLock(storage);
    }

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(string name)
    {
        string? handle;
        try
        {
            // "unsupported" = no Web Locks; null = timed out waiting; otherwise a handle to release.
            handle = await _js.InvokeAsync<string?>("dgbCrypto.acquireLock", name);
        }
        catch (JSException)
        {
            return await _fallback.TryAcquireAsync(name);
        }

        if (handle is null) return null;
        if (handle == "unsupported") return await _fallback.TryAcquireAsync(name);
        return new Held(_js, handle);
    }

    private sealed class Held(IJSRuntime js, string handle) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await js.InvokeVoidAsync("dgbCrypto.releaseLock", handle); }
            catch (JSException) { /* the lock is released when the tab closes anyway */ }
        }
    }
}
