namespace DigiByte.Crypto.Transactions;

/// <summary>The chosen input indexes, or why no valid selection exists.</summary>
public readonly record struct DdSelection(IReadOnlyList<int> Indexes, DdBuildError? Error);

/// <summary>
/// Chooses DigiDollar inputs for an amount (research R15): the change must be exactly zero or between the $1.00 and
/// $100,000.00 per-output limits, so nothing is ever built that the network would reject. Prefers an exact match with
/// the fewest inputs, otherwise takes the largest outputs and finishes with the best-fitting one; if the leftover would be
/// under the minimum it adds one more input. Pure and deterministic: callers pass cents, not outputs.
/// </summary>
public static class DigiDollarCoinSelector
{
    /// <summary>Exact-subset search is exponential, so it only runs for this many candidates or fewer.</summary>
    private const int ExactSearchLimit = 18;

    public static DdSelection Select(IReadOnlyList<long> candidates, long amountCents)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (amountCents <= 0) throw new ArgumentOutOfRangeException(nameof(amountCents), "Amount must be positive.");

        const long min = DigiDollarTransferBuilder.MinAmountCents;
        const long max = DigiDollarTransferBuilder.MaxAmountCents;

        // An output so large that spending it leaves change above the maximum can never be part of a valid transfer.
        var usable = Enumerable.Range(0, candidates.Count)
            .Where(i => candidates[i] > 0 && candidates[i] <= amountCents + max)
            .ToList();
        var hasOversized = Enumerable.Range(0, candidates.Count).Any(i => candidates[i] > amountCents + max);

        if (usable.Sum(i => candidates[i]) < amountCents)
            return Fail(hasOversized ? DdBuildError.ChangeAboveMaximum : DdBuildError.InsufficientDd);

        // 1. An exact match with the fewest inputs (no change at all).
        if (usable.Count <= ExactSearchLimit && TryExact(candidates, usable, amountCents, out var exact))
            return new DdSelection(exact, null);

        // 2. Largest first; when one remaining output covers the need, take the smallest that does (least change).
        var picked = new List<int>();
        var remaining = usable.OrderByDescending(i => candidates[i]).ThenBy(i => i).ToList();
        long acc = 0;
        while (acc < amountCents)
        {
            var need = amountCents - acc;
            var fit = remaining.Where(i => candidates[i] >= need).OrderBy(i => candidates[i]).ThenBy(i => i).FirstOrDefault(-1);
            var next = fit >= 0 ? fit : remaining[0];
            picked.Add(next);
            remaining.Remove(next);
            acc += candidates[next];
        }

        var change = acc - amountCents;

        // 3. Change between 1 and 99 cents is not allowed: take one more input to make it valid.
        if (change is > 0 and < min)
        {
            if (remaining.Count == 0) return Fail(DdBuildError.ChangeBelowMinimum);
            var extra = remaining.OrderBy(i => candidates[i]).ThenBy(i => i).First();
            picked.Add(extra);
            change += candidates[extra];
        }

        if (change > max) return Fail(DdBuildError.ChangeAboveMaximum);
        return new DdSelection(picked, null);
    }

    private static DdSelection Fail(DdBuildError error) => new([], error);

    /// <summary>Fewest-inputs subset that sums exactly to the amount, if one exists.</summary>
    private static bool TryExact(IReadOnlyList<long> candidates, List<int> usable, long amount, out List<int> result)
    {
        result = [];
        var n = usable.Count;
        var bestCount = int.MaxValue;
        var bestMask = 0;

        for (var mask = 1; mask < (1 << n); mask++)
        {
            var count = System.Numerics.BitOperations.PopCount((uint)mask);
            if (count >= bestCount) continue;

            long sum = 0;
            for (var b = 0; b < n; b++)
                if ((mask & (1 << b)) != 0) sum += candidates[usable[b]];

            if (sum == amount)
            {
                bestCount = count;
                bestMask = mask;
            }
        }

        if (bestCount == int.MaxValue) return false;
        for (var b = 0; b < n; b++)
            if ((bestMask & (1 << b)) != 0) result.Add(usable[b]);
        return true;
    }
}
