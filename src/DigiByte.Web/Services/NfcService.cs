using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>
/// Web NFC API wrapper for tap-to-pay functionality.
/// Only works on Android Chrome with NFC hardware.
/// </summary>
public class NfcService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<NfcService>? _objRef;

    public bool IsSupported { get; private set; }
    public event Action<string>? OnTagRead;
    public event Action<string>? OnError;

    public NfcService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        IsSupported = await _js.InvokeAsync<bool>("nfcManager.isSupported");
    }

    /// <summary>
    /// Write a digibyte: URI to an NFC tag for tap-to-pay.
    /// </summary>
    public async Task WriteAsync(string digibyteUri)
    {
        await _js.InvokeVoidAsync("nfcManager.write", digibyteUri);
    }

    /// <summary>
    /// Start scanning for NFC tags containing payment data.
    /// </summary>
    public async Task StartReadingAsync()
    {
        _objRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("nfcManager.read", _objRef);
    }

    [JSInvokable]
    public void OnNfcRead(string data)
    {
        OnTagRead?.Invoke(data);
    }

    [JSInvokable]
    public void OnNfcError(string error)
    {
        OnError?.Invoke(error);
    }

    public async ValueTask DisposeAsync()
    {
        _objRef?.Dispose();
    }
}
