using System.Net;
using Microsoft.Data.Sqlite;

namespace DigiByte.SpvGateway.Peers;

/// <summary>An address and port, the identity of a peer.</summary>
public readonly record struct PeerEndpoint(IPAddress Address, int Port)
{
    public override string ToString() => Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{Address}]:{Port}" : $"{Address}:{Port}";

    /// <summary>
    /// The address range used to spread connections: the /16 of an IPv4 address, the first 32 bits of an IPv6 one. An IPv4-mapped IPv6
    /// address counts as the IPv4 address it wraps.
    /// </summary>
    public string Range()
    {
        var address = Address.IsIPv4MappedToIPv6 ? Address.MapToIPv4() : Address;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 ? $"{bytes[0]}.{bytes[1]}" : $"{bytes[0]:x2}{bytes[1]:x2}:{bytes[2]:x2}{bytes[3]:x2}";
    }
}

/// <summary>What the book remembers about one peer (data-model: PeerRecord).</summary>
public sealed record PeerRecord(
    PeerEndpoint Endpoint,
    ulong Services,
    string UserAgent,
    int ProtocolVersion,
    DateTimeOffset LastSeen,
    DateTimeOffset? LastGood,
    DateTimeOffset? LastFailure,
    int FailureCount,
    int Misbehavior,
    DateTimeOffset? BannedUntil)
{
    /// <summary>Service bit 0: the peer serves the full block history.</summary>
    public const ulong NodeNetwork = 1;

    public bool IsFullHistory => (Services & NodeNetwork) != 0;
}

/// <summary>
/// SQLite book of the peers the gateway knows (address and port are unique together). It decides who may be tried again: a peer that
/// failed waits twice as long after each failure (30 seconds, up to 6 hours), and a peer whose misbehavior reaches
/// <see cref="BanThreshold"/> is not used for <see cref="BanDuration"/>.
/// </summary>
public sealed class PeerBook : IDisposable
{
    public const int BanThreshold = 100;
    public static readonly TimeSpan BanDuration = TimeSpan.FromHours(24);
    public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    private PeerBook(SqliteConnection db) => _db = db;

