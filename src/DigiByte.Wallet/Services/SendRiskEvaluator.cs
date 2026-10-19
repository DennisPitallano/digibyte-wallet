namespace DigiByte.Wallet.Services;

/// <summary>What a pending send looks like to Baku, the guardian mascot.</summary>
public readonly record struct SendRiskAssessment(bool LargeAmount, bool NewAddress)
{
    public bool AnyRisk => LargeAmount || NewAddress;
}

/// <summary>
/// Decides when a send deserves a guardian warning: half or more of the balance, or a destination the user
/// has never dealt with. Pure and deterministic: no I/O and no address validation (callers validate first).
/// Applies equally to DGB sends and DigiDollar sends; pass amounts and balances in the same unit.
/// </summary>
public static class SendRiskEvaluator
{
    /// <summary>Share of the balance at or above which a send counts as large (the network fee is not included).</summary>
    public const decimal LargeAmountShare = 0.5m;

    public static SendRiskAssessment Evaluate(
        decimal amount, decimal balance, string destination, IReadOnlySet<string> knownAddresses) =>
        new(IsLarge(amount, balance), IsNew(destination, knownAddresses));

    /// <summary>A batch send: large judged on the total, new if any recipient is unknown.</summary>
    public static SendRiskAssessment EvaluateBatch(
        IEnumerable<string> destinations, decimal total, decimal balance, IReadOnlySet<string> knownAddresses) =>
        new(IsLarge(total, balance), destinations.Any(d => IsNew(d, knownAddresses)));

    private static bool IsLarge(decimal amount, decimal balance) =>
        balance > 0 && amount > 0 && amount >= balance * LargeAmountShare;

    private static bool IsNew(string? destination, IReadOnlySet<string> knownAddresses)
    {
        var key = KnownAddressProvider.Normalize(destination);
        return key.Length > 0 && !knownAddresses.Contains(key);
    }
}
