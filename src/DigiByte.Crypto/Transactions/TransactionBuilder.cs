using DigiByte.Crypto.Networks;
using NBitcoin;

namespace DigiByte.Crypto.Transactions;

/// <summary>
/// Builds and signs DigiByte transactions.
/// </summary>
public class DigiByteTransactionBuilder
{
    private readonly Network _network;

    public DigiByteTransactionBuilder(Network? network = null)
    {
        _network = network ?? DigiByteNetwork.Mainnet;
    }

    /// <summary>
    /// Builds a simple send transaction with an optional OP_RETURN memo.
    /// </summary>
    public Transaction BuildSendTransaction(
        IEnumerable<Utxo> utxos,
        BitcoinAddress destination,
        Money amount,
        BitcoinAddress changeAddress,
        FeeRate feeRate,
        string? memo = null)
    {
        var builder = _network.CreateTransactionBuilder();
        // Disable NBitcoin's dust prevention — it uses Bitcoin's dust relay fee
        // which incorrectly rejects OP_RETURN outputs and doesn't match DigiByte's policy.
        builder.DustPrevention = false;

        foreach (var utxo in utxos)
        {
            builder.AddCoins(utxo.ToCoin());
            builder.AddKeys(utxo.PrivateKey);
        }

        builder.Send(destination, amount);

        // Embed memo as an OP_RETURN output (max 80 bytes, unspendable)
        if (!string.IsNullOrWhiteSpace(memo))
        {
            var memoBytes = System.Text.Encoding.UTF8.GetBytes(memo);
            if (memoBytes.Length > 80)
                memoBytes = memoBytes[..80];
            var opReturnScript = TxNullDataTemplate.Instance.GenerateScriptPubKey(memoBytes);
            builder.Send(opReturnScript, Money.Zero);
        }

        builder.SetChange(changeAddress);
        builder.SendEstimatedFees(feeRate);

        return builder.BuildTransaction(sign: true);
    }

    /// <summary>
    /// Builds a transaction with multiple outputs.
    /// </summary>
    public Transaction BuildMultiOutputTransaction(
        IEnumerable<Utxo> utxos,
        IEnumerable<(BitcoinAddress Address, Money Amount)> outputs,
        BitcoinAddress changeAddress,
        FeeRate feeRate)
    {
        var builder = _network.CreateTransactionBuilder();

        foreach (var utxo in utxos)
        {
            builder.AddCoins(utxo.ToCoin());
            builder.AddKeys(utxo.PrivateKey);
        }

        foreach (var (address, amount) in outputs)
        {
            builder.Send(address, amount);
        }

        builder.SetChange(changeAddress);
        builder.SendEstimatedFees(feeRate);

        return builder.BuildTransaction(sign: true);
    }

    /// <summary>
    /// Estimates the fee for a transaction without signing.
    /// </summary>
    public Money EstimateFee(
        int inputCount,
        int outputCount,
        FeeRate feeRate,
        ScriptPubKeyType inputType = ScriptPubKeyType.Segwit)
    {
        // Approximate vbytes for SegWit: ~68 per input, ~31 per output, ~10 overhead
        int estimatedVBytes = inputType switch
        {
            ScriptPubKeyType.Segwit => 10 + (inputCount * 68) + (outputCount * 31),
            ScriptPubKeyType.Legacy => 10 + (inputCount * 148) + (outputCount * 34),
            _ => 10 + (inputCount * 68) + (outputCount * 31)
        };

        return feeRate.GetFee(estimatedVBytes);
    }

    /// <summary>
    /// Verifies a transaction is properly signed and valid.
    /// </summary>
    public bool Verify(Transaction transaction, IEnumerable<ICoin> spentCoins)
    {
        var builder = _network.CreateTransactionBuilder();
        builder.AddCoins(spentCoins);
        return builder.Verify(transaction);
    }
}

/// <summary>
/// Represents an unspent transaction output with its private key for signing.
/// </summary>
public class Utxo
{
    public required uint256 TransactionId { get; init; }
    public required uint OutputIndex { get; init; }
    public required Money Amount { get; init; }
    public required Script ScriptPubKey { get; init; }
    public required Key PrivateKey { get; init; }
    public int Confirmations { get; init; }

    public Coin ToCoin()
    {
        return new Coin(
            new OutPoint(TransactionId, OutputIndex),
            new TxOut(Amount, ScriptPubKey));
    }
}
