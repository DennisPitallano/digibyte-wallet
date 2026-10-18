using Microsoft.Data.Sqlite;

namespace DigiByte.NodeApi.Metrics;

public sealed record DailyCounts(string Date, IReadOnlyDictionary<string, long> Events);

/// <summary>
/// Anonymous, aggregate-only usage counters (research R17, SC-005, FR-023): a per-day, per-event count and nothing else.
/// No IP address, user agent, cookie, or identifier of any kind is stored (there is no column for one).
/// </summary>
public sealed class MetricsStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public MetricsStore(string connectionString)
    {
        _db = new SqliteConnection(connectionString);
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS metric_count (
              day TEXT NOT NULL,
              event TEXT NOT NULL,
              n INTEGER NOT NULL,
              PRIMARY KEY (day, event));
            """;
        cmd.ExecuteNonQuery();
    }

    public static MetricsStore InMemory() => new("Data Source=:memory:");

    public void Increment(string eventName, DateOnly day)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO metric_count (day, event, n) VALUES ($d, $e, 1)
                ON CONFLICT (day, event) DO UPDATE SET n = n + 1
                """;
            cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$e", eventName);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Counts per day, newest first, for the last <paramref name="days"/> days that have any.</summary>
    public IReadOnlyList<DailyCounts> Summary(int days)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT day, event, n FROM metric_count ORDER BY day DESC, event";
            var byDay = new SortedDictionary<string, Dictionary<string, long>>(Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a)));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var day = r.GetString(0);
                if (!byDay.TryGetValue(day, out var events)) byDay[day] = events = new Dictionary<string, long>();
                events[r.GetString(1)] = r.GetInt64(2);
            }
            return byDay.Take(Math.Max(1, days)).Select(kv => new DailyCounts(kv.Key, kv.Value)).ToList();
        }
    }

    public void Dispose() => _db.Dispose();
}
