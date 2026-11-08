using System.Reflection;
using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// The sweep store keeps one record per swept Digi-ID address, per wallet, so a sweep's status survives a reload (FR-011a).
/// It must never hold anything secret (FR-015) and must never let two sweeps of one address be in flight (FR-012).
/// </summary>
public class DigiIdSweepStoreTests
{
    private const string WalletA = "wallet-a";
    private const string WalletB = "wallet-b";

    private static (DigiIdSweepStore Store, ChangeAddressTests.InMemoryStorage Storage) NewStore()
    {
        var storage = new ChangeAddressTests.InMemoryStorage();
        return (new DigiIdSweepStore(storage), storage);
    }

    private static DigiIdSweepRecord Record(string source = "DSource1", string id = "r1", DateTime? created = null,
        DigiIdSweepStatus status = DigiIdSweepStatus.Sending, string? txId = null) => new()
    {
        Id = id,
        Domain = "example.com",
        SourceAddress = source,
        DestinationAddress = "dgb1qdestination",
        AmountSatoshis = 5_000_000,
        FeeSatoshis = 19_200,
        TxId = txId,
        Status = status,
        CreatedUtc = created ?? DateTime.UtcNow,
        UpdatedUtc = created ?? DateTime.UtcNow,
    };

    // ─── Persistence ───

    [Fact]
    public async Task Records_PersistPerWalletId_AndReloadInANewStore()
    {
        var (store, storage) = NewStore();
        var (created, _) = await store.TryBeginAsync(WalletA, Record());
        Assert.True(created);

        Assert.NotNull(await storage.GetAsync($"digiid_sweeps_{WalletA}"));
        var reloaded = await new DigiIdSweepStore(storage).GetAllAsync(WalletA);

        var only = Assert.Single(reloaded);
        Assert.Equal("r1", only.Id);
        Assert.Equal(DigiIdSweepStatus.Sending, only.Status);
        Assert.Equal(5_000_000, only.AmountSatoshis);
    }

