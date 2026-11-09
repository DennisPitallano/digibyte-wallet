using DigiByte.Wallet.Storage;

namespace DigiByte.Web.Services;

/// <summary>
/// The passphrase deny list, loaded lazily from the same-origin static file <c>data/common-passwords.txt</c>
/// (cached by the service worker, so it works offline). Until it has loaded, or if it cannot be loaded, the other
/// passphrase rules still apply and this one is skipped.
/// </summary>
public sealed class CommonPasswordList : ICommonPasswordList
{
    private readonly HttpClient _http;
    private HashSet<string>? _entries;
    private Task? _loading;

    public CommonPasswordList(HttpClient http)
    {
        _http = http;
    }

    public bool IsLoaded => _entries is not null;

    public bool Contains(string lowercased) => _entries?.Contains(lowercased) ?? false;

    /// <summary>Starts loading once; safe to call from every screen that asks for a passphrase.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var text = await _http.GetStringAsync("data/common-passwords.txt");
            _entries = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            _loading = null; // try again next time; the rest of the rules still apply
        }
    }
}
