namespace DigiByte.P2P.Shared.Sponsors;

/// <summary>Whether a submission adds an entry or asks for one to be removed.</summary>
public enum SponsorSubmissionType { Add, Remove }

/// <summary>Outcome of a sponsor submission as the wallet shows it. See contracts/submission-api.md.</summary>
public enum SponsorSubmissionState
{
    Created,
    Duplicate,
    Invalid,
    NotVerified,
    AlreadyUsed,
    NameTaken,
    RateLimited,
    QueueFull,
    Unavailable,
}

/// <summary>Why the donation check failed (carried with <see cref="SponsorSubmissionState.NotVerified"/>).</summary>
public enum DonationRejection { NotFound, TooFewConfirmations, WrongRecipient, BelowMinimum }

/// <summary>Response body of POST /api/sponsors/submissions. Never carries the token, GitHub details or submitted content.</summary>
public sealed class SponsorSubmissionResult
{
    public SponsorSubmissionState State { get; init; }

    /// <summary>For <see cref="SponsorSubmissionState.Invalid"/>: the request field names that failed (for example "name", "txid", "consent").</summary>
    public string[]? Fields { get; init; }

    /// <summary>For <see cref="SponsorSubmissionState.NotVerified"/>: the reason.</summary>
    public DonationRejection? Reason { get; init; }
}
