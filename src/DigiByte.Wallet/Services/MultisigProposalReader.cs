using DigiByte.Wallet.Models;
using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>The facts of a multisig payment, read from its PSBT. <see cref="Problems"/> empty means it is safe to show and sign.</summary>
public sealed record ProposalFacts
{
    public string UnsignedTxId { get; init; } = "";
    public IReadOnlyList<(OutPoint OutPoint, long ValueSat)> Inputs { get; init; } = [];
    public string DestinationAddress { get; init; } = "";
    public long AmountSat { get; init; }
    public long ChangeSat { get; init; }
    public long FeeSat { get; init; }
    /// <summary>The note written into the transaction itself (OP_RETURN), if any.</summary>
    public string? Memo { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// Reads destination, amount, change, fee and memo from a multisig PSBT, checked against the local multisig
/// (specs/017-multisig-online-signing, research R5). Nothing a signing room says about a payment is trusted: what is shown
/// and signed comes from here. Supports the shape the wallet's Send page creates: one recipient, optional change back to the
/// multisig, optional memo.
/// </summary>
public static class MultisigProposalReader
{
    public static ProposalFacts Read(string? psbtBase64, MultisigWalletConfig config, Network network)
    {
        PSBT psbt;
        try { psbt = PSBT.Parse((psbtBase64 ?? "").Trim(), network); }
        catch { return new ProposalFacts { Problems = ["This is not a valid transaction (PSBT)."] }; }
        return Read(psbt, config, network);
    }

    public static ProposalFacts Read(PSBT psbt, MultisigWalletConfig config, Network network)
    {
        var problems = new List<string>();
        var tx = psbt.GetGlobalTransaction();
        Script multisigScript;
        try { multisigScript = BitcoinAddress.Create(config.Address, network).ScriptPubKey; }
        catch { return new ProposalFacts { Problems = ["This multisig's address is not valid on this network."] }; }

        var inputs = new List<(OutPoint, long)>();
        long totalIn = 0;
        var missingAmount = false;
        for (var i = 0; i < psbt.Inputs.Count; i++)
        {
            var spent = psbt.Inputs[i].WitnessUtxo;
            if (spent is null)
            {
                missingAmount = true;
                continue;
            }
            if (spent.ScriptPubKey != multisigScript)
                problems.Add("This payment spends coins that are not this multisig's.");
            inputs.Add((tx.Inputs[i].PrevOut, spent.Value.Satoshi));
            totalIn += spent.Value.Satoshi;
        }
        if (missingAmount)
            problems.Add("This payment is missing the amount of a coin it spends.");
        if (psbt.Inputs.Count == 0)
            problems.Add("This payment spends no coins.");

        string? memo = null;
        long change = 0, totalOut = 0;
        var external = new List<TxOut>();
        foreach (var output in tx.Outputs)
        {
            totalOut += output.Value.Satoshi;
            if (TxNullDataTemplate.Instance.CheckScriptPubKey(output.ScriptPubKey))
            {
                var data = TxNullDataTemplate.Instance.ExtractScriptPubKeyParameters(output.ScriptPubKey);
                if (data is { Length: > 0 })
                {
                    try { memo = System.Text.Encoding.UTF8.GetString(data.SelectMany(b => b).ToArray()); }
                    catch { memo = null; }
                }
            }
            else if (output.ScriptPubKey == multisigScript)
                change += output.Value.Satoshi;
            else
                external.Add(output);
        }

        string destination = "";
        long amount = 0;
        if (external.Count == 0)
            problems.Add("This payment has no recipient.");
        else if (external.Count > 1)
            problems.Add("This payment pays more than one recipient, which online signing does not support.");
        else
        {
            amount = external[0].Value.Satoshi;
            var address = external[0].ScriptPubKey.GetDestinationAddress(network);
            if (address is null)
                problems.Add("The recipient's address can't be read on this network.");
            else
                destination = address.ToString();
        }

        var fee = totalIn - totalOut;
        if (!missingAmount && fee <= 0)
            problems.Add("This payment's outputs are not covered by its coins, so it pays no fee.");

        return new ProposalFacts
        {
            UnsignedTxId = tx.GetHash().ToString(),
            Inputs = inputs,
            DestinationAddress = destination,
            AmountSat = amount,
            ChangeSat = change,
            FeeSat = missingAmount ? 0 : fee,
            Memo = string.IsNullOrWhiteSpace(memo) ? null : memo,
            Problems = problems.Distinct().ToList(),
        };
    }
}
