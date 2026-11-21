using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Which addresses a send reads coins from first. <see cref="Planned"/> false means every address is in <see cref="Funded"/>, as
/// before spec 024.
/// </summary>
public sealed record SendCoinPlan(IReadOnlyList<string> Funded, IReadOnlyList<string> Rest, bool Planned);

/// <summary>
/// Reads coins only where the summaries show money (spec 024, FR-010). Pure. Coins are still read fresh by the caller; this only
/// orders the reads, and the caller reads <see cref="SendCoinPlan.Rest"/> before reporting insufficient funds.
/// </summary>
public static class SendCoinPlanner
{
    /// <summary>Margin above the amount before the rest is skipped: 0.01 DGB, well above any realistic DigiByte fee.</summary>
    public const long FeeAllowanceSatoshis = 1_000_000;

    /// <summary>
    /// Funded = the summary shows a confirmed balance or pending activity; Rest = the others. With no summaries, or a summary
    /// missing for any address, there is no plan and every address is read (FR-011).
    /// </summary>
    public static SendCoinPlan Plan(IReadOnlyList<string> addresses, IReadOnlyDictionary<string, AddressSummary>? summaries)
    {
        if (summaries is null || addresses.Any(a => !summaries.ContainsKey(a)))
            return new SendCoinPlan(addresses.ToList(), [], Planned: false);

        var funded = new List<string>();
        var rest = new List<string>();
        foreach (var address in addresses)
        {
            var s = summaries[address];
            if (s.Confirmed > 0 || s.PendingTxCount > 0 || s.PendingFunded > 0 || s.PendingSpent > 0)
                funded.Add(address);
            else
                rest.Add(address);
        }
        return new SendCoinPlan(funded, rest, Planned: true);
    }

    /// <summary>True when the coins found can't cover the amount plus the fee allowance and there are addresses left to read.</summary>
    public static bool NeedsRest(SendCoinPlan plan, long foundSatoshis, long amountSatoshis) =>
        plan.Rest.Count > 0 && foundSatoshis < amountSatoshis + FeeAllowanceSatoshis;

    /// <summary>Consolidate spends every coin: true when the coins found are less than the summaries' confirmed total.</summary>
    public static bool NeedsRestForConsolidate(SendCoinPlan plan, long foundSatoshis, long summariesConfirmedSatoshis) =>
        plan.Rest.Count > 0 && foundSatoshis < summariesConfirmedSatoshis;
}
