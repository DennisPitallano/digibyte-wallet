namespace DigiByte.Api.Services;

/// <summary>
/// Settings for the sponsor submission endpoint (section "Sponsors"). Everything here is public configuration except
/// <see cref="GitHubToken"/>, which is read from the environment variable <c>Sponsors__GitHubToken</c> only and must never
/// be logged, returned, or committed.
/// </summary>
public sealed class SponsorOptions
{
    public string Repository { get; set; } = "DennisPitallano/digibyte-wallet";
    public string Branch { get; set; } = "sponsors";
    public int MaxOpenSubmissions { get; set; } = 50;
    public int PerVisitorPerHour { get; set; } = 3;
    public string[] DonationAddresses { get; set; } = [];
    public int MinConfirmations { get; set; } = 3;
    public long MinDonationSatoshis { get; set; } = 100_000_000;

    /// <summary>Bot token. Secret. Empty means submissions fail with "unavailable".</summary>
    public string? GitHubToken { get; set; }
}
