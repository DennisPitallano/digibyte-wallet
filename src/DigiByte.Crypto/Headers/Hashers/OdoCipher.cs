using System.Buffers.Binary;
using System.Numerics;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// The Odocrypt block cipher, a port of <c>OdoCrypt</c> in DigiByte Core's <c>crypto/odocrypt.cpp</c>. It encrypts one 80-byte block
/// with substitution boxes, permutation boxes and round keys that are all generated from a 32-bit key, so the algorithm changes shape
/// whenever the key changes (every 10 days on mainnet). The structure and the order of the random draws must match the C++ exactly.
/// </summary>
internal sealed class OdoCipher
{
    public const int DigestSize = 80;

    private const int Rounds = 84;
    private const int SmallSboxWidth = 6;
    private const int LargeSboxWidth = 10;
    private const int PboxSubrounds = 6;
    private const int PboxM = 3;
    private const int RotationCount = 6;
    private const int WordBits = 64;
    private const int StateSize = DigestSize * 8 / WordBits;                              // 10 words
    private const int SmallSboxCount = DigestSize * 8 / (SmallSboxWidth + LargeSboxWidth); // 40
    private const int LargeSboxCount = StateSize;                                          // 10

    private readonly byte[][] _sbox1 = new byte[SmallSboxCount][];
    private readonly ushort[][] _sbox2 = new ushort[LargeSboxCount][];
    private readonly Pbox[] _permutation = new Pbox[2];
    private readonly int[] _rotations = new int[RotationCount];
    private readonly ushort[] _roundKey = new ushort[Rounds];

    private sealed class Pbox
    {
        public readonly ulong[][] Mask = new ulong[PboxSubrounds][];
        public readonly int[][] Rotation = new int[PboxSubrounds - 1][];
    }

    public OdoCipher(uint key)
    {
        var r = new OdoRandom(key);

        for (var i = 0; i < SmallSboxCount; i++)
            _sbox1[i] = r.Permutation8(1 << SmallSboxWidth);
        for (var i = 0; i < LargeSboxCount; i++)
            _sbox2[i] = r.Permutation16(1 << LargeSboxWidth);

        for (var i = 0; i < 2; i++)
        {
            var perm = new Pbox();
            for (var j = 0; j < PboxSubrounds; j++)
            {
                perm.Mask[j] = new ulong[StateSize / 2];
                for (var k = 0; k < StateSize / 2; k++)
                    perm.Mask[j][k] = r.NextLong();
            }
            for (var j = 0; j < PboxSubrounds - 1; j++)
            {
                perm.Rotation[j] = new int[StateSize / 2];
                for (var k = 0; k < StateSize / 2; k++)
                    perm.Rotation[j][k] = r.Next(63) + 1;
            }
            _permutation[i] = perm;
        }

        // Rotations must be distinct, non-zero and have an odd sum.
        var bits = r.PermutationInt(WordBits - 1);
        var sum = 0;
        for (var j = 0; j < RotationCount - 1; j++)
        {
            _rotations[j] = bits[j] + 1;
            sum += _rotations[j];
        }
        for (var j = RotationCount - 1; ; j++)
        {
            if ((bits[j] + 1 + sum) % 2 != 0)
            {
                _rotations[RotationCount - 1] = bits[j] + 1;
                break;
            }
        }

        for (var i = 0; i < Rounds; i++)
            _roundKey[i] = (ushort)r.Next(1 << StateSize);
    }

    /// <summary>Encrypts the first 80 bytes of <paramref name="block"/> in place.</summary>
    public void Encrypt(Span<byte> block)
    {
        Span<ulong> state = stackalloc ulong[StateSize];
        for (var i = 0; i < StateSize; i++)
            state[i] = BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(8 * i, 8));

        PreMix(state);
        for (var round = 0; round < Rounds; round++)
        {
            ApplyPbox(state, _permutation[0]);
            ApplySboxes(state);
            ApplyPbox(state, _permutation[1]);
            ApplyRotations(state);
            ApplyRoundKey(state, _roundKey[round]);
        }

