using NBitcoin;

namespace DigiByte.Crypto.Transactions;

/// <summary>DigiDollar transaction types (DigiByte Core <c>DigiDollarTxType</c>).</summary>
public enum DigiDollarTxType : byte
{
    None = 0,
    Mint = 1,
    Transfer = 2,
    Redeem = 3,
}

/// <summary>
/// On-chain codec for DigiDollar (DD) transactions, mirroring DigiByte Core
/// (<c>src/primitives/transaction.h</c>, <c>src/digidollar/txbuilder.cpp</c>, <c>validation.cpp</c>);
/// see research R1.
///
/// - nVersion = (type &lt;&lt; 24) | (flags &lt;&lt; 16) | 0x0770. A transaction is DD when the low 16 bits are 0x0770.
/// - A DD output has value 0 and the canonical P2TR script <c>OP_1 &lt;32-byte key&gt;</c>; the key is the tweaked
///   OUTPUT key the DD address encodes. There is no script tree and no amount in the script.
/// - Amounts (USD cents) live in exactly one <c>OP_RETURN</c>:
///   <c>OP_RETURN "DD" CScriptNum(type) CScriptNum(cents)...</c>, one amount per DD output in output order.
///   Each element is a data push of the minimal sign-magnitude little-endian encoding.
/// </summary>
public static class DigiDollarScripts
{
    /// <summary>Core's <c>DD_TX_VERSION</c> marker constant.</summary>
    public const uint DdTxVersion = 0x0D1D0770;

    /// <summary>Core's <c>DD_VERSION_MASK</c>: only the low 16 bits identify a DD transaction.</summary>
    public const uint DdVersionMask = 0x0000FFFF;

    private const int KeyLength = 32;
    private const int MaxScriptNumBytes = 8;
    private static readonly byte[] Marker = "DD"u8.ToArray();

    // ---------- version ----------

    public static uint MakeVersion(DigiDollarTxType type, byte flags = 0) =>
        ((uint)type << 24) | ((uint)flags << 16) | (DdTxVersion & DdVersionMask);

    public static bool IsDigiDollarVersion(uint version) =>
        (version & DdVersionMask) == (DdTxVersion & DdVersionMask);

    public static DigiDollarTxType GetTxType(uint version) => (DigiDollarTxType)(byte)(version >> 24);

    // ---------- output script ----------

    /// <summary>The DD output script <c>OP_1 &lt;32-byte output key&gt;</c> (value 0).</summary>
    public static Script CreateOutputScript(ReadOnlySpan<byte> outputKey)
    {
        if (outputKey.Length != KeyLength)
            throw new ArgumentException($"DigiDollar output key must be {KeyLength} bytes.", nameof(outputKey));
        return new Script(OpcodeType.OP_1, Op.GetPushOp(outputKey.ToArray()));
    }

    /// <summary>True (with the 32-byte key) when the script is canonical P2TR: witness v1, 32-byte program.</summary>
    public static bool TryGetOutputKey(Script script, out byte[] outputKey)
    {
        outputKey = [];
        var bytes = script.ToBytes();
        // 0x51 (OP_1) 0x20 (push 32) <32 bytes>
        if (bytes.Length != 34 || bytes[0] != 0x51 || bytes[1] != 0x20) return false;
        outputKey = bytes[2..];
        return true;
    }

    // ---------- OP_RETURN ----------

    /// <summary>Build <c>OP_RETURN "DD" CScriptNum(type) CScriptNum(cents)...</c> exactly as Core's txbuilder does.</summary>
    public static Script BuildOpReturn(DigiDollarTxType type, IReadOnlyList<long> amountsCents)
    {
        ArgumentNullException.ThrowIfNull(amountsCents);
        if (type == DigiDollarTxType.None)
            throw new ArgumentException("A DigiDollar OP_RETURN needs a transaction type.", nameof(type));
        if (amountsCents.Count == 0)
            throw new ArgumentException("A DigiDollar OP_RETURN needs at least one amount.", nameof(amountsCents));

        var bytes = new List<byte> { 0x6a }; // OP_RETURN
        AppendPush(bytes, Marker);
        AppendPush(bytes, EncodeScriptNum((long)type));
        foreach (var cents in amountsCents)
        {
            if (cents <= 0) throw new ArgumentOutOfRangeException(nameof(amountsCents), "Amounts must be positive.");
            AppendPush(bytes, EncodeScriptNum(cents));
        }
        return new Script(bytes.ToArray());
    }

