using DigiByte.Wallet.Storage;
using Microsoft.JSInterop;

namespace DigiByte.Web.Services;

/// <summary>The wallet's chimes. The wire names are the file names under <c>wwwroot/audio</c>.</summary>
public enum Sound
{
    /// <summary>A payment arrived.</summary>
    Receive,
    /// <summary>A send went out.</summary>
    Send,
}

/// <summary>
/// Optional sounds for the two money moments: a payment received and a send confirmed. Off unless the user turns them
/// on in Settings (one switch for this device). Sound is never the only signal: every caller also shows its toast or
/// overlay, so nothing is lost when this does nothing. Playing is best effort; a browser that refuses is ignored.
/// </summary>
public sealed class SoundService(ISecureStorage storage, IJSRuntime js)
{
    /// <summary>The per-device preference: "1" when sounds are on. Missing or unreadable means off.</summary>
    public const string PreferenceKey = "pref:sounds";

    private bool? _enabled;

    /// <summary>Whether the user has turned sounds on. Read from storage once, then remembered.</summary>
    public async Task<bool> IsEnabledAsync()
    {
        if (_enabled is { } known) return known;
        bool enabled;
        try { enabled = await storage.GetAsync(PreferenceKey) == "1"; }
        catch { return false; } // not remembered: a later read may succeed
        _enabled = enabled;
        return enabled;
    }

    /// <summary>
    /// Call once when the app starts: when sounds are on, loads the files and waits for the user's first tap, which
    /// browsers require before a page may play audio.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (await IsEnabledAsync()) await ArmAsync();
    }

    /// <summary>Turns sounds on or off for this device.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        _enabled = enabled;
        try
        {
            if (enabled) await storage.SetAsync(PreferenceKey, "1");
            else await storage.RemoveAsync(PreferenceKey);
        }
        catch { /* the choice still holds until the app is closed */ }
        if (enabled) await ArmAsync();
    }

    /// <summary>Plays <paramref name="sound"/> when sounds are on. Never throws.</summary>
    public async Task PlayAsync(Sound sound)
    {
        if (!await IsEnabledAsync()) return;
        try { await js.InvokeVoidAsync("sounds.play", WireName(sound)); } catch { }
    }

    /// <summary>The name the script and the audio file use for <paramref name="sound"/>.</summary>
    public static string WireName(Sound sound) => sound switch
    {
        Sound.Receive => "receive",
        Sound.Send => "send",
        _ => throw new ArgumentOutOfRangeException(nameof(sound)),
    };

    private async Task ArmAsync()
    {
        try { await js.InvokeVoidAsync("sounds.arm"); } catch { }
    }
}
