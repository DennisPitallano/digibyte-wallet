using NBitcoin;

namespace DigiByte.Crypto.Headers.Hashers;

/// <summary>
/// Odocrypt blocks (<c>HashOdo</c> in DigiByte Core's <c>crypto/hashodo.h</c>): the 80-byte header is padded with a single 1 byte into
/// a 100-byte Keccak state, encrypted with the Odocrypt cipher keyed by the block time, then run through Keccak-p[800, 12];
/// the first 32 bytes are the hash. The key is the block time rounded down to the shape-change interval, which is 10 days on mainnet
/// and 1 day on testnet.
/// </summary>
public sealed class OdocryptHasher : IPowHasher
{
    public const uint MainnetShapeChangeIntervalSeconds = 10 * 24 * 60 * 60;

    private readonly uint _shapeChangeInterval;
    private readonly object _gate = new();
    private uint _cachedKey;
    private OdoCipher? _cachedCipher;

    public OdocryptHasher(uint shapeChangeIntervalSeconds = MainnetShapeChangeIntervalSeconds)
    {
        if (shapeChangeIntervalSeconds == 0)
            throw new ArgumentOutOfRangeException(nameof(shapeChangeIntervalSeconds));
        _shapeChangeInterval = shapeChangeIntervalSeconds;
    }

    public bool Supports(PowAlgorithm algorithm) => algorithm == PowAlgorithm.Odo;

    public uint256 Hash(BlockHeader80 header)
    {
        if (!Supports(header.Algorithm))
            throw new NotSupportedException($"{nameof(OdocryptHasher)} does not hash {header.Algorithm} blocks.");

        var key = header.Time - header.Time % _shapeChangeInterval;
        var cipher = CipherFor(key);

        var state = new byte[KeccakP800.StateBytes];
        header.ToBytes().CopyTo(state, 0);
        state[BlockHeader80.Size] = 1;
        cipher.Encrypt(state.AsSpan(0, OdoCipher.DigestSize));
        KeccakP800.Permute12Rounds(state);
        return new uint256(state.AsSpan(0, 32).ToArray());
    }

    private OdoCipher CipherFor(uint key)
    {
        lock (_gate)
        {
            if (_cachedCipher is null || _cachedKey != key)
            {
                _cachedCipher = new OdoCipher(key);
                _cachedKey = key;
            }
            return _cachedCipher;
        }
    }
}
