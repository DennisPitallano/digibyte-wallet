using DigiByte.P2P.Shared.Sponsors;
using Microsoft.Extensions.Options;

namespace DigiByte.Api.Services;

public sealed record DonationCheck(bool Ok, DonationRejection? Reason)
{
    public static readonly DonationCheck Passed = new(true, null);
    public static DonationCheck Rejected(DonationRejection reason) => new(false, reason);
}

/// <summary>No explorer could give a definite answer, so the donation could not be checked. The caller must fail, never skip the check.</summary>
public sealed class DonationLookupUnavailableException(string message) : Exception(message);

public interface IDonationVerifier
{
    /// <exception cref="DonationLookupUnavailableException">The explorers could not be reached.</exception>
    Task<DonationCheck> VerifyAsync(string txId, CancellationToken ct);
}

/// <summary>
/// Confirms that a transaction really paid the project: it exists, is at least <see cref="SponsorOptions.MinConfirmations"/> blocks deep,
/// and pays at least <see cref="SponsorOptions.MinDonationSatoshis"/> in total to the project's donation addresses. Explorers are tried
/// in order; the first one that has the transaction decides. See specs/006-sponsors-page/research.md (R15).
/// </summary>
public sealed class DonationVerifier(IEnumerable<IExplorerTxLookup> lookups, IOptions<SponsorOptions> options) : IDonationVerifier
{
    public async Task<DonationCheck> VerifyAsync(string txId, CancellationToken ct)
    {
        var settings = options.Value;
        DonationTx? found = null;
        var failures = 0;
        var asked = 0;

        foreach (var lookup in lookups)
        {
            asked++;
            try
            {
                found = await lookup.LookupAsync(txId, ct);
                if (found is not null) break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failures++;
            }
        }

        if (found is null)
        {
            // "Not found" is only trustworthy if no explorer failed to answer.
            if (asked == 0 || failures > 0)
                throw new DonationLookupUnavailableException("No explorer could confirm the transaction.");
            return DonationCheck.Rejected(DonationRejection.NotFound);
        }

        long paid = 0;
        foreach (var output in found.Outputs)
            if (IsProjectAddress(output.Address, settings.DonationAddresses))
                paid += output.Satoshis;

        if (paid == 0) return DonationCheck.Rejected(DonationRejection.WrongRecipient);
        if (paid < settings.MinDonationSatoshis) return DonationCheck.Rejected(DonationRejection.BelowMinimum);
        if (found.Confirmations < settings.MinConfirmations) return DonationCheck.Rejected(DonationRejection.TooFewConfirmations);
        return DonationCheck.Passed;
    }

    // Bech32 addresses are case-insensitive; Base58 addresses are not.
    private static bool IsProjectAddress(string address, string[] projectAddresses)
    {
        if (string.IsNullOrEmpty(address)) return false;
        foreach (var project in projectAddresses)
        {
            var comparison = project.StartsWith("dgb1", StringComparison.OrdinalIgnoreCase)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(address, project, comparison)) return true;
        }
        return false;
    }
}
