using DigiByte.Crypto.Addresses;
using NBitcoin;

namespace DigiByte.Crypto.KeyGeneration;

/// <summary>Which BIP44-style chain a DigiDollar key belongs to.</summary>
public enum DigiDollarChain
{
    /// <summary>External addresses handed out to payers (chain 0).</summary>
    Receive = 0,

    /// <summary>Internal addresses for DigiDollar change (chain 1); hidden from the Receive list.</summary>
    Change = 1,
}

/// <summary>
/// One derived DigiDollar key. The private key stays inside this object (in memory only, never persisted,
/// Constitution I/II) and is exposed only through <see cref="SignKeySpend"/>.
/// </summary>
public sealed class DigiDollarKey
{
    private readonly Key _privateKey;

    internal DigiDollarKey(string path, DigiDollarChain chain, int index, Key privateKey)
    {
        Path = path;
        Chain = chain;
        Index = index;
        _privateKey = privateKey;
        InternalKey = privateKey.PubKey.TaprootInternalKey;
        // BIP341 key-path tweak with no script tree: the key Core puts in the DD output (CreateTapTweak(nullptr)).
        OutputKey = InternalKey.GetTaprootFullPubKey().OutputKey;
    }

    /// <summary>Derivation path, for example <c>m/86'/20'/0'/0/3</c>.</summary>
    public string Path { get; }

    public DigiDollarChain Chain { get; }

    public int Index { get; }

    /// <summary>The BIP86 internal x-only key (before tweaking).</summary>
    public TaprootInternalPubKey InternalKey { get; }

    /// <summary>The tweaked Taproot output key. This is what the DD address encodes and what appears in <c>OP_1 &lt;key&gt;</c>.</summary>
    public TaprootPubKey OutputKey { get; }

    /// <summary>
    /// Schnorr key-path signature over a BIP341 sighash, made with the tweaked private key.
    /// </summary>
    public TaprootSignature SignKeySpend(uint256 sighash, TaprootSigHash sigHash = TaprootSigHash.Default)
    {
        var pair = _privateKey.CreateTaprootKeyPair();
        return pair.SignTaprootKeySpend(sighash, sigHash);
    }
}

/// <summary>
/// DigiDollar (DD) key derivation from the user's existing BIP39 seed (research R2).
///
/// Path: <c>m/86'/{coin}'/0'/{chain}/{index}</c> (BIP86 single-key Taproot). The DD address encodes the
/// BIP341 key-path-tweaked output key, and signing uses the tweaked private key, matching DigiByte Core's
/// <c>CreateDigiDollarP2TR</c> (<c>CreateTapTweak(nullptr)</c>).
///
/// Coin type defaults to 20 on mainnet and 1 on testnet (Core descriptor-wallet convention). The wallet's
/// BIP84 code uses 20 on every network, so the coin type is an explicit parameter; confirm the testnet
/// default against a Core-generated testnet26 vector (tasks T002) before shipping.
/// </summary>
public sealed class DigiDollarKeyDerivation
{
    private const int Purpose = 86;
    private const int MainnetCoinType = 20;
    private const int TestnetCoinType = 1;

    private readonly ExtKey _master;
    private readonly string _network;
    private readonly int _coinType;

    public DigiDollarKeyDerivation(Mnemonic mnemonic, string passphrase = "", string network = "mainnet", int? coinType = null)
        : this(RequireMnemonic(mnemonic).DeriveExtKey(passphrase), network, coinType)
    {
    }

    /// <summary>From an already-derived master key (for example the one an unlocked wallet session holds).</summary>
    public DigiDollarKeyDerivation(ExtKey master, string network = "mainnet", int? coinType = null)
    {
        ArgumentNullException.ThrowIfNull(master);
        _network = network switch
        {
            "mainnet" or "testnet" => network,
            _ => throw new ArgumentException("DigiDollar supports 'mainnet' and 'testnet' only.", nameof(network)),
        };
        _coinType = coinType ?? (_network == "mainnet" ? MainnetCoinType : TestnetCoinType);
        _master = master;
    }

    private static Mnemonic RequireMnemonic(Mnemonic mnemonic)
    {
        ArgumentNullException.ThrowIfNull(mnemonic);
        return mnemonic;
    }

    /// <summary>
    /// Secret-derived bytes used only as input key material for the DigiDollar storage key (HKDF); never stored or sent.
    /// </summary>
    public byte[] StorageKeyMaterial() => _master.ToBytes();

    /// <summary>Network this derivation encodes addresses for ("mainnet" or "testnet").</summary>
    public string Network => _network;

    /// <summary>Derive the key at <c>m/86'/{coin}'/0'/{chain}/{index}</c>.</summary>
    public DigiDollarKey Derive(DigiDollarChain chain, int index)
    {
        if (!Enum.IsDefined(chain))
            throw new ArgumentOutOfRangeException(nameof(chain));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");

        var path = $"m/{Purpose}'/{_coinType}'/0'/{(int)chain}/{index}";
        var extKey = _master.Derive(new KeyPath(path));
        return new DigiDollarKey(path, chain, index, extKey.PrivateKey);
    }

    /// <summary>The DD address (Base58Check of the tweaked output key) for a derived key on this network.</summary>
    public string GetAddress(DigiDollarKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return DigiDollarAddressValidator.Encode(key.OutputKey, _network);
    }
}
