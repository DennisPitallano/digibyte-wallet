using NBitcoin;

namespace DigiByte.Crypto.Transactions;

/// <summary>
/// Selects UTXOs for transaction building using various strategies.
/// </summary>
public static class UtxoSelector
{
    /// <summary>
    /// Selects UTXOs using a simple largest-first strategy.
    /// Good for minimizing number of inputs (lower fees).
    /// </summary>
    public static List<Utxo> SelectLargestFirst(IEnumerable<Utxo> available, Money target)
    {
        var selected = new List<Utxo>();
        var total = Money.Zero;

        foreach (var utxo in available.OrderByDescending(u => u.Amount))
        {
            selected.Add(utxo);
            total += utxo.Amount;
            if (total >= target)
                break;
        }

        if (total < target)
            throw new InsufficientFundsException(target, total);

        return selected;
    }

    /// <summary>
    /// Selects UTXOs using smallest-first strategy.
    /// Good for consolidating small UTXOs (dust cleanup).
    /// </summary>
    public static List<Utxo> SelectSmallestFirst(IEnumerable<Utxo> available, Money target)
    {
        var selected = new List<Utxo>();
        var total = Money.Zero;

        foreach (var utxo in available.OrderBy(u => u.Amount))
        {
            selected.Add(utxo);
            total += utxo.Amount;
            if (total >= target)
                break;
        }

        if (total < target)
            throw new InsufficientFundsException(target, total);

        return selected;
    }

    /// <summary>
    /// Selects the single UTXO closest to the target amount to minimize change.
    /// Falls back to largest-first if no single UTXO is sufficient.
    /// </summary>
    public static List<Utxo> SelectClosestMatch(IEnumerable<Utxo> available, Money target)
    {
        var utxoList = available.ToList();

        // Try to find a single UTXO that covers the target with minimal change
        var bestSingle = utxoList
            .Where(u => u.Amount >= target)
            .OrderBy(u => u.Amount - target)
            .FirstOrDefault();

        if (bestSingle != null)
            return [bestSingle];

        return SelectLargestFirst(utxoList, target);
    }
}

public class InsufficientFundsException : Exception
{
    public Money Required { get; }
    public Money Available { get; }

    public InsufficientFundsException(Money required, Money available)
        : base($"Insufficient funds. Required: {required}, Available: {available}")
    {
        Required = required;
        Available = available;
    }
}
