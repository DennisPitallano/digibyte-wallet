using System.Numerics;
using DigiByte.Crypto.Headers;
using Microsoft.Data.Sqlite;
using NBitcoin;

namespace DigiByte.SpvGateway.Sync;

/// <summary>A header as kept by the store.</summary>
public sealed record StoredHeader(int Height, BlockHeader80 Header, BigInteger ChainWork, bool IsBestChain);

public enum StoreAddResult
{
    Added,
    /// <summary>A header with this hash is already stored; nothing changed.</summary>
    Duplicate,
    /// <summary>The previous hash is not stored, so the header cannot be placed on any branch.</summary>
    ParentMissing,
    /// <summary>The height does not follow the parent's height.</summary>
    HeightMismatch,
    UnknownAlgorithm,
}

/// <summary>
/// SQLite store of block headers for every branch the gateway has seen, with exactly one best chain marked.
/// Rules from the data model: a hash is stored once, a header's previous hash must be the stored hash at height - 1 on its branch,
/// the algorithm must be one DigiByte defines, chain work is cumulative from the checkpoint, and one branch at a time is "best".
/// </summary>
public sealed class HeaderStore : IDisposable
{
    private readonly SqliteConnection _db;
    // A SqliteConnection must not be used from two threads at once, and the sync loop and the HTTP endpoints share this store.
    private readonly object _gate = new();
    private readonly string _network;

    private HeaderStore(SqliteConnection db, string network)
    {
        _db = db;
        _network = network;
    }

