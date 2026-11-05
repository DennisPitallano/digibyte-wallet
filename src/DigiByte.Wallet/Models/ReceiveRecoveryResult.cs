namespace DigiByte.Wallet.Models;

/// <summary>What a search for missing receive funds added to the wallet's scan, and the funds now on those addresses.</summary>
public record ReceiveRecoveryResult(int AddressesAdded, long Satoshis);
