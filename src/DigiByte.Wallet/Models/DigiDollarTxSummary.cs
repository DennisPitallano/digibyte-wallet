using DigiByte.Crypto.Transactions;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Models;

/// <summary>
/// What a DigiDollar (DD) transaction moved, read from its outputs: its kind and the DD amount of each DD output (USD cents), in output order.
/// A DD transaction moves no DGB apart from its network fee, so the list shows this instead of "-0.0000 DGB". It carries no addresses and
/// nothing about who the wallet paid: a transfer lists the amount sent and the amount returned as change side by side.
/// </summary>
public sealed class DigiDollarTxSummary
{
    public DigiDollarTxType Type { get; init; }

    /// <summary>The DD amount of each DD output in USD cents. A transfer usually has the amount sent and the change.</summary>
    public List<long> OutputCents { get; init; } = [];

    public long TotalCents => OutputCents.Sum();

    /// <summary>US dollars with two decimals, for example <c>$1.42</c>.</summary>
    public static string Usd(long cents) => "$" + (cents / 100m).ToString("N2");

    /// <summary>The DD amounts of the outputs, for example <c>$1.42 + $498.58</c>.</summary>
    public string OutputsText => string.Join(" + ", OutputCents.Select(Usd));

    /// <summary>A short name for the row and the detail view.</summary>
    public string Label => Type switch
    {
        DigiDollarTxType.Mint => "DigiDollar minted",
        DigiDollarTxType.Redeem => "DigiDollar redeemed",
        _ => "DigiDollar transfer",
    };

    /// <summary>
    /// Reads a DD summary from a transaction's outputs, or returns null for an ordinary transaction. A DD transaction has exactly one DD
    /// <c>OP_RETURN</c> (marker "DD", a type and the amounts) and value-0 DD outputs <c>OP_1 &lt;32-byte key&gt;</c>, the same rules the
    /// explorer parser uses. Anything else, including a text note that happens to contain "DD", is not a DD transaction.
    /// </summary>
    public static DigiDollarTxSummary? TryRead(IReadOnlyList<TxOutput> outputs)
    {
        List<long>? amounts = null;
        var type = DigiDollarTxType.None;
        var ddOutputs = 0;

        foreach (var output in outputs)
        {
            if (!TryScript(output.ScriptHex, out var script))
                continue;

            DigiDollarTxType t;
            List<long> a;
            if (DigiDollarScripts.TryParseOpReturn(script, out t, out a))
            {
            }
            else if (DigiDollarScripts.TryParseMintOpReturn(script, out var mintCents))
            {
                t = DigiDollarTxType.Mint;
                a = [mintCents];
            }
            else
            {
                if (output.AmountSatoshis == 0 && DigiDollarScripts.TryGetOutputKey(script, out _))
                    ddOutputs++;
                continue;
            }

            if (amounts is not null)
                return null; // more than one DD OP_RETURN
            amounts = a;
            type = t;
        }

        if (amounts is null || ddOutputs == 0)
            return null;
        // A mint carries only its own amount; a transfer has one amount per DD output.
        if (type != DigiDollarTxType.Mint && amounts.Count != ddOutputs)
            return null;

        return new DigiDollarTxSummary { Type = type, OutputCents = amounts };
    }

    private static bool TryScript(string? hex, out Script script)
    {
        script = Script.Empty;
        if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
            return false;
        try
        {
            script = new Script(Convert.FromHexString(hex));
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
