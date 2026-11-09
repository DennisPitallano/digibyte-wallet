namespace DigiByte.Wallet.Services;

/// <summary>
/// A named cross-tab lock used while a protected secret is migrated to the new format, so two tabs cannot
/// both rewrite it at the same time (FR-014). Implemented with the Web Locks API in the web project.
/// </summary>
public interface IMigrationLock
{
    /// <summary>
    /// Takes the lock for <paramref name="name"/>, waiting if another holder has it. Returns <c>null</c> when
    /// the lock cannot be taken at all; the caller then skips migrating and tries again on the next unlock.
    /// Disposing the result releases the lock.
    /// </summary>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string name);
}

/// <summary>Used when no real lock is supplied (tests and single-context hosts): never blocks.</summary>
public sealed class NoMigrationLock : IMigrationLock
{
    public static readonly NoMigrationLock Instance = new();

    public ValueTask<IAsyncDisposable?> TryAcquireAsync(string name) =>
        ValueTask.FromResult<IAsyncDisposable?>(new Nothing());

    private sealed class Nothing : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
