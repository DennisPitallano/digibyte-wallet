namespace DigiByte.Wallet.Services;

/// <summary>
/// A receive index beyond the base run that the wallet checks individually (spec 027): found paid by a search
/// (<paramref name="Paid"/> true), or handed out by this wallet and not yet seen paid (false). Paid never goes back to false.
/// </summary>
public sealed record WatchedReceiveIndex(int Index, bool Paid);

/// <summary>
/// Which receive addresses the wallet checks (spec 027, data-model.md section 2). Three parts that do not depend on each
/// other: a base run from the first address, whose length only grows; watched indexes beyond it, checked one by one; and a
/// tail of <see cref="TailLength"/> after the highest paid watched index, which follows an outside issuer. Derived from the
/// saved wallet record, never saved. Pure.
/// </summary>
public sealed class ReceiveScanSet
{
    /// <summary>The first addresses are always checked, whatever the wallet has used.</summary>
    public const int MinBaseCount = 20;
    /// <summary>How many addresses after the highest paid watched index are checked, and how many unpaid addresses may be out past the anchor.</summary>
    public const int TailLength = 20;

    private readonly HashSet<int> _contains;
    private readonly List<WatchedReceiveIndex> _watched;
    private readonly int _shown;

    private ReceiveScanSet(int shown, int used, int baseCount, List<WatchedReceiveIndex> watched)
    {
        _shown = shown;
        _watched = watched;
        BaseCount = baseCount;

        int? maxPaid = null;
        foreach (var entry in watched)
            if (entry.Paid) maxPaid = entry.Index; // ascending: the last paid entry is the highest
        MaxPaid = maxPaid;

        Anchor = maxPaid is { } max ? Math.Max(used, max + 1) : used;
        NextNewIndex = Math.Max(shown + 1, Anchor);

        var indexes = new SortedSet<int>(Enumerable.Range(0, baseCount));
        foreach (var entry in watched) indexes.Add(entry.Index);
        if (maxPaid is { } paid)
            for (var i = 1; i <= TailLength; i++) indexes.Add(paid + i);
        Indexes = [.. indexes];
        _contains = [.. Indexes];
    }

    /// <param name="nextReceivingIndex">The address on show.</param>
    /// <param name="usedReceivingCount">One past the highest used index in the base run.</param>
    /// <param name="baseFloor">A lower bound on the base run, set when the address on show first left it.</param>
    /// <param name="watched">The saved watched entries, as saved.</param>
    public static ReceiveScanSet From(int nextReceivingIndex, int usedReceivingCount, int baseFloor,
        IReadOnlyList<WatchedReceiveIndex>? watched)
    {
        var shown = Math.Max(0, nextReceivingIndex);
        var used = Math.Max(0, usedReceivingCount);
        var floor = Math.Max(0, baseFloor);
        watched ??= [];

        // Tested against the list as saved, before entries the base run covers are ignored: otherwise the address on show
        // would feed the base count through a definition that refers to itself.
        var shownIsWatched = false;
        foreach (var entry in watched)
            if (entry.Index == shown) { shownIsWatched = true; break; }

        // The one threshold, computed first. While the address on show is a watched entry it does not feed it, so being
        // found paid cannot turn every address up to it into the base run.
        var baseCount = Math.Max(Math.Max(MinBaseCount, used), Math.Max(floor, shownIsWatched ? 0 : shown + 1));

        return new ReceiveScanSet(shown, used, baseCount, Normalize(baseCount, watched));
    }

    /// <summary>Every receive index the wallet checks, ascending, no duplicates.</summary>
    public IReadOnlyList<int> Indexes { get; }

    /// <summary>The base run is indexes 0 to this, exclusive.</summary>
    public int BaseCount { get; }

    /// <summary>The highest paid watched index, if any. The tail is the <see cref="TailLength"/> indexes after it.</summary>
    public int? MaxPaid { get; }

    /// <summary>One past the last index known to be used: where new addresses are counted from.</summary>
    public int Anchor { get; }

    /// <summary>The index a newly handed-out address would get.</summary>
    public int NextNewIndex { get; }

    /// <summary>False once <see cref="TailLength"/> unpaid addresses are out past the anchor: a restore would not find a further one.</summary>
    public bool CanHandOutNew => NextNewIndex < Anchor + TailLength;

    /// <summary>The new index is not contiguous with the base run, so it must be recorded as a watched entry.</summary>
    public bool HandOutIsWatched => NextNewIndex > BaseCount;

    public bool Contains(int index) => _contains.Contains(index);

    /// <summary>Watched indexes not yet seen paid: the automatic search looks each of them up.</summary>
    public IReadOnlyList<int> UnpaidWatched => [.. _watched.Where(w => !w.Paid).Select(w => w.Index)];

    /// <summary>What "Your addresses" lists: base-run and watched indexes at or below the address on show. No tail-only index.</summary>
    public IReadOnlyList<int> ListedIndexes =>
    [
        .. Enumerable.Range(0, Math.Min(BaseCount, _shown + 1)),
        .. _watched.Where(w => w.Index <= _shown).Select(w => w.Index),
    ];

    /// <summary>
    /// What a search of the first <paramref name="upTo"/> addresses has to look up: every index below it that is not in the
    /// base run and is not already a paid watched entry. Tail indexes and unpaid watched entries are included.
    /// </summary>
    public IReadOnlyList<int> SearchTargets(int upTo)
    {
        if (upTo <= BaseCount) return [];
        var paid = _watched.Where(w => w.Paid).Select(w => w.Index).ToHashSet();
        return [.. Enumerable.Range(BaseCount, upTo - BaseCount).Where(i => !paid.Contains(i))];
    }

    /// <summary>Equal exactly when <see cref="Indexes"/> are equal: the indexes written as runs.</summary>
    public string Fingerprint
    {
        get
        {
            var parts = new List<string>();
            var i = 0;
            while (i < Indexes.Count)
            {
                var start = Indexes[i];
                while (i + 1 < Indexes.Count && Indexes[i + 1] == Indexes[i] + 1) i++;
                parts.Add(start == Indexes[i] ? $"{start}" : $"{start}-{Indexes[i]}");
                i++;
            }
            return string.Join(',', parts);
        }
    }

    /// <summary>
    /// Sorted by index, one entry per index (paid wins), without the entries the base run has grown over. Nothing else is
    /// removed.
    /// </summary>
    public static List<WatchedReceiveIndex> Normalize(int baseCount, IEnumerable<WatchedReceiveIndex>? watched)
    {
        var byIndex = new SortedDictionary<int, bool>();
        foreach (var entry in watched ?? [])
        {
            if (entry.Index < baseCount) continue;
            byIndex[entry.Index] = entry.Paid || (byIndex.TryGetValue(entry.Index, out var paid) && paid);
        }
        return [.. byIndex.Select(pair => new WatchedReceiveIndex(pair.Key, pair.Value))];
    }

    /// <summary>The list with each of <paramref name="paidIndexes"/> at or above the base count added as paid, or set to paid.</summary>
    public static List<WatchedReceiveIndex> MarkPaid(int baseCount, IEnumerable<WatchedReceiveIndex>? watched, IEnumerable<int> paidIndexes) =>
        Normalize(baseCount, (watched ?? []).Concat(paidIndexes.Select(i => new WatchedReceiveIndex(i, true))));
}
