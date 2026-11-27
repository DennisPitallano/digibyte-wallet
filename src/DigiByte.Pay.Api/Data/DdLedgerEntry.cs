namespace DigiByte.Pay.Api.Data;

/// <summary>
/// One append-only movement of DD held for an owner (a store, or a merchant when <see cref="StoreId"/> is null).
/// Amounts are positive; <see cref="Type"/> gives the sign (specs/034 data-model).
/// </summary>
public class DdLedgerEntry
{
    public const string TypeCredit = "credit";
    public const string TypeDebit = "debit";
    public const string TypeDebitReversal = "debit_reversal";
    public const string TypeTransferOut = "transfer_out";
    public const string TypeTransferIn = "transfer_in";
    public const string TypeReversalAbsorbed = "reversal_absorbed";

    public const string SourcePayment = "payment";
    public const string SourceOverpayment = "overpayment";
    public const string SourceLate = "late";
    public const string SourceExtraRefund = "extra_refund";
    public const string SourceDonation = "donation";

    public required string Id { get; init; }
    public required string MerchantId { get; init; }
    public string? StoreId { get; set; }
    public required string Type { get; init; }
    /// <summary>Credits only: payment | overpayment | late | extra_refund | donation.</summary>
    public string? Source { get; set; }
    public required long AmountCents { get; init; }
    /// <summary>Credits: the receive transaction. Unique together with <see cref="Address"/>.</summary>
    public string? Txid { get; init; }
    public string? Address { get; init; }
    public string? SessionId { get; init; }
    /// <summary>Debits, debit reversals and transfers: the payout or the move they belong to.</summary>
    public string? PayoutId { get; init; }
    public int Confirmations { get; set; }
    /// <summary>When confirmations first reached the payout threshold; from then the credit can be paid out.</summary>
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>Credits only: set when the receive disappeared from the chain.</summary>
    public DateTime? ReversedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
