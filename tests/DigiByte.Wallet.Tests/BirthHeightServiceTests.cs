using System.Text.Json;
using DigiByte.Crypto.Headers;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>A wallet remembers where it started, or estimates it from a date, so a restore need not read the whole chain (T089, FR-008a).</summary>
public class BirthHeightServiceTests
{
    private const int Cp = 24563100;

    private sealed class MemoryStorage : ISecureStorage
    {
        public Dictionary<string, string> Data { get; } = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value) { Data[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { Data.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(Data.ContainsKey(key));
        public Task ClearAsync() { Data.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => Task.FromResult(Data.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private static readonly Lazy<Dictionary<int, byte[]>> HeaderBytes = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "headers-mainnet-run.json")));
        return doc.RootElement.GetProperty("headers").EnumerateArray()
            .ToDictionary(e => e.GetProperty("height").GetInt32(), e => Convert.FromHexString(e.GetProperty("header").GetString()!));
    });

    private static async Task<(BirthHeightService Service, GatewayHeaderStore Headers, MemoryStorage Storage)> RigAsync(int headersUpTo = Cp + 300)
    {
        var storage = new MemoryStorage();
        var headers = new GatewayHeaderStore(storage, Checkpoints.Mainnet, "mainnet", chunkSize: 100);
        await headers.InitializeAsync();
        if (headersUpTo > Cp)
            await headers.AppendRangeAsync(Enumerable.Range(Cp + 1, headersUpTo - Cp).Select(h => BlockHeader80.Parse(HeaderBytes.Value[h])));
        return (new BirthHeightService(storage, headers), headers, storage);
    }

    [Fact]
    public async Task A_new_wallet_records_the_verified_tip_as_its_birth()
    {
        var (service, headers, _) = await RigAsync();

        var birth = await service.RecordAtCreationAsync("w1");

        Assert.Equal(new WalletBirthHeight(Cp + 300, BirthOrigin.Recorded), birth);
        Assert.Equal(birth, await service.GetAsync("w1"));
        Assert.Equal(Cp + 300, headers.TipHeight);
    }

    [Fact]
    public async Task With_no_verified_headers_beyond_the_checkpoint_the_birth_is_unknown_rather_than_a_guess()
    {
        var (service, _, _) = await RigAsync(headersUpTo: Cp);

        var birth = await service.RecordAtCreationAsync("w1");

        Assert.Null(birth.Height);
        Assert.Equal(BirthOrigin.Unknown, birth.Origin);
    }

    [Fact]
    public async Task A_wallet_with_no_record_has_an_unknown_birth()
    {
        var (service, _, _) = await RigAsync();

        Assert.Equal(new WalletBirthHeight(null, BirthOrigin.Unknown), await service.GetAsync("nobody"));
    }

    [Fact]
    public async Task A_date_inside_the_verified_headers_converts_to_a_height_a_day_early_but_not_before_the_checkpoint_range()
    {
        var (service, headers, _) = await RigAsync();
        var target = Cp + 200;
        var date = DateTimeOffset.FromUnixTimeSeconds(headers.HeaderAt(target)!.Time);

        var height = service.EstimateHeight(date);

        // A date this close to the start of the headers reaches back past them, so it leans early by the margin.
        Assert.True(height <= target - 1);
        Assert.True(height >= target - BirthHeightService.EarlyMarginBlocks - 50);
    }

    [Fact]
    public async Task A_date_after_the_tip_gives_a_height_one_day_before_the_tip()
    {
        var (service, headers, _) = await RigAsync();

        var height = service.EstimateHeight(DateTimeOffset.FromUnixTimeSeconds(headers.HeaderAt(headers.TipHeight)!.Time).AddDays(30));

        Assert.Equal(headers.TipHeight - BirthHeightService.EarlyMarginBlocks, height);
    }

    [Fact]
    public async Task A_date_far_before_the_headers_is_extrapolated_at_fifteen_seconds_a_block()
    {
        var (service, headers, _) = await RigAsync();
        var firstTime = DateTimeOffset.FromUnixTimeSeconds(headers.HeaderAt(headers.FirstHeight)!.Time);

        var height = service.EstimateHeight(firstTime.AddDays(-30));

        var expected = headers.FirstHeight - 30 * 5760 - BirthHeightService.EarlyMarginBlocks;
        Assert.InRange(height, expected - 2, expected + 2);
    }

    [Fact]
    public async Task An_earlier_date_never_gives_a_later_height()
    {
        var (service, headers, _) = await RigAsync();
        var tipTime = DateTimeOffset.FromUnixTimeSeconds(headers.HeaderAt(headers.TipHeight)!.Time);

        var heights = new[] { 90, 60, 30, 10, 1 }.Select(days => service.EstimateHeight(tipTime.AddDays(-days))).ToArray();

        Assert.Equal(heights.OrderByDescending(h => h), heights.OrderByDescending(h => h)); // sanity
        for (var i = 1; i < heights.Length; i++)
            Assert.True(heights[i] >= heights[i - 1], "a later date must not start before an earlier one");
    }

    [Fact]
    public async Task The_height_is_never_below_one()
    {
        var (service, _, _) = await RigAsync();

        Assert.Equal(1, service.EstimateHeight(new DateTimeOffset(2009, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task A_date_chosen_on_restore_is_stored_with_the_estimated_origin()
    {
        var (service, headers, _) = await RigAsync();

        var birth = await service.SetFromDateAsync("w1", DateTimeOffset.FromUnixTimeSeconds(headers.HeaderAt(headers.TipHeight)!.Time).AddDays(-5));

        Assert.Equal(BirthOrigin.EstimatedFromDate, birth.Origin);
        Assert.NotNull(birth.Height);
        Assert.Equal(birth, await service.GetAsync("w1"));
    }

    [Fact]
    public async Task The_origin_is_stored_with_the_names_the_data_model_uses()
    {
        var (service, headers, storage) = await RigAsync();
        await service.SetFromDateAsync("w1", DateTimeOffset.FromUnixTimeSeconds(headers.HeaderAt(headers.TipHeight)!.Time));
        await service.RecordAtCreationAsync("w2");

        Assert.Contains("\"estimated-from-date\"", storage.Data[BirthHeightService.KeyFor("w1")]);
        Assert.Contains("\"recorded\"", storage.Data[BirthHeightService.KeyFor("w2")]);
    }

    [Fact]
    public async Task A_damaged_record_reads_as_unknown_and_a_delete_removes_it()
    {
        var (service, _, storage) = await RigAsync();
        storage.Data[BirthHeightService.KeyFor("w1")] = "not json";

        Assert.Equal(BirthOrigin.Unknown, (await service.GetAsync("w1")).Origin);

        await service.RecordAtCreationAsync("w1");
        await service.DeleteAsync("w1");
        Assert.False(storage.Data.ContainsKey(BirthHeightService.KeyFor("w1")));
    }
}
