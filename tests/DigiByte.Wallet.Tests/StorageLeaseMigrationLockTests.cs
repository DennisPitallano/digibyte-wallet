using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

public class StorageLeaseMigrationLockTests
{
    [Fact]
    public async Task Second_holder_is_refused_while_a_lease_is_live_and_allowed_after_release()
    {
        var storage = new FaultyStorage();
        var a = new StorageLeaseMigrationLock(storage);
        var b = new StorageLeaseMigrationLock(storage);

        var first = await a.TryAcquireAsync("w1");
        Assert.NotNull(first);
        Assert.Null(await b.TryAcquireAsync("w1"));

        await first!.DisposeAsync();
        var second = await b.TryAcquireAsync("w1");
        Assert.NotNull(second);
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task An_expired_lease_is_taken_over()
    {
        var storage = new FaultyStorage();
        var clock = new ManualClock();
        var crashed = new StorageLeaseMigrationLock(storage, clock);
        Assert.NotNull(await crashed.TryAcquireAsync("w1")); // never released: simulates a closed tab

        clock.Advance(StorageLeaseMigrationLock.LeaseDuration + TimeSpan.FromSeconds(1));

        Assert.NotNull(await new StorageLeaseMigrationLock(storage, clock).TryAcquireAsync("w1"));
    }

    [Fact]
    public async Task Different_names_do_not_block_each_other()
    {
        var storage = new FaultyStorage();
        var l = new StorageLeaseMigrationLock(storage);
        Assert.NotNull(await l.TryAcquireAsync("w1"));
        Assert.NotNull(await l.TryAcquireAsync("w2"));
    }
}