    public static PeerBook Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS peers (
                address       TEXT    NOT NULL,
                port          INTEGER NOT NULL,
                services      INTEGER NOT NULL DEFAULT 0,
                user_agent    TEXT    NOT NULL DEFAULT '',
                protocol      INTEGER NOT NULL DEFAULT 0,
                last_seen     TEXT    NOT NULL,
                last_good     TEXT    NULL,
                last_failure  TEXT    NULL,
                failures      INTEGER NOT NULL DEFAULT 0,
                misbehavior   INTEGER NOT NULL DEFAULT 0,
                banned_until  TEXT    NULL,
                PRIMARY KEY (address, port)
            );
            """;
        cmd.ExecuteNonQuery();
        return new PeerBook(connection);
    }

    /// <summary>Remembers an address learned from a seed or another peer. Nothing changes when it is already known.</summary>
    public void Add(PeerEndpoint endpoint, DateTimeOffset now)
    {
        lock (_gate)
        {
            Run("INSERT OR IGNORE INTO peers (address, port, last_seen) VALUES ($a, $p, $t)", endpoint, ("$t", Text(now)));
        }
    }

    /// <summary>A successful handshake: remembers what the peer said and clears its failures (not its misbehavior).</summary>
    public void RecordGood(PeerEndpoint endpoint, ulong services, string userAgent, int protocolVersion, DateTimeOffset now)
    {
        lock (_gate)
        {
            Add(endpoint, now);
            Run("UPDATE peers SET services = $s, user_agent = $u, protocol = $v, last_seen = $t, last_good = $t, failures = 0, last_failure = NULL WHERE address = $a AND port = $p",
                endpoint, ("$s", (long)services), ("$u", userAgent), ("$v", protocolVersion), ("$t", Text(now)));
        }
    }

    public void RecordFailure(PeerEndpoint endpoint, DateTimeOffset now)
    {
        lock (_gate)
        {
            Add(endpoint, now);
            Run("UPDATE peers SET failures = failures + 1, last_failure = $t WHERE address = $a AND port = $p", endpoint, ("$t", Text(now)));
        }
    }

    /// <summary>Adds misbehavior points. Reaching the threshold bans the peer for a day.</summary>
    public void AddMisbehavior(PeerEndpoint endpoint, int points, DateTimeOffset now)
    {
        lock (_gate)
        {
            Add(endpoint, now);
            Run("UPDATE peers SET misbehavior = misbehavior + $m, banned_until = CASE WHEN misbehavior + $m >= $th THEN $b ELSE banned_until END WHERE address = $a AND port = $p",
                endpoint, ("$m", points), ("$th", BanThreshold), ("$b", Text(now + BanDuration)));
        }
    }

    public PeerRecord? Get(PeerEndpoint endpoint)
    {
        lock (_gate)
            return Query("WHERE address = $a AND port = $p", endpoint).FirstOrDefault();
    }

    public IReadOnlyList<PeerRecord> All()
    {
        lock (_gate)
            return Query("", null);
    }

    public int Count()
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM peers";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    /// <summary>The peers that may be tried now: not banned, and past their backoff. Best first (see <see cref="Rank"/>).</summary>
    public IReadOnlyList<PeerRecord> Candidates(DateTimeOffset now)
    {
        lock (_gate)
            return Query("", null).Where(p => IsUsable(p, now)).OrderBy(p => Rank(p, now)).ToList();
    }

    public static bool IsUsable(PeerRecord peer, DateTimeOffset now)
    {
        if (peer.BannedUntil is { } banned && now < banned)
            return false;
        return peer.LastFailure is not { } failed || now >= failed + Backoff(peer.FailureCount);
    }

    public static TimeSpan Backoff(int failures)
    {
        if (failures <= 0)
            return TimeSpan.Zero;
        var seconds = BaseBackoff.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 20));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    /// <summary>Lower is better: full-history peers first, then peers that worked before (most recent first), then fewer failures.</summary>
    private static (int, int, long, int) Rank(PeerRecord p, DateTimeOffset now) =>
        (p.IsFullHistory ? 0 : 1, p.LastGood is null ? 1 : 0, p.LastGood is { } good ? -good.ToUnixTimeSeconds() : 0, p.FailureCount);

    private IReadOnlyList<PeerRecord> Query(string where, PeerEndpoint? endpoint)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT address, port, services, user_agent, protocol, last_seen, last_good, last_failure, failures, misbehavior, banned_until FROM peers " + where;
        if (endpoint is { } e)
        {
            cmd.Parameters.AddWithValue("$a", e.Address.ToString());
            cmd.Parameters.AddWithValue("$p", e.Port);
        }
        var list = new List<PeerRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PeerRecord(
                new PeerEndpoint(IPAddress.Parse(reader.GetString(0)), reader.GetInt32(1)),
                (ulong)reader.GetInt64(2), reader.GetString(3), reader.GetInt32(4),
                Parse(reader.GetString(5))!.Value, Parse(reader.IsDBNull(6) ? null : reader.GetString(6)),
                Parse(reader.IsDBNull(7) ? null : reader.GetString(7)), reader.GetInt32(8), reader.GetInt32(9),
                Parse(reader.IsDBNull(10) ? null : reader.GetString(10))));
        }
        return list;
    }

    private void Run(string sql, PeerEndpoint endpoint, params (string Name, object Value)[] extra)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$a", endpoint.Address.ToString());
        cmd.Parameters.AddWithValue("$p", endpoint.Port);
        foreach (var (name, value) in extra)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static string Text(DateTimeOffset t) => t.UtcDateTime.ToString("O");

    private static DateTimeOffset? Parse(string? text) =>
        text is null ? null : new DateTimeOffset(DateTime.Parse(text, null, System.Globalization.DateTimeStyles.RoundtripKind), TimeSpan.Zero);

    public void Dispose() => _db.Dispose();
}
