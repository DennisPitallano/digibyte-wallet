using System.Numerics;
using NBitcoin;

namespace DigiByte.Crypto.Headers;

/// <summary>
/// The compact "bits" form of a proof-of-work target, and the comparison of a hash against it.
/// Follows Bitcoin Core's <c>arith_uint256::SetCompact</c> and <c>GetCompact</c>, which DigiByte Core uses unchanged.
/// </summary>
public static class CompactTarget
{
    /// <summary>Converts compact bits to the full target. Ignores the sign and overflow flags; use <see cref="TryToTarget"/> to check them.</summary>
    public static BigInteger ToBigInteger(uint compact) => Decode(compact, out _, out _);

    private static BigInteger Decode(uint compact, out bool negative, out bool overflow)
    {
        var size = (int)(compact >> 24);
        var word = compact & 0x007fffff;
        BigInteger value;
        if (size <= 3)
        {
            word >>= 8 * (3 - size);
            value = word;
        }
        else
        {
            value = (BigInteger)word << (8 * (size - 3));
        }
        negative = word != 0 && (compact & 0x00800000) != 0;
        overflow = word != 0 && (size > 34 || (word > 0xff && size > 33) || (word > 0xffff && size > 32));
        return value;
    }

    /// <summary>Converts a target to compact bits, rounding down to what the format can hold.</summary>
    public static uint ToCompact(BigInteger value)
    {
        if (value.Sign <= 0)
            return 0;
        var bytes = (int)((value.GetBitLength() + 7) / 8);
        ulong compact;
        if (bytes <= 3)
            compact = (ulong)value << (8 * (3 - bytes));
        else
            compact = (ulong)(value >> (8 * (bytes - 3)));

        // The 0x00800000 bit is the sign bit, so shift a size up instead of setting it.
        if ((compact & 0x00800000) != 0)
        {
            compact >>= 8;
            bytes++;
        }
        return (uint)compact | ((uint)bytes << 24);
    }

    /// <summary>
    /// Checks that the bits describe a valid target: not negative, not zero, not overflowing, and not easier than the network's limit.
    /// </summary>
    public static bool TryToTarget(uint bits, BigInteger powLimit, out BigInteger target)
    {
        target = Decode(bits, out var negative, out var overflow);
        return !(negative || overflow || target.IsZero || target > powLimit);
    }

    /// <summary>True when the proof-of-work hash is at or below the target the bits claim.</summary>
    public static bool MeetsTarget(uint256 powHash, uint bits, BigInteger powLimit)
    {
        if (!TryToTarget(bits, powLimit, out var target))
            return false;
        // uint256 keeps its bytes in little-endian order, which is the numeric order the comparison needs.
        var value = new BigInteger(powHash.ToBytes(), isUnsigned: true, isBigEndian: false);
        return value <= target;
    }
}
