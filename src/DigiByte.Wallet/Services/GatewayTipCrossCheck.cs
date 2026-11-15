namespace DigiByte.Wallet.Services;

public enum CrossCheckResult
{
    /// <summary>Every block the explorer answered for is the same block in the wallet own headers.</summary>
    Agrees,

    /// <summary>The explorer names a different block than the wallet header chain for a settled height: the chain the gateway served is suspect.</summary>
    Mismatch,

    /// <summary>The explorer gave no usable answer (or only disagreed at the very tip, which is how a one-block race looks): neither promote nor demote.</summary>
    Inconclusive,
}

/// <summary>
/// Compares the wallet verified header chain with an independent explorer at the tip and at 1, 10 and 100 blocks below it (spec FR-013,
/// T054, T067). This is the defense against a gateway fed a false chain by an eclipsed peer set: the wallet checks every header itself,
/// but a chain with real work built on a hidden fork can still pass those checks, and only another view of the network shows it. A
/// disagreement at the tip alone is treated as a race between two honest views; a disagreement at any settled height is a mismatch.
/// </summary>
public sealed class GatewayTipCrossCheck(GatewayHeaderStore headers, IExplorerBlockHashSource explorers)
{
    public static readonly int[] Offsets = [0, 1, 10, 100];

    public async Task<CrossCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await headers.EnsureInitializedAsync();
        var tip = headers.TipHeight;
        var first = tip - headers.Count + 1; // the lowest height the wallet holds a header for (after the checkpoint)

        var answered = 0;
        var tipDisagrees = false;
        foreach (var offset in Offsets)
        {
            var height = tip - offset;
            if (height < first || headers.HeaderAt(height) is not { } own)
                continue;
            var theirs = await explorers.GetBlockHashAsync(height, cancellationToken);
            if (theirs is null)
                continue; // no answer for this height (an explorer a block behind has none for the tip): skip it
            answered++;
            if (string.Equals(theirs, own.Hash.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (offset == 0)
            {
                tipDisagrees = true;
                continue;
            }
            return CrossCheckResult.Mismatch;
        }

        if (answered == 0 || (tipDisagrees && answered == 1))
            return CrossCheckResult.Inconclusive;
        return tipDisagrees ? CrossCheckResult.Inconclusive : CrossCheckResult.Agrees;
    }
}
