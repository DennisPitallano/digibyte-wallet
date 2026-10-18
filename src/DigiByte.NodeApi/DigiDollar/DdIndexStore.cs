using Microsoft.Data.Sqlite;

namespace DigiByte.NodeApi.DigiDollar;

/// <summary>A DD output row as stored in the index.</summary>
public sealed record DdOutputRow(
    string Txid,
    int Vout,
    byte[] OutputKey,
    long AmountCents,
    string ScriptPubKeyHex,
    long ValueSats,
    int BlockHeight,
    string? SpentByTxid,
    int? SpentHeight);

/// <summary>A DD transaction row as stored in the index.</summary>
public sealed record DdTxRow(string Txid, int BlockHeight, long BlockTime, int TxType, long? FeeSats, int Seq);

public sealed record DdIndexerState(int? LastHeight, string? LastHash);

/// <summary>
/// SQLite store for the DigiDollar output index (data-model.md, server side): public chain data only, with no
/// user identifiers. Confirmed blocks only; the mempool is an in-memory overlay.
/// </summary>
public sealed class DdIndexStore : IDisposable
{
    /// <summary>How many recent block hashes are kept for reorg detection.</summary>
    public const int BlockRetention = 500;

    private const string OutputColumns =
        "txid, vout, output_key, amount_cents, script_pubkey, value_sats, block_height, spent_by_txid, spent_height";

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public DdIndexStore(string connectionString)
    {
        _db = new SqliteConnection(connectionString);
        _db.Open();
        Exec("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS dd_output (
              txid TEXT NOT NULL, vout INTEGER NOT NULL, output_key BLOB NOT NULL, amount_cents INTEGER NOT NULL,
              script_pubkey TEXT NOT NULL, value_sats INTEGER NOT NULL DEFAULT 0, block_height INTEGER NOT NULL,
              spent_by_txid TEXT NULL, spent_height INTEGER NULL,
              PRIMARY KEY (txid, vout));
            CREATE INDEX IF NOT EXISTS ix_dd_output_key ON dd_output(output_key);
            CREATE INDEX IF NOT EXISTS ix_dd_output_spent ON dd_output(spent_by_txid);
            CREATE TABLE IF NOT EXISTS dd_tx (
              txid TEXT PRIMARY KEY, block_height INTEGER NOT NULL, block_time INTEGER NOT NULL,
              tx_type INTEGER NOT NULL, fee_sats INTEGER NULL, seq INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_dd_tx_height ON dd_tx(block_height, seq);
            CREATE TABLE IF NOT EXISTS dd_block (height INTEGER PRIMARY KEY, hash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS indexer_state (
              id INTEGER PRIMARY KEY CHECK (id = 1), last_height INTEGER NULL, last_hash TEXT NULL);
            INSERT OR IGNORE INTO indexer_state (id, last_height, last_hash) VALUES (1, NULL, NULL);
            """);
    }

    public static DdIndexStore InMemory() => new("Data Source=:memory:");

    // ------------------------------------------------------------------ state

    public DdIndexerState GetState()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT last_height, last_hash FROM indexer_state WHERE id = 1";
            using var r = cmd.ExecuteReader();
            r.Read();
            return new DdIndexerState(r.IsDBNull(0) ? null : r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1));
        }
    }

    public string? GetBlockHash(int height)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT hash FROM dd_block WHERE height = $h";
            cmd.Parameters.AddWithValue("$h", height);
            return cmd.ExecuteScalar() as string;
        }
    }

    // ------------------------------------------------------------------ writes

    /// <summary>
    /// Apply one block atomically: insert DD outputs, mark spends (transactions are processed in block order so a
    /// later tx can spend an earlier tx's output in the same block), record the tx rows, and advance the state.
    /// </summary>
    public void ApplyBlock(int height, string hash, long blockTime, IReadOnlyList<(DdParsedTx Tx, long? FeeSats)> txs)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            var seq = 0;
            foreach (var (parsed, fee) in txs)
            {
                foreach (var o in parsed.Outputs)
                {
                    Exec("""
                        INSERT OR REPLACE INTO dd_output
                        (txid, vout, output_key, amount_cents, script_pubkey, value_sats, block_height, spent_by_txid, spent_height)
                        VALUES ($t, $v, $k, $a, $s, 0, $h, NULL, NULL)
                        """, tx,
                        ("$t", parsed.Txid), ("$v", o.Vout), ("$k", o.OutputKey), ("$a", o.AmountCents),
                        ("$s", o.ScriptPubKeyHex), ("$h", height));
                }

                foreach (var s in parsed.Spends)
                {
                    Exec("""
                        UPDATE dd_output SET spent_by_txid = $by, spent_height = $h
                        WHERE txid = $t AND vout = $v AND spent_by_txid IS NULL
                        """, tx,
                        ("$by", parsed.Txid), ("$h", height), ("$t", s.Txid), ("$v", s.Vout));
                }

                Exec("""
                    INSERT OR REPLACE INTO dd_tx (txid, block_height, block_time, tx_type, fee_sats, seq)
                    VALUES ($t, $h, $bt, $ty, $f, $q)
                    """, tx,
                    ("$t", parsed.Txid), ("$h", height), ("$bt", blockTime), ("$ty", (int)parsed.Type),
                    ("$f", (object?)fee ?? DBNull.Value), ("$q", seq++));
            }

            Exec("INSERT OR REPLACE INTO dd_block (height, hash) VALUES ($h, $x)", tx, ("$h", height), ("$x", hash));
            Exec("DELETE FROM dd_block WHERE height <= $c", tx, ("$c", height - BlockRetention));
            Exec("UPDATE indexer_state SET last_height = $h, last_hash = $x WHERE id = 1", tx, ("$h", height), ("$x", hash));
            tx.Commit();
        }
    }

    /// <summary>Undo everything above <paramref name="height"/> (reorg): drop later outputs/txs and un-spend later spends.</summary>
    public void RollbackTo(int height, string? hashAtHeight)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            Exec("DELETE FROM dd_output WHERE block_height > $h", tx, ("$h", height));
            Exec("UPDATE dd_output SET spent_by_txid = NULL, spent_height = NULL WHERE spent_height > $h", tx, ("$h", height));
            Exec("DELETE FROM dd_tx WHERE block_height > $h", tx, ("$h", height));
            Exec("DELETE FROM dd_block WHERE height > $h", tx, ("$h", height));
            Exec("UPDATE indexer_state SET last_height = $h, last_hash = $x WHERE id = 1", tx,
                ("$h", height < 0 ? DBNull.Value : height), ("$x", (object?)hashAtHeight ?? DBNull.Value));
            tx.Commit();
        }
    }

    // ------------------------------------------------------------------ reads

    public IReadOnlyList<DdOutputRow> GetOutputsByKeys(IReadOnlyCollection<byte[]> keys, bool unspentOnly)
    {
        var rows = new List<DdOutputRow>();
        foreach (var key in keys)
        {
            rows.AddRange(QueryOutputs(
                $"SELECT {OutputColumns} FROM dd_output WHERE output_key = $k {(unspentOnly ? "AND spent_by_txid IS NULL" : "")}",
                ("$k", key)));
        }
        return rows;
    }

    public IReadOnlyList<DdOutputRow> GetOutputsCreatedBy(string txid) =>
        QueryOutputs($"SELECT {OutputColumns} FROM dd_output WHERE txid = $t ORDER BY vout", ("$t", txid));

    public IReadOnlyList<DdOutputRow> GetOutputsSpentBy(string txid) =>
        QueryOutputs($"SELECT {OutputColumns} FROM dd_output WHERE spent_by_txid = $t ORDER BY txid, vout", ("$t", txid));

    public DdOutputRow? GetOutput(string txid, int vout) =>
        QueryOutputs($"SELECT {OutputColumns} FROM dd_output WHERE txid = $t AND vout = $v", ("$t", txid), ("$v", vout))
            .FirstOrDefault();

    public DdTxRow? GetTx(string txid)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT txid, block_height, block_time, tx_type, fee_sats, seq FROM dd_tx WHERE txid = $t";
            cmd.Parameters.AddWithValue("$t", txid);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadTx(r) : null;
        }
    }

    /// <summary>
    /// Confirmed DD txs that created or spent an output owned by any of <paramref name="keys"/>, newest first
    /// (height desc, seq desc), strictly before <paramref name="before"/> when given.
    /// </summary>
    public IReadOnlyList<DdTxRow> GetTxsTouching(IReadOnlyCollection<byte[]> keys, int count, DdTxRow? before)
    {
        var all = new Dictionary<string, DdTxRow>();
        lock (_gate)
        {
            foreach (var key in keys)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    SELECT t.txid, t.block_height, t.block_time, t.tx_type, t.fee_sats, t.seq FROM dd_tx t
                    WHERE t.txid IN (SELECT txid FROM dd_output WHERE output_key = $k
                                     UNION SELECT spent_by_txid FROM dd_output WHERE output_key = $k AND spent_by_txid IS NOT NULL)
                    """;
                cmd.Parameters.AddWithValue("$k", key);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var row = ReadTx(r);
                    all[row.Txid] = row;
                }
            }
        }

        IEnumerable<DdTxRow> rows = all.Values.OrderByDescending(t => t.BlockHeight).ThenByDescending(t => t.Seq);
        if (before is not null)
            rows = rows.Where(t => t.BlockHeight < before.BlockHeight || (t.BlockHeight == before.BlockHeight && t.Seq < before.Seq));
        return rows.Take(count).ToList();
    }

    // ------------------------------------------------------------------ helpers

    private IReadOnlyList<DdOutputRow> QueryOutputs(string sql, params (string Name, object Value)[] p)
    {
        var rows = new List<DdOutputRow>();
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new DdOutputRow(
                    r.GetString(0), r.GetInt32(1), (byte[])r["output_key"], r.GetInt64(3), r.GetString(4), r.GetInt64(5),
                    r.GetInt32(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetInt32(8)));
            }
        }
        return rows;
    }

    private static DdTxRow ReadTx(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt32(1), r.GetInt64(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetInt32(5));

    private void Exec(string sql, SqliteTransaction? tx = null, params (string Name, object Value)[] p)
    {
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