    /// <summary>Opens (creating if needed) the store at <paramref name="path"/>, or in memory for ":memory:".</summary>
    public static HeaderStore Open(string path, string network)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS headers (
                    network    TEXT    NOT NULL,
                    hash       BLOB    NOT NULL,
                    height     INTEGER NOT NULL,
                    prev_hash  BLOB    NOT NULL,
                    raw        BLOB    NOT NULL,
                    algo       INTEGER NOT NULL,
                    chain_work BLOB    NOT NULL,
                    is_best    INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (network, hash)
                );
                CREATE INDEX IF NOT EXISTS ix_headers_height ON headers (network, height);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_headers_best ON headers (network, height) WHERE is_best = 1;
                CREATE TABLE IF NOT EXISTS reorgs (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    network     TEXT    NOT NULL,
                    fork_height INTEGER NOT NULL,
                    depth       INTEGER NOT NULL,
                    at_utc      TEXT    NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }
        return new HeaderStore(connection, network);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Stores the checkpoint's headers as the best chain, with zero work. Does nothing if they are already there.</summary>
    public void SeedCheckpoint(Checkpoint checkpoint)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            for (var i = 0; i < checkpoint.Headers.Count; i++)
            {
                var header = checkpoint.Headers[i];
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR IGNORE INTO headers (network, hash, height, prev_hash, raw, algo, chain_work, is_best)
                    VALUES ($n, $h, $height, $p, $raw, $algo, $w, 1)
                    """;
                Bind(cmd, header, checkpoint.FirstHeight + i, BigInteger.Zero);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public StoreAddResult TryAdd(BlockHeader80 header, int height, BigInteger chainWork)
    {
        lock (_gate)
        {
            if (header.Algorithm == PowAlgorithm.Unknown)
                return StoreAddResult.UnknownAlgorithm;
            if (Get(header.Hash) is not null)
                return StoreAddResult.Duplicate;

            var parent = Get(header.PrevHash);
            if (parent is null)
                return StoreAddResult.ParentMissing;
            if (parent.Height != height - 1)
                return StoreAddResult.HeightMismatch;

            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO headers (network, hash, height, prev_hash, raw, algo, chain_work, is_best)
                VALUES ($n, $h, $height, $p, $raw, $algo, $w, 0)
                """;
            Bind(cmd, header, height, chainWork);
            cmd.ExecuteNonQuery();
            return StoreAddResult.Added;
        }
    }

    public StoredHeader? Get(uint256 hash)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT height, raw, chain_work, is_best FROM headers WHERE network = $n AND hash = $h";
            cmd.Parameters.AddWithValue("$n", _network);
            cmd.Parameters.AddWithValue("$h", hash.ToBytes());
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    public StoredHeader? GetBest(int height)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT height, raw, chain_work, is_best FROM headers WHERE network = $n AND height = $height AND is_best = 1";
            cmd.Parameters.AddWithValue("$n", _network);
            cmd.Parameters.AddWithValue("$height", height);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    public StoredHeader? BestTip()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT height, raw, chain_work, is_best FROM headers WHERE network = $n AND is_best = 1 ORDER BY height DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$n", _network);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    /// <summary>The height of the earliest stored header on the best chain, or null when the store is empty.</summary>
    public int? EarliestHeight()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT MIN(height) FROM headers WHERE network = $n AND is_best = 1";
            cmd.Parameters.AddWithValue("$n", _network);
            var value = cmd.ExecuteScalar();
            return value is null or DBNull ? null : Convert.ToInt32(value);
        }
    }

    /// <summary>Up to <paramref name="count"/> consecutive best-chain headers starting at <paramref name="from"/>.</summary>
    public IReadOnlyList<BlockHeader80> BestRange(int from, int count)
    {
        lock (_gate)
        {
            var result = new List<BlockHeader80>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT height, raw, chain_work, is_best FROM headers
                WHERE network = $n AND is_best = 1 AND height >= $from AND height < $to ORDER BY height
                """;
            cmd.Parameters.AddWithValue("$n", _network);
            cmd.Parameters.AddWithValue("$from", from);
            cmd.Parameters.AddWithValue("$to", from + count);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(Read(reader).Header);
            return result;
        }
    }

    /// <summary>
    /// Up to <paramref name="count"/> headers on the branch that ends at <paramref name="tip"/>, oldest first, following previous hashes
    /// (so it works for a branch that is not the best chain).
    /// </summary>
    public IReadOnlyList<StoredHeader> BranchEndingAt(uint256 tip, int count)
    {
        lock (_gate)
        {
            var result = new List<StoredHeader>();
            var current = Get(tip);
            while (current is not null && result.Count < count)
            {
                result.Add(current);
                current = Get(current.Header.PrevHash);
            }
            result.Reverse();
            return result;
        }
    }

    /// <summary>
    /// Makes the branch ending at <paramref name="newTip"/> the best chain, in one transaction: headers of the old best chain above the
    /// fork point lose the mark and the new branch gains it. Returns how many best-chain headers were replaced (0 for a plain extension)
    /// and records a reorganization when it is above 0.
    /// </summary>
    public int SetBestTip(uint256 newTip)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();

            var branch = new List<StoredHeader>();
            var cursor = Get(newTip) ?? throw new InvalidOperationException("The new tip is not stored.");
            while (!cursor.IsBestChain)
            {
                branch.Add(cursor);
                cursor = Get(cursor.Header.PrevHash) ?? throw new InvalidOperationException("The branch does not reach the best chain.");
            }
            var forkHeight = cursor.Height;

            int replaced;
            using (var count = _db.CreateCommand())
            {
                count.Transaction = tx;
                count.CommandText = "SELECT COUNT(*) FROM headers WHERE network = $n AND is_best = 1 AND height > $f";
                count.Parameters.AddWithValue("$n", _network);
                count.Parameters.AddWithValue("$f", forkHeight);
                replaced = Convert.ToInt32(count.ExecuteScalar());
            }
            // Count only those above the fork that the new branch actually replaces (the rest of the old chain stays if the new tip is higher).
            using (var clear = _db.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "UPDATE headers SET is_best = 0 WHERE network = $n AND is_best = 1 AND height > $f";
                clear.Parameters.AddWithValue("$n", _network);
                clear.Parameters.AddWithValue("$f", forkHeight);
                clear.ExecuteNonQuery();
            }
            foreach (var header in branch)
            {
                using var set = _db.CreateCommand();
                set.Transaction = tx;
                set.CommandText = "UPDATE headers SET is_best = 1 WHERE network = $n AND hash = $h";
                set.Parameters.AddWithValue("$n", _network);
                set.Parameters.AddWithValue("$h", header.Header.Hash.ToBytes());
                set.ExecuteNonQuery();
            }

            // A plain extension replaces nothing; a reorganization replaces the old chain's headers above the fork point.
            var depth = branch.Count > 0 && replaced > 0 ? replaced : 0;
            if (depth > 0)
            {
                using var log = _db.CreateCommand();
                log.Transaction = tx;
                log.CommandText = "INSERT INTO reorgs (network, fork_height, depth, at_utc) VALUES ($n, $f, $d, $t)";
                log.Parameters.AddWithValue("$n", _network);
                log.Parameters.AddWithValue("$f", forkHeight);
                log.Parameters.AddWithValue("$d", depth);
                log.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                log.ExecuteNonQuery();
            }
            tx.Commit();
            return depth;
        }
    }

    /// <summary>The recorded reorganizations as (fork height, depth), oldest first.</summary>
    public IReadOnlyList<(int ForkHeight, int Depth)> Reorganizations()
    {
        lock (_gate)
        {
            var result = new List<(int, int)>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT fork_height, depth FROM reorgs WHERE network = $n ORDER BY id";
            cmd.Parameters.AddWithValue("$n", _network);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add((reader.GetInt32(0), reader.GetInt32(1)));
            return result;
        }
    }

    private void Bind(SqliteCommand cmd, BlockHeader80 header, int height, BigInteger chainWork)
    {
        cmd.Parameters.AddWithValue("$n", _network);
        cmd.Parameters.AddWithValue("$h", header.Hash.ToBytes());
        cmd.Parameters.AddWithValue("$height", height);
        cmd.Parameters.AddWithValue("$p", header.PrevHash.ToBytes());
        cmd.Parameters.AddWithValue("$raw", header.ToBytes());
        cmd.Parameters.AddWithValue("$algo", (int)header.Algorithm);
        cmd.Parameters.AddWithValue("$w", chainWork.ToByteArray(isUnsigned: true, isBigEndian: true));
    }

    private static StoredHeader Read(SqliteDataReader reader)
    {
        var raw = (byte[])reader["raw"];
        var work = (byte[])reader["chain_work"];
        return new StoredHeader(
            reader.GetInt32(0),
            BlockHeader80.Parse(raw),
            work.Length == 0 ? BigInteger.Zero : new BigInteger(work, isUnsigned: true, isBigEndian: true),
            reader.GetInt32(3) == 1);
    }
}
