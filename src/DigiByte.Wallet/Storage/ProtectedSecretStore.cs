using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage.Envelope;

namespace DigiByte.Wallet.Storage;

/// <summary>The three storage keys that together hold one protected secret during and after migration.</summary>
public sealed record ProtectedSlot(string LegacyKey, string V2Key, string RecordKey);

/// <summary>Per-secret migration state (<c>wallet_migration_{id}</c>).</summary>
public sealed class MigrationRecord
{
    public DateTimeOffset MigratedAt { get; set; }
    /// <summary>SHA-256 (hex) of the legacy value at migration time. A mismatch means an older app version changed it.</summary>
    public string LegacyFingerprint { get; set; } = "";
    public DateTimeOffset? LegacyRemovedAt { get; set; }
}

/// <summary>
/// Opens protected secrets and moves them from the legacy format to the <c>dgb2</c> envelope.
/// Storage layout and read order: contracts/storage-keys.md. Migration steps: research R4.
/// The legacy value is never modified by migration; it is removed only after the rollback window.
/// </summary>
public sealed class ProtectedSecretStore
{
    /// <summary>How long the legacy copy is kept after a verified migration (clarification: 7 days).</summary>
    public static readonly TimeSpan LegacyRetention = TimeSpan.FromDays(7);

    private readonly ISecureStorage _storage;
    private readonly ICryptoService _crypto;
    private readonly TimeProvider _time;
    private readonly IMigrationLock _lock;
    private readonly HashSet<string> _justMigrated = new();

    public ProtectedSecretStore(ISecureStorage storage, ICryptoService crypto, TimeProvider? time = null, IMigrationLock? migrationLock = null)
    {
        _storage = storage;
        _crypto = crypto;
        _time = time ?? TimeProvider.System;
        _lock = migrationLock ?? NoMigrationLock.Instance;
    }

    /// <summary>True once, right after this secret was migrated in this session, so the UI can show the one-time upgrade notice.</summary>
    public bool TakeMigrationNotice(ProtectedSlot slot)
    {
        lock (_justMigrated) return _justMigrated.Remove(slot.RecordKey);
    }

    public static string Fingerprint(string legacyValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyValue)));

    public async Task<bool> ExistsAsync(ProtectedSlot slot) =>
        await _storage.ContainsKeyAsync(slot.LegacyKey) || await _storage.ContainsKeyAsync(slot.V2Key);

    /// <summary>
    /// Decrypts the secret with <paramref name="credential"/>. A successful open of a legacy-only (or stale) secret
    /// migrates it; a successful open from <c>v2</c> may remove an expired legacy copy. Neither can fail the open.
    /// </summary>
    public async Task<CryptoResult<byte[]>> OpenAsync(ProtectedSlot slot, string credential, CredentialKind kind)
    {
        var legacy = await _storage.GetAsync(slot.LegacyKey);
        var v2 = await _storage.GetAsync(slot.V2Key);
        var record = await ReadRecordAsync(slot);

        if (legacy is null && v2 is null)
            return CryptoResult<byte[]>.Fail(CryptoFailure.Unreadable);

        var useV2 = v2 is not null && (legacy is null || (record is not null && record.LegacyFingerprint == Fingerprint(legacy)));

        if (useV2)
        {
            var opened = await _crypto.DecryptAsync(v2!, credential);
            if (opened.Success)
                await TryRemoveExpiredLegacyAsync(slot, legacy, record);
            return opened;
        }

        var result = await _crypto.DecryptAsync(legacy!, credential);
        if (result.Success)
            await TryMigrateAsync(slot, legacy!, result.Value!, credential, kind);
        return result;
    }

    private async Task TryRemoveExpiredLegacyAsync(ProtectedSlot slot, string? legacy, MigrationRecord? record)
    {
        if (legacy is null || record is null) return;
        if (_time.GetUtcNow() - record.MigratedAt < LegacyRetention) return;
        try
        {
            await _storage.RemoveAsync(slot.LegacyKey);
            record.LegacyRemovedAt = _time.GetUtcNow();
            await _storage.SetAsync(slot.RecordKey, JsonSerializer.Serialize(record));
        }
        catch
        {
            // Cleanup is best effort; it is retried on the next successful unlock.
        }
    }

    private async Task TryMigrateAsync(ProtectedSlot slot, string legacy, byte[] plaintext, string credential, CredentialKind kind)
    {
        IAsyncDisposable? held = null;
        var wroteV2 = false;
        var recorded = false;
        try
        {
            held = await _lock.TryAcquireAsync(slot.RecordKey);
            if (held is null) return; // cannot lock: do not migrate, try on the next unlock

            // Another tab may have finished while we waited for the lock.
            var current = await ReadRecordAsync(slot);
            if (current is not null
                && current.LegacyFingerprint == Fingerprint(legacy)
                && await _storage.ContainsKeyAsync(slot.V2Key))
                return;

            var sealedResult = await _crypto.EncryptAsync(plaintext, credential, kind);
            if (!sealedResult.Success) return;
            var envelope = sealedResult.Value!;
            // Only a real dgb2 envelope counts; a crypto service without Argon2 returns legacy bytes here.
            if (EnvelopeCodec.Parse(envelope).Status != EnvelopeStatus.Ok) return;

            await _storage.SetAsync(slot.V2Key, envelope);
            wroteV2 = true;

            var check = await _crypto.DecryptAsync(envelope, credential);
            if (!check.Success || !check.Value!.AsSpan().SequenceEqual(plaintext))
                return; // finally removes the unverified v2 value

            await _storage.SetAsync(slot.RecordKey, JsonSerializer.Serialize(new MigrationRecord
            {
                MigratedAt = _time.GetUtcNow(),
                LegacyFingerprint = Fingerprint(legacy),
            }));
            recorded = true;
            lock (_justMigrated) _justMigrated.Add(slot.RecordKey);
        }
        catch
        {
            // Migration problems are silent and never block the unlock (FR-010, FR-011).
        }
        finally
        {
            if (wroteV2 && !recorded)
            {
                try { await _storage.RemoveAsync(slot.V2Key); } catch { /* the stale v2 is ignored without a record */ }
            }
            if (held is not null)
            {
                try { await held.DisposeAsync(); } catch { /* releasing a lock must not surface */ }
            }
        }
    }

    private async Task<MigrationRecord?> ReadRecordAsync(ProtectedSlot slot)
    {
        var json = await _storage.GetAsync(slot.RecordKey);
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<MigrationRecord>(json); }
        catch (JsonException) { return null; }
    }
}
