using DigiByte.Crypto.Psbt;

namespace DigiByte.Web.Services;

public enum PsbtConfirmationStep
{
    Acknowledgment,
    SpendingLimit,
    Warnings,
    Authentication,
}

public sealed record PsbtConfirmationOutcome(bool Approved, PsbtConfirmationStep? FailedStep, string? Message)
{
    public static readonly PsbtConfirmationOutcome Approve = new(true, null, null);
}

/// <summary>
/// The fixed order of checks before a PSBT is signed or a signed one is broadcast (contracts/psbt-signing.md):
/// acknowledgment, spending limit, warnings, then biometrics or PIN. Each check is a delegate so the order can be tested
/// without the page, and the first that fails stops the rest. A spending-limit or warning check that can't run does not
/// block, as on the Send page; an authentication check that can't run gives no go-ahead.
/// </summary>
public sealed class PsbtConfirmationFlow(
    Func<decimal, Task<string?>> checkSpendingLimit,
    Func<ReviewSummary, Task<bool>> confirmWarnings,
    Func<Task<bool>> authenticate)
{
    private const decimal SatoshisPerDgb = 100_000_000m;

    /// <summary>The amount counted against the spending limit: what leaves the wallet, or zero when it gains overall.</summary>
    public static decimal SpendingAmountDgb(ReviewSummary review) => Math.Max(review.NetAmount, 0) / SatoshisPerDgb;

    public async Task<PsbtConfirmationOutcome> RunAsync(ReviewSummary review, bool acknowledged)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (review.NeedsAcknowledgment && !acknowledged)
            return new(false, PsbtConfirmationStep.Acknowledgment, "Confirm the warnings above before signing.");

        string? limitBlock = null;
        try
        {
            limitBlock = await checkSpendingLimit(SpendingAmountDgb(review));
        }
        catch (Exception)
        {
            // A limit check that can't run never blocks, as on the Send page.
        }
        if (limitBlock is not null)
            return new(false, PsbtConfirmationStep.SpendingLimit, limitBlock);

        bool warningsAccepted;
        try
        {
            warningsAccepted = await confirmWarnings(review);
        }
        catch (Exception)
        {
            warningsAccepted = true;
        }
        if (!warningsAccepted)
            return new(false, PsbtConfirmationStep.Warnings, null);

        bool authenticated;
        try
        {
            authenticated = await authenticate();
        }
        catch (Exception)
        {
            authenticated = false;
        }
        return authenticated
            ? PsbtConfirmationOutcome.Approve
            : new(false, PsbtConfirmationStep.Authentication, null);
    }
}
