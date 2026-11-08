using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>An address a user asked to sweep, as the planner sees it: amounts and flags only, never a key.</summary>
/// <param name="AmountSatoshis">Confirmed plain-DGB funds on the address.</param>
/// <param name="InputCount">How many outputs make up that amount, which sets the transaction size and so the fee.</param>
/// <param name="InFlight">A sweep of this address is already Sending or Pending.</param>
/// <param name="LookupFailed">The address could not be re-checked just now, so its funds are unknown.</param>
public sealed record DigiIdSweepCandidate(
    string Domain, string SourceAddress, long AmountSatoshis, int InputCount, bool InFlight = false, bool LookupFailed = false);

/// <summary>
/// Decides which Digi-ID addresses are worth sweeping, what each sweep would cost, and which fresh receive address each pays to
/// (spec 009). Pure: no key, no network, no storage. The wallet service reads the chain and the keys; this decides.
/// </summary>
public static class DigiIdSweepPlanner
{
    /// <summary>The wallet never sends below this rate (DigiByte's minimum relay fee).</summary>
    public const int MinFeeRateSatPerVByte = 100;

    /// <summary>Funds that would arrive below this (0.0001 DGB), after the fee, are not worth moving.</summary>
    public const long MinNetSatoshis = 10_000;

    /// <summary>
    /// Most addresses one confirmation sweeps: one fewer than the wallet's unused-address limit, so that after the sweep the
    /// Receive page can show a fresh unused address instead of one that now holds swept funds.
    /// </summary>
    public static int MaxItemsPerRound => WalletService.ReceiveGapLimit - 1;

    /// <summary>The rate that will really be used for a requested one: never below the floor.</summary>
    public static int EffectiveFeeRate(int requestedSatPerVByte) => Math.Max(requestedSatPerVByte, MinFeeRateSatPerVByte);

    /// <summary>Estimated fee for sweeping an address with <paramref name="inputCount"/> Legacy outputs into one output, no change.</summary>
    public static long EstimateFee(int inputCount, int feeRateSatPerVByte)
    {
        var rate = new FeeRate(Money.Satoshis(EffectiveFeeRate(feeRateSatPerVByte) * 1000L));
        return new DigiByteTransactionBuilder().EstimateFee(Math.Max(inputCount, 1), 1, rate, ScriptPubKeyType.Legacy).Satoshi;
    }

    /// <summary>
    /// Builds the plan. An address is left out, with a reason, if its sweep is already in flight, its funds could not be
    /// re-checked, nothing is confirmed, or what would arrive is below <see cref="MinNetSatoshis"/>. Of the rest, the first
    /// <paramref name="maxItems"/> are planned, each paying to the next of <paramref name="destinations"/>, and the others are
    /// counted in <see cref="DigiIdSweepPlan.Remaining"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer destinations than planned items, or two destinations alike.</exception>
    public static DigiIdSweepPlan Build(IReadOnlyList<DigiIdSweepCandidate> candidates, int feeRateSatPerVByte, int maxItems,
        IReadOnlyList<(int Index, string Address)> destinations)
    {
        var rate = EffectiveFeeRate(feeRateSatPerVByte);

        if (destinations.Select(d => d.Address).Distinct(StringComparer.Ordinal).Count() != destinations.Count)
            throw new ArgumentException("Two destinations are the same address.", nameof(destinations));

        var excluded = new List<DigiIdSweepExclusion>();
        var eligible = new List<(DigiIdSweepCandidate Candidate, long Fee, long Net)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in candidates)
        {
            if (!seen.Add(c.SourceAddress)) continue;

            if (c.InFlight)
            {
                excluded.Add(new DigiIdSweepExclusion(c.Domain, c.SourceAddress, c.AmountSatoshis, DigiIdSweepExclusionKind.InFlight,
                    "A sweep of this address is already in progress."));
                continue;
            }

            if (c.LookupFailed)
            {
                excluded.Add(new DigiIdSweepExclusion(c.Domain, c.SourceAddress, c.AmountSatoshis, DigiIdSweepExclusionKind.LookupFailed,
                    "Could not check this address just now. Try again."));
                continue;
            }

            if (c.AmountSatoshis <= 0)
            {
                excluded.Add(new DigiIdSweepExclusion(c.Domain, c.SourceAddress, c.AmountSatoshis, DigiIdSweepExclusionKind.NothingConfirmed,
                    "Nothing is confirmed yet. Pending funds can be swept once they confirm."));
                continue;
            }

            var fee = EstimateFee(c.InputCount, rate);
            var net = c.AmountSatoshis - fee;
            if (net < MinNetSatoshis)
            {
                excluded.Add(new DigiIdSweepExclusion(c.Domain, c.SourceAddress, c.AmountSatoshis, DigiIdSweepExclusionKind.TooSmall,
                    "Too small to sweep: after the network fee almost nothing would arrive."));
                continue;
            }

            eligible.Add((c, fee, net));
        }

        var take = Math.Max(0, Math.Min(maxItems, eligible.Count));
        if (destinations.Count < take)
            throw new ArgumentException($"{take} addresses to sweep need {take} destinations, but only {destinations.Count} were given.", nameof(destinations));

        var items = new List<DigiIdSweepPlanItem>(take);
        for (var i = 0; i < take; i++)
        {
            var (c, fee, net) = eligible[i];
            items.Add(new DigiIdSweepPlanItem
            {
                Domain = c.Domain,
                SourceAddress = c.SourceAddress,
                AmountSatoshis = c.AmountSatoshis,
                EstimatedFeeSatoshis = fee,
                NetSatoshis = net,
                DestinationAddress = destinations[i].Address,
                DestinationIndex = destinations[i].Index,
            });
        }

        return new DigiIdSweepPlan
        {
            Items = items,
            Remaining = eligible.Count - take,
            Excluded = excluded,
            FeeRateSatPerVByte = rate,
        };
    }
}
