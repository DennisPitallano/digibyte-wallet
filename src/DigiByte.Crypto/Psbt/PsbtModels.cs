using NBitcoin;

namespace DigiByte.Crypto.Psbt;

/// <summary>Why a PSBT is refused, in the order they are checked (specs/007-psbt-signing/data-model.md). The first that applies wins.</summary>
public enum PsbtRefusalReason
{
    TooLarge = 1,
    NotAPsbt,
    InvalidStructure,
    UnsupportedDigiDollar,
    UnsupportedSignatureType,
    InputNotIdentified,
    UnsupportedTaproot,
    MissingPreviousTransaction,
    PreviousTransactionMismatch,
    AmountOrScriptMismatch,
    InputSpentOrUnknown,
    OutputsExceedInputs,

    /// <summary>Not an error: shown as a message, with no sign action.</summary>
    NothingToSign,
}

/// <summary>A refusal with a plain message and, where relevant, the input it concerns.</summary>
public sealed record PsbtRefusal(PsbtRefusalReason Reason, string Message, int? InputIndex = null);

public enum PsbtSourceFormat
{
    Text,
    Binary,
}

public enum ChainLookup
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>What the chain says about one input. Supplied to the analyzer, so the analyzer stays free of I/O.</summary>
/// <param name="IsUnspent">Checked only for this wallet's inputs; null when not checked.</param>
/// <param name="Confirmations">From the unspent list, when checked.</param>
public sealed record ChainInputFact(
    OutPoint Outpoint,
    ChainLookup Lookup,
    long? Amount = null,
    Script? Script = null,
    bool? IsUnspent = null,
    int? Confirmations = null);

public enum InputOwner
{
    Wallet,
    Other,

    /// <summary>No script in the PSBT, and the chain lookup failed or found nothing.</summary>
    Unidentified,
}

public enum InputKind
{
    NativeSegWit,
    Legacy,
    Other,
}

public enum AmountSource
{
    Chain,
    PsbtOnly,
    Unknown,
}

public enum SignatureState
{
    Unsigned,
    SignedByWallet,
    SignedByOthers,
    Finalized,
}

public sealed record InputAssessment(
    int Index,
    InputOwner Owner,
    InputKind Kind,
    long? Amount,
    AmountSource AmountSource,
    SignatureState SignatureState,
    bool IsUnconfirmed);

public enum OutputLabel
{
    Address,
    Data,
    NonStandard,
}

/// <param name="DataText">Readable text of a data output, when it has any.</param>
public sealed record OutputAssessment(int Index, long Amount, OutputLabel Label, string? Address, string? DataText, bool IsWallet);

public enum FeeStatus
{
    Verified,
    Unverified,
}

public enum PsbtWarning
{
    LockTime,
    UnconfirmedInput,
    NonStandardOutput,
    HighFee,
    UnverifiedFee,
    OutOfRangeNote,
    SharedOutputsNotChecked,
}

public enum ChainStatus
{
    Verified,

    /// <summary>Chain data for this wallet's inputs could not be read. Signing is blocked.</summary>
    Unavailable,
}

/// <summary>What the review shows, and whether signing or broadcasting is allowed.</summary>
public sealed record ReviewSummary
{
    public IReadOnlyList<InputAssessment> Inputs { get; init; } = [];
    public IReadOnlyList<OutputAssessment> Outputs { get; init; } = [];

    /// <summary>The spec's "total leaving this wallet": wallet inputs less wallet outputs. Negative means the wallet receives.</summary>
    public long NetAmount { get; init; }

    /// <summary>Known only when every input amount is known.</summary>
    public long? Fee { get; init; }

    public FeeStatus FeeStatus { get; init; } = FeeStatus.Unverified;
    public int InputsToSign { get; init; }
    public int OtherInputs { get; init; }
    public IReadOnlyList<PsbtWarning> Warnings { get; init; } = [];
    public PsbtRefusal? Refusal { get; init; }
    public ChainStatus ChainStatus { get; init; }
    public bool NeedsAcknowledgment { get; init; }
    public bool CanSign { get; init; }
    public bool IsComplete { get; init; }

    /// <summary>The PSBT arrived fully signed and can be broadcast after the confirmation flow (FR-034).</summary>
    public bool CanBroadcastSigned { get; init; }

    /// <summary>A review for input that could not be loaded at all.</summary>
    public static ReviewSummary Refused(PsbtRefusal refusal) => new() { Refusal = refusal };
}

/// <param name="SignedPsbtBase64">The export form: partial signatures, not finalized.</param>
/// <param name="FinalTransaction">The extracted transaction when complete, for broadcast.</param>
public sealed record SigningResult(string SignedPsbtBase64, int SignedCount, int RemainingCount, bool IsComplete, Transaction? FinalTransaction);

public sealed record BroadcastResult(string? TxId, string? Error)
{
    public bool Succeeded => TxId is not null;
}

/// <summary>The outcome of trying to finalize a copy of a PSBT.</summary>
public sealed record PsbtCompletion(bool IsComplete, Transaction? Transaction, IReadOnlyList<string> Errors);