        for (var i = 0; i < StateSize; i++)
            BinaryPrimitives.WriteUInt64LittleEndian(block.Slice(8 * i, 8), state[i]);
    }

    private static void PreMix(Span<ulong> state)
    {
        ulong total = 0;
        for (var i = 0; i < StateSize; i++)
            total ^= state[i];
        total ^= total >> 32;
        for (var i = 0; i < StateSize; i++)
            state[i] ^= total;
    }

    private void ApplySboxes(Span<ulong> state)
    {
        const ulong mask1 = (1UL << SmallSboxWidth) - 1;
        const ulong mask2 = (1UL << LargeSboxWidth) - 1;

        var smallIndex = 0;
        for (var i = 0; i < StateSize; i++)
        {
            ulong next = 0;
            var pos = 0;
            var largeIndex = i;
            for (var j = 0; j < SmallSboxCount / StateSize; j++)
            {
                next |= (ulong)_sbox1[smallIndex][(state[i] >> pos) & mask1] << pos;
                pos += SmallSboxWidth;
                next |= (ulong)_sbox2[largeIndex][(state[i] >> pos) & mask2] << pos;
                pos += LargeSboxWidth;
                smallIndex++;
            }
            state[i] = next;
        }
    }

    private static void ApplyMaskedSwaps(Span<ulong> state, ulong[] mask)
    {
        for (var i = 0; i < StateSize / 2; i++)
        {
            var swap = mask[i] & (state[2 * i] ^ state[2 * i + 1]);
            state[2 * i] ^= swap;
            state[2 * i + 1] ^= swap;
        }
    }

    private static void ApplyWordShuffle(Span<ulong> state, int m)
    {
        Span<ulong> next = stackalloc ulong[StateSize];
        for (var i = 0; i < StateSize; i++)
            next[m * i % StateSize] = state[i];
        next.CopyTo(state);
    }

    private static ulong Rot(ulong x, int r) => r == 0 ? x : BitOperations.RotateLeft(x, r);

    private static void ApplyPboxRotations(Span<ulong> state, int[] rotation)
    {
        // Only the even words are rotated, as in the reference.
        for (var i = 0; i < StateSize / 2; i++)
            state[2 * i] = Rot(state[2 * i], rotation[i]);
    }

    private static void ApplyPbox(Span<ulong> state, Pbox perm)
    {
        for (var i = 0; i < PboxSubrounds - 1; i++)
        {
            ApplyMaskedSwaps(state, perm.Mask[i]);
            ApplyWordShuffle(state, PboxM);
            ApplyPboxRotations(state, perm.Rotation[i]);
        }
        ApplyMaskedSwaps(state, perm.Mask[PboxSubrounds - 1]);
    }

    private void ApplyRotations(Span<ulong> state)
    {
        Span<ulong> next = stackalloc ulong[StateSize];
        for (var i = 0; i < StateSize; i++)
            next[i] = state[(i + 1) % StateSize];
        for (var i = 0; i < StateSize; i++)
            for (var j = 0; j < RotationCount; j++)
                next[i] ^= Rot(state[i], _rotations[j]);
        next.CopyTo(state);
    }

    private static void ApplyRoundKey(Span<ulong> state, int roundKey)
    {
        for (var i = 0; i < StateSize; i++)
            state[i] ^= (ulong)((roundKey >> i) & 1);
    }

    /// <summary>Knuth's linear congruential generator, advanced so that each seed gives a unique sequence (<c>OdoRandom</c>).</summary>
    private sealed class OdoRandom
    {
        private const ulong BaseMultiplicand = 6364136223846793005UL;
        private const ulong BaseAddend = 1442695040888963407UL;

        private ulong _current;
        private ulong _multiplicand = 1;
        private ulong _addend;

        public OdoRandom(uint seed) => _current = seed;

        private uint NextInt()
        {
            unchecked
            {
                _addend += _multiplicand * BaseAddend;
                _multiplicand *= BaseMultiplicand;
                _current = _current * _multiplicand + _addend;
                return (uint)(_current >> 32);
            }
        }

        public ulong NextLong()
        {
            ulong hi = NextInt();
            return (hi << 32) | NextInt();
        }

        public int Next(int n) => (int)(((ulong)NextInt() * (ulong)n) >> 32);

        public byte[] Permutation8(int size)
        {
            var arr = new byte[size];
            for (var i = 0; i < size; i++)
                arr[i] = (byte)i;
            for (var i = 1; i < size; i++)
            {
                var j = Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        public ushort[] Permutation16(int size)
        {
            var arr = new ushort[size];
            for (var i = 0; i < size; i++)
                arr[i] = (ushort)i;
            for (var i = 1; i < size; i++)
            {
                var j = Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }

        public int[] PermutationInt(int size)
        {
            var arr = new int[size];
            for (var i = 0; i < size; i++)
                arr[i] = i;
            for (var i = 1; i < size; i++)
            {
                var j = Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }
            return arr;
        }
    }
}