    /// <summary>
    /// Parse a DD OP_RETURN into its type and amounts. Strict about structure (marker, type, at least one
    /// positive amount, pushes only) but tolerant of small-integer opcodes (OP_1..OP_16) used as pushes.
    /// </summary>
    public static bool TryParseOpReturn(Script script, out DigiDollarTxType type, out List<long> amountsCents)
    {
        type = DigiDollarTxType.None;
        amountsCents = [];

        List<Op> ops;
        try { ops = script.ToOps().ToList(); }
        catch { return false; }

        if (ops.Count < 4) return false; // OP_RETURN, marker, type, >= 1 amount
        if (ops[0].Code != OpcodeType.OP_RETURN) return false;

        if (ops[1].PushData is not { } marker || !marker.AsSpan().SequenceEqual(Marker)) return false;

        if (!TryReadNumber(ops[2], out var typeValue) || typeValue is < 1 or > 255) return false;

        var amounts = new List<long>();
        for (var i = 3; i < ops.Count; i++)
        {
            if (!TryReadNumber(ops[i], out var cents) || cents <= 0) return false;
            amounts.Add(cents);
        }

        type = (DigiDollarTxType)(byte)typeValue;
        amountsCents = amounts;
        return true;
    }

    /// <summary>
    /// Parse a MINT <c>OP_RETURN</c>: <c>"DD" CScriptNum(1) CScriptNum(ddCents)</c> followed by extra pushes
    /// (lock height, tier, a 32-byte commitment) that this wallet does not interpret. Only the DigiDollar amount,
    /// the first number after the type, is read. Observed on mainnet blocks (research R18).
    /// </summary>
    public static bool TryParseMintOpReturn(Script script, out long ddCents)
    {
        ddCents = 0;

        List<Op> ops;
        try { ops = script.ToOps().ToList(); }
        catch { return false; }

        if (ops.Count < 4 || ops[0].Code != OpcodeType.OP_RETURN) return false;
        if (ops[1].PushData is not { } marker || !marker.AsSpan().SequenceEqual(Marker)) return false;
        if (!TryReadNumber(ops[2], out var typeValue) || typeValue != (long)DigiDollarTxType.Mint) return false;
        if (!TryReadNumber(ops[3], out var cents) || cents <= 0) return false;

        ddCents = cents;
        return true;
    }

    // ---------- CScriptNum ----------

    private static byte[] EncodeScriptNum(long value)
    {
        if (value == 0) return [];
        var negative = value < 0;
        var magnitude = (ulong)Math.Abs(value);
        var result = new List<byte>();
        while (magnitude != 0)
        {
            result.Add((byte)(magnitude & 0xff));
            magnitude >>= 8;
        }
        if ((result[^1] & 0x80) != 0)
            result.Add(negative ? (byte)0x80 : (byte)0x00);
        else if (negative)
            result[^1] |= 0x80;
        return result.ToArray();
    }

    private static bool TryReadNumber(Op op, out long value)
    {
        value = 0;
        var code = op.Code;

        // Small-integer opcodes: OP_0 is an empty push, OP_1..OP_16 are 1..16.
        if (code == OpcodeType.OP_0) return true;
        if (code >= OpcodeType.OP_1 && code <= OpcodeType.OP_16)
        {
            value = (int)code - (int)OpcodeType.OP_1 + 1;
            return true;
        }

        if (op.PushData is not { } data) return false; // not a push
        if (data.Length > MaxScriptNumBytes) return false;
        if (data.Length == 0) return true;

        ulong magnitude = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (i == data.Length - 1) b &= 0x7f;
            magnitude |= (ulong)b << (8 * i);
        }
        if (magnitude > long.MaxValue) return false;
        var negative = (data[^1] & 0x80) != 0;
        value = negative ? -(long)magnitude : (long)magnitude;
        return true;
    }

    private static void AppendPush(List<byte> script, byte[] data)
    {
        // Data pushes only: lengths here are always < 0x4c, so a single length byte is the canonical push.
        // (An empty push would be OP_0; amounts are always positive so it never occurs.)
        script.Add((byte)data.Length);
        script.AddRange(data);
    }
}
