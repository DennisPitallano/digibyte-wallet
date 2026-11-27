using System.Data;
using DigiByte.Crypto.KeyGeneration;
using DigiByte.Pay.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DigiByte.Pay.Api.Services;

/// <summary>
/// Gives out DigiDollar addresses derived from the store's own key (specs/035 research R4). Each address takes the next
/// index of the key's shared <see cref="DdKeySequence"/> with one atomic UPDATE ... RETURNING, so stores with the same key
/// and concurrent requests never get the same index. No outside service is called (FR-010).
/// </summary>
public sealed class DdAddressAllocator(DigiPayDbContext db)
{
    /// <summary>
    /// Allocate the next address for <paramref name="store"/> and add its <see cref="DdAddress"/> row to the context (saved
    /// with the caller's next SaveChanges). The index itself is consumed at once, so a failed save leaves a gap, never a reuse.
    /// </summary>
    public async Task<string> AllocateAsync(PayStore store, string kind, string? sessionId, CancellationToken ct = default)
    {
        if (store.DdAccountKey is null || store.DdKeyId is null)
            throw new InvalidOperationException("The store has no DigiDollar key.");
        var parsed = DigiDollarAccountKey.Parse(store.DdAccountKey, store.Network == "mainnet" ? "mainnet" : "testnet");
        if (!parsed.Ok) throw new InvalidOperationException($"The store's DigiDollar key can't be read ({parsed.Reason}).");
        var key = parsed.Key!;

        var index = await NextIndexAsync(key, ct);
        var address = key.Address(index);
        db.DdAddresses.Add(new DdAddress
        {
            Address = address,
            DdKeyId = key.KeyId,
            Index = index,
            Kind = kind,
            StoreId = store.Id,
            SessionId = sessionId,
        });
        return address;
    }

    private async Task<int> NextIndexAsync(DigiDollarAccountKey key, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var taken = await ExecuteScalarAsync(
                "UPDATE \"DdKeySequences\" SET \"NextIndex\" = \"NextIndex\" + 1 WHERE \"Id\" = @id RETURNING \"NextIndex\" - 1", key.KeyId, ct);
            if (taken is not null) return taken.Value;

            // No sequence yet (a key set before sequences existed): create it, then take an index as above.
            try
            {
                await ExecuteScalarAsync(
                    "INSERT INTO \"DdKeySequences\" (\"Id\", \"Network\", \"NextIndex\", \"CreatedAt\") VALUES (@id, @network, 0, @now)",
                    key.KeyId, ct, key.Network);
            }
            catch (Exception) when (attempt < 2) { /* another request created it first */ }
        }
        throw new InvalidOperationException("Couldn't take a DigiDollar address index.");
    }

    private async Task<int?> ExecuteScalarAsync(string sql, string id, CancellationToken ct, string? network = null)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            Add(command, "@id", id);
            if (network is not null)
            {
                Add(command, "@network", network);
                Add(command, "@now", DateTime.UtcNow);
            }
            var result = await command.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }
}