    [Fact]
    public async Task Records_AreKeptPerWallet()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());

        Assert.Single(await store.GetAllAsync(WalletA));
        Assert.Empty(await store.GetAllAsync(WalletB));
    }

    [Fact]
    public async Task GetAll_ReturnsNewestFirst()
    {
        var (store, _) = NewStore();
        var now = DateTime.UtcNow;
        await store.TryBeginAsync(WalletA, Record("DSourceOld", "old", now.AddMinutes(-10)));
        await store.TryBeginAsync(WalletA, Record("DSourceNew", "new", now));

        var all = await store.GetAllAsync(WalletA);

        Assert.Equal(["new", "old"], all.Select(r => r.Id));
    }

    [Fact]
    public async Task GetAll_NothingStored_IsEmpty()
    {
        var (store, _) = NewStore();

        Assert.Empty(await store.GetAllAsync(WalletA));
    }

    [Fact]
    public async Task GetAll_DamagedStorage_IsEmptyNotAnError()
    {
        var (store, storage) = NewStore();
        await storage.SetAsync($"digiid_sweeps_{WalletA}", "{ this is not json");

        Assert.Empty(await store.GetAllAsync(WalletA));
    }

    // ─── Transitions ───

    [Theory]
    [InlineData(DigiIdSweepStatus.Sending, DigiIdSweepStatus.Pending)]
    [InlineData(DigiIdSweepStatus.Sending, DigiIdSweepStatus.Failed)]
    [InlineData(DigiIdSweepStatus.Pending, DigiIdSweepStatus.Confirmed)]
    [InlineData(DigiIdSweepStatus.Pending, DigiIdSweepStatus.Failed)]
    public async Task Transition_LegalMoves_Apply(DigiIdSweepStatus from, DigiIdSweepStatus to)
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());
        if (from == DigiIdSweepStatus.Pending) await store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Pending, txId: "tx1");

        var updated = await store.TransitionAsync(WalletA, "r1", to, txId: from == DigiIdSweepStatus.Sending && to == DigiIdSweepStatus.Pending ? "tx1" : null,
            failureReason: to == DigiIdSweepStatus.Failed ? "rejected" : null);

        Assert.Equal(to, updated.Status);
        Assert.Equal(to, (await store.GetAllAsync(WalletA)).Single().Status);
    }

    [Theory]
    [InlineData(DigiIdSweepStatus.Confirmed, DigiIdSweepStatus.Pending)]
    [InlineData(DigiIdSweepStatus.Confirmed, DigiIdSweepStatus.Failed)]
    [InlineData(DigiIdSweepStatus.Confirmed, DigiIdSweepStatus.Sending)]
    [InlineData(DigiIdSweepStatus.Failed, DigiIdSweepStatus.Confirmed)]
    [InlineData(DigiIdSweepStatus.Failed, DigiIdSweepStatus.Pending)]
    [InlineData(DigiIdSweepStatus.Failed, DigiIdSweepStatus.Sending)]
    [InlineData(DigiIdSweepStatus.Pending, DigiIdSweepStatus.Sending)]
    [InlineData(DigiIdSweepStatus.Sending, DigiIdSweepStatus.Confirmed)]
    [InlineData(DigiIdSweepStatus.Pending, DigiIdSweepStatus.Pending)]
    public void IsLegalTransition_IllegalMoves_AreRejected(DigiIdSweepStatus from, DigiIdSweepStatus to)
    {
        Assert.False(DigiIdSweepStore.IsLegalTransition(from, to));
    }

    [Fact]
    public async Task Transition_Illegal_ThrowsAndLeavesTheRecordAlone()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());
        await store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Failed, failureReason: "rejected");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Confirmed));

        Assert.Equal(DigiIdSweepStatus.Failed, (await store.GetAllAsync(WalletA)).Single().Status);
    }

    [Fact]
    public async Task Transition_UnknownRecord_Throws()
    {
        var (store, _) = NewStore();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TransitionAsync(WalletA, "nope", DigiIdSweepStatus.Pending, txId: "tx"));
    }

    [Fact]
    public async Task Transition_ToPending_RequiresATxId()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Pending));
    }

    [Fact]
    public async Task Transition_Failed_KeepsTheReason_AndTouchesUpdatedTime()
    {
        var (store, _) = NewStore();
        var created = DateTime.UtcNow.AddMinutes(-5);
        await store.TryBeginAsync(WalletA, Record(created: created));

        var failed = await store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Failed, failureReason: "wallet locked");

        Assert.Equal("wallet locked", failed.FailureReason);
        Assert.True(failed.UpdatedUtc > created);
        Assert.Equal(created, failed.CreatedUtc);
    }

    [Fact]
    public async Task Transition_ToPending_SetsTxIdAndTheRealFee()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());

        var pending = await store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Pending, txId: "abc123", feeSatoshis: 20_000);

        Assert.Equal("abc123", pending.TxId);
        Assert.Equal(20_000, pending.FeeSatoshis);
    }

    // ─── In flight (FR-012) ───

    [Theory]
    [InlineData(DigiIdSweepStatus.Sending, true)]
    [InlineData(DigiIdSweepStatus.Pending, true)]
    [InlineData(DigiIdSweepStatus.Confirmed, false)]
    [InlineData(DigiIdSweepStatus.Failed, false)]
    public async Task InFlight_OnlySendingAndPending(DigiIdSweepStatus status, bool expected)
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());
        if (status == DigiIdSweepStatus.Pending) await store.TransitionAsync(WalletA, "r1", status, txId: "tx1");
        if (status == DigiIdSweepStatus.Confirmed)
        {
            await store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Pending, txId: "tx1");
            await store.TransitionAsync(WalletA, "r1", status);
        }
        if (status == DigiIdSweepStatus.Failed) await store.TransitionAsync(WalletA, "r1", status, failureReason: "x");

        var inFlight = await store.GetInFlightAddressesAsync(WalletA);

        Assert.Equal(expected, inFlight.Contains("DSource1"));
        Assert.Equal(expected, (await store.GetAllAsync(WalletA)).Single().IsInFlight);
    }

    [Fact]
    public async Task TryBegin_AddressAlreadyInFlight_ReturnsTheExistingRecordAndCreatesNothing()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record(id: "first"));

        var (created, existing) = await store.TryBeginAsync(WalletA, Record(id: "second"));

        Assert.False(created);
        Assert.Equal("first", existing.Id);
        Assert.Single(await store.GetAllAsync(WalletA));
    }

    [Fact]
    public async Task TryBegin_AfterTheFirstFailed_CreatesANewRecord()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record(id: "first"));
        await store.TransitionAsync(WalletA, "first", DigiIdSweepStatus.Failed, failureReason: "rejected");

        var (created, record) = await store.TryBeginAsync(WalletA, Record(id: "second"));

        Assert.True(created);
        Assert.Equal("second", record.Id);
        Assert.Equal(2, (await store.GetAllAsync(WalletA)).Count);
    }

    [Fact]
    public async Task TryBegin_ConcurrentCallsForOneAddress_CreateExactlyOne()
    {
        var (store, _) = NewStore();

        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => store.TryBeginAsync(WalletA, Record(id: $"r{i}"))));

        Assert.Single(results, r => r.Created);
        Assert.Single(await store.GetAllAsync(WalletA));
    }

    [Fact]
    public async Task TryBegin_SameAddressInAnotherWallet_IsIndependent()
    {
        var (store, _) = NewStore();
        await store.TryBeginAsync(WalletA, Record());

        var (created, _) = await store.TryBeginAsync(WalletB, Record());

        Assert.True(created);
    }

    [Fact]
    public async Task TryBegin_OnlyAcceptsANewSendingRecord()
    {
        var (store, _) = NewStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.TryBeginAsync(WalletA, Record(status: DigiIdSweepStatus.Confirmed)));
    }

    [Fact]
    public async Task Store_KeepsAtMostTheNewestFinishedRecords()
    {
        var (store, _) = NewStore();
        var start = DateTime.UtcNow.AddHours(-10);
        for (var i = 0; i < DigiIdSweepStore.MaxRecords + 20; i++)
        {
            var id = $"r{i}";
            await store.TryBeginAsync(WalletA, Record($"DSource{i}", id, start.AddSeconds(i)));
            await store.TransitionAsync(WalletA, id, DigiIdSweepStatus.Failed, failureReason: "x");
        }

        var all = await store.GetAllAsync(WalletA);

        Assert.Equal(DigiIdSweepStore.MaxRecords, all.Count);
        Assert.Equal($"r{DigiIdSweepStore.MaxRecords + 19}", all[0].Id); // newest kept
    }

    [Fact]
    public async Task Store_NeverDropsARecordThatIsStillInFlight()
    {
        var (store, _) = NewStore();
        var start = DateTime.UtcNow.AddHours(-10);
        await store.TryBeginAsync(WalletA, Record("DOldInFlight", "inflight", start.AddDays(-1)));
        for (var i = 0; i < DigiIdSweepStore.MaxRecords + 5; i++)
        {
            var id = $"r{i}";
            await store.TryBeginAsync(WalletA, Record($"DSource{i}", id, start.AddSeconds(i)));
            await store.TransitionAsync(WalletA, id, DigiIdSweepStatus.Failed, failureReason: "x");
        }

        Assert.Contains(await store.GetAllAsync(WalletA), r => r.Id == "inflight");
    }

    // ─── Nothing secret (FR-015) ───

    [Fact]
    public async Task StoredJson_HoldsNoKeySeedRawTransactionOrSignature()
    {
        var (store, storage) = NewStore();
        await store.TryBeginAsync(WalletA, Record());
        await store.TransitionAsync(WalletA, "r1", DigiIdSweepStatus.Pending, txId: "abc123");

        var json = (await storage.GetAsync($"digiid_sweeps_{WalletA}"))!;
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateArray().SelectMany(e => e.EnumerateObject()).Select(p => p.Name).ToList();

        Assert.NotEmpty(names);
        foreach (var name in names)
            Assert.DoesNotMatch("(?i)key|seed|mnemonic|pin|secret|signature|raw|wif|hex", name);
    }

    [Theory]
    [InlineData(typeof(DigiIdSweepRecord))]
    [InlineData(typeof(DigiIdFundFinding))]
    [InlineData(typeof(DigiIdSweepPlan))]
    [InlineData(typeof(DigiIdSweepPlanItem))]
    [InlineData(typeof(DigiIdSweepExclusion))]
    public void ModelTypes_HaveNoSecretMembers(Type type)
    {
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.DoesNotMatch("(?i)privatekey|seed|mnemonic|secret|signature|raw|wif|^pin$", prop.Name);
            Assert.NotEqual(typeof(Key), prop.PropertyType);
            Assert.NotEqual(typeof(ExtKey), prop.PropertyType);
            Assert.NotEqual(typeof(byte[]), prop.PropertyType);
        }
    }
}
