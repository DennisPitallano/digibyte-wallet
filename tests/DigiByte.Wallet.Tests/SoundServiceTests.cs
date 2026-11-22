using DigiByte.Wallet.Storage;
using DigiByte.Web.Services;
using Microsoft.JSInterop;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The optional chimes: silent unless the user turned Sounds on, one switch per device, and never a reason for a page
/// to fail (storage or the browser refusing is ignored).
/// </summary>
public class SoundServiceTests
{
    private readonly TestMemoryStorage _storage = new();
    private readonly RecordingJs _js = new();
    private SoundService NewService() => new(_storage, _js);

    [Fact]
    public async Task Sounds_are_off_until_the_user_turns_them_on()
    {
        var sounds = NewService();

        await sounds.InitializeAsync();
        await sounds.PlayAsync(Sound.Receive);
        await sounds.PlayAsync(Sound.Send);

        Assert.False(await sounds.IsEnabledAsync());
        Assert.Empty(_js.Calls);
    }

    [Fact]
    public async Task Turning_sounds_on_is_remembered_and_plays_the_named_chime()
    {
        await NewService().SetEnabledAsync(true);
        _js.Calls.Clear();

        var afterRestart = NewService();
        await afterRestart.PlayAsync(Sound.Receive);
        await afterRestart.PlayAsync(Sound.Send);

        Assert.True(await afterRestart.IsEnabledAsync());
        Assert.Equal(["sounds.play:receive", "sounds.play:send"], _js.Calls);
    }

    [Fact]
    public async Task Turning_sounds_on_gets_the_files_ready()
    {
        await NewService().SetEnabledAsync(true);

        Assert.Equal(["sounds.arm"], _js.Calls);
    }

    [Fact]
    public async Task Starting_the_app_with_sounds_on_gets_the_files_ready()
    {
        await _storage.SetAsync(SoundService.PreferenceKey, "1");

        await NewService().InitializeAsync();

        Assert.Equal(["sounds.arm"], _js.Calls);
    }

    [Fact]
    public async Task Turning_sounds_off_is_remembered_and_silences_both_chimes()
    {
        var sounds = NewService();
        await sounds.SetEnabledAsync(true);
        await sounds.SetEnabledAsync(false);
        _js.Calls.Clear();

        await sounds.PlayAsync(Sound.Receive);
        await NewService().PlayAsync(Sound.Send);

        Assert.Empty(_js.Calls);
        Assert.False(await _storage.ContainsKeyAsync(SoundService.PreferenceKey));
    }

    [Fact]
    public async Task A_browser_that_refuses_to_play_does_not_break_the_caller()
    {
        var sounds = NewService();
        await sounds.SetEnabledAsync(true);
        _js.Throw = true;

        await sounds.PlayAsync(Sound.Receive);
        await sounds.InitializeAsync();
    }

    [Fact]
    public async Task Unreadable_storage_means_silent_not_broken()
    {
        var sounds = new SoundService(new BrokenStorage(), _js);

        await sounds.InitializeAsync();
        await sounds.PlayAsync(Sound.Receive);

        Assert.False(await sounds.IsEnabledAsync());
        Assert.Empty(_js.Calls);
    }

    [Fact]
    public async Task A_choice_that_cannot_be_saved_still_holds_while_the_app_is_open()
    {
        var sounds = new SoundService(new BrokenStorage(), _js);

        await sounds.SetEnabledAsync(true);
        await sounds.PlayAsync(Sound.Send);

        Assert.Contains("sounds.play:send", _js.Calls);
    }

    [Theory]
    [InlineData(Sound.Receive, "receive")]
    [InlineData(Sound.Send, "send")]
    public void Every_chime_has_an_audio_file_with_its_name(Sound sound, string expected)
    {
        Assert.Equal(expected, SoundService.WireName(sound));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DigiByte.Web", "wwwroot", "audio")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        Assert.True(File.Exists(Path.Combine(dir.FullName, "src", "DigiByte.Web", "wwwroot", "audio", expected + ".mp3")));
    }

    [Fact]
    public void Every_sound_is_covered_by_the_file_test()
    {
        Assert.Equal([Sound.Receive, Sound.Send], Enum.GetValues<Sound>());
    }

    private sealed class RecordingJs : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public bool Throw { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (Throw) throw new JSException("refused");
            Calls.Add(args is { Length: > 0 } ? $"{identifier}:{args[0]}" : identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class BrokenStorage : ISecureStorage
    {
        public Task<string?> GetAsync(string key) => throw new InvalidOperationException("storage unavailable");
        public Task SetAsync(string key, string value) => throw new InvalidOperationException("storage unavailable");
        public Task RemoveAsync(string key) => throw new InvalidOperationException("storage unavailable");
        public Task<bool> ContainsKeyAsync(string key) => throw new InvalidOperationException("storage unavailable");
        public Task ClearAsync() => throw new InvalidOperationException("storage unavailable");
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => throw new InvalidOperationException("storage unavailable");
    }
}
