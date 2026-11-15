using DigiByte.SpvGateway.Sync;
using Microsoft.Data.Sqlite;
using NBitcoin;

namespace DigiByte.SpvGateway.Index;

/// <summary>SQLite store of the match index: per block its hash and the short hashes of output scripts and spent outpoints with the position of each transaction.</summary>
public sealed class BlockIndexStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    private BlockIndexStore(SqliteConnection db) => _db = db;

    public static BlockIndexStore Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS indexed_blocks (
                height     INTEGER PRIMARY KEY,
                hash       BLOB    NOT NULL,
                tx_count   INTEGER NOT NULL,
                indexed_at TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS entries (
                height   INTEGER NOT NULL,
                tx_index INTEGER NOT NULL,
                kind     INTEGER NOT NULL,
                key      BLOB    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_entries_key ON entries (kind, key, height);
            CREATE INDEX IF NOT EXISTS ix_entries_height ON entries (height);
            """;
        cmd.ExecuteNonQuery();
        return new BlockIndexStore(connection);
    }

    /// <summary>Stores a block. A block already stored at this height with the same hash is left alone; a different hash replaces it.</summary>
    public void Add(BlockIndexEntry entry, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (HasCore(entry.Height, entry.Hash))
                return;
            using var tx = _db.BeginTransaction();
            RemoveCore(entry.Height);
            Exec("INSERT INTO indexed_blocks (height, hash, tx_count, indexed_at) VALUES ($h, $x, $c, $t)",
                ("$h", entry.Height), ("$x", entry.Hash.ToBytes()), ("$c", entry.TransactionCount), ("$t", now.UtcDateTime.ToString("O")));
            foreach (var (kind, keys) in new[] { (0, entry.Outputs), (1, entry.Spends), (2, entry.TxKeys ?? []) })
                foreach (var key in keys)
                    Exec("INSERT INTO entries (height, tx_index, kind, key) VALUES ($h, $i, $k, $key)",
                        ("$h", entry.Height), ("$i", key.TxIndex), ("$k", kind), ("$key", key.Key));
            tx.Commit();
        }
    }

    public bool Has(int height, uint256 hash)
    {
        lock (_gate)
            return HasCore(height, hash);
    }

    private bool HasCore(int height, uint256 hash)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM indexed_blocks WHERE height = $h AND hash = $x";
        cmd.Parameters.AddWithValue("$h", height);
        cmd.Parameters.AddWithValue("$x", hash.ToBytes());
        return cmd.ExecuteScalar() is not null;
    }

    public void Remove(int height)
    {
        lock (_gate)
            RemoveCore(height);
    }

    private void RemoveCore(int height)
    {
        Exec("DELETE FROM entries WHERE height = $h", ("$h", height));
        Exec("DELETE FROM indexed_blocks WHERE height = $h", ("$h", height));
    }

    /// <summary>Drops everything below <paramref name="height"/> (the scan window moving forward).</summary>
    public void PruneBelow(int height)
    {
        lock (_gate)
        {
            Exec("DELETE FROM entries WHERE height < $h", ("$h", height));
            Exec("DELETE FROM indexed_blocks WHERE height < $h", ("$h", height));
        }
    }

    /// <summary>Removes indexed blocks whose hash is no longer the best-chain hash at their height. Returns how many were removed.</summary>
    public int RemoveNotOnBestChain(HeaderStore headers)
    {
        lock (_gate)
        {
            var stale = new List<int>();
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT height, hash FROM indexed_blocks";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var height = reader.GetInt32(0);
                    var hash = new uint256((byte[])reader[1]);
                    if (headers.GetBest(height)?.Header.Hash != hash)
                        stale.Add(height);
                }
            }
            foreach (var height in stale)
                RemoveCore(height);
            return stale.Count;
        }
    }

    public int? IndexedFromHeight() => Scalar("SELECT MIN(height) FROM indexed_blocks");

    public int? IndexedToHeight() => Scalar("SELECT MAX(height) FROM indexed_blocks");

    public int EntryCount() => Scalar("SELECT COUNT(*) FROM entries") ?? 0;

    /// <summary>The (height, transaction position) pairs in the range whose outputs pay a given script key or whose inputs spend a given outpoint key.</summary>
    public IReadOnlyList<(int Height, int TxIndex)> Find(IReadOnlyCollection<byte[]> scriptKeys, IReadOnlyCollection<byte[]> outpointKeys, int fromHeight, int toHeight)
    {
        var hits = new HashSet<(int, int)>();
        lock (_gate)
        {
            foreach (var (kind, keys) in new[] { (0, scriptKeys), (1, outpointKeys) })
            {
                foreach (var key in keys)
                {
                    using var cmd = _db.CreateCommand();
                    cmd.CommandText = "SELECT height, tx_index FROM entries WHERE kind = $k AND key = $key AND height BETWEEN $a AND $b";
                    cmd.Parameters.AddWithValue("$k", kind);
                    cmd.Parameters.AddWithValue("$key", key);
                    cmd.Parameters.AddWithValue("$a", fromHeight);
                    cmd.Parameters.AddWithValue("$b", toHeight);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        hits.Add((reader.GetInt32(0), reader.GetInt32(1)));
                }
            }
        }
        return hits.OrderBy(h => h.Item1).ThenBy(h => h.Item2).ToList();
    }

    /// <summary>The (height, position) pairs whose transaction id starts with the given short key.</summary>
    public IReadOnlyList<(int Height, int TxIndex)> FindByTxid(byte[] txidKey)
    {
        var hits = new List<(int, int)>();
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT height, tx_index FROM entries WHERE kind = 2 AND key = $key ORDER BY height";
            cmd.Parameters.AddWithValue("$key", txidKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                hits.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }
        return hits;
    }

    private int? Scalar(string sql)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            var value = cmd.ExecuteScalar();
            return value is null or DBNull ? null : Convert.ToInt32(value);
        }
    }

    private void Exec(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
