using NBitcoin;

namespace DigiByte.Wallet.Services;

/// <summary>A P2WPKH DGB output of the active wallet that can pay a DigiDollar transaction fee, with its signing key.</summary>
public sealed record DdFeeUtxo(string Txid, uint Vout, long ValueSats, int Confirmations, Key PrivateKey);

/// <summary>
/// The DGB side of a DigiDollar send: DigiDollar fees are paid in DGB (spec assumptions), from the wallet's own
/// DGB outputs. Implemented by <c>WalletService</c>; a seam so the DigiDollar service stays testable.
/// </summary>
public interface IDdFeeSource
{
    /// <summary>Spendable P2WPKH DGB outputs of the active seed-based wallet, with their keys.</summary>
    Task<IReadOnlyList<DdFeeUtxo>> GetFeeUtxosAsync(CancellationToken ct);

    /// <summary>The script that receives the leftover DGB after the fee.</summary>
    Task<Script> GetDgbChangeScriptAsync(CancellationToken ct);
}
