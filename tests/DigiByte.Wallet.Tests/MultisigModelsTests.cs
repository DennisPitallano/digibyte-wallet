using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Tests;

public class MultisigModelsTests
{
    [Fact]
    public void MultisigWalletConfig_Label_FormatsCorrectly()
    {
        var config = new MultisigWalletConfig
        {
            WalletId = "test",
            RequiredSignatures = 2,
            TotalSigners = 3,
            CoSigners = [],
            RedeemScriptHex = "00",
            Address = "addr",
        };

        Assert.Equal("2-of-3 Multisig", config.Label);
    }

    [Fact]
    public void MultisigWalletConfig_DefaultAddressType_IsP2SH_P2WSH()
    {
        var config = new MultisigWalletConfig
        {
            WalletId = "test",
            RequiredSignatures = 1,
            TotalSigners = 2,
            CoSigners = [],
            RedeemScriptHex = "00",
            Address = "addr",
        };

        Assert.Equal("p2sh-p2wsh", config.AddressType);
    }

    [Fact]
    public void MultisigWalletConfig_DefaultOwnKeyIndex_IsNegative()
    {
        var config = new MultisigWalletConfig
        {
            WalletId = "test",
            RequiredSignatures = 1,
            TotalSigners = 1,
            CoSigners = [],
            RedeemScriptHex = "00",
            Address = "addr",
        };

        Assert.Equal(-1, config.OwnKeyIndex);
    }

    [Fact]
    public void PendingMultisigTransaction_AmountDgb_ConvertsCorrectly()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 100_000_000,
            FeeSatoshis = 1000,
            RequiredSignatures = 2,
        };

        Assert.Equal(1.0m, tx.AmountDgb);
    }

    [Fact]
    public void PendingMultisigTransaction_AmountDgb_FractionalAmount()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 50_000,
            FeeSatoshis = 100,
            RequiredSignatures = 2,
        };

        Assert.Equal(0.00050000m, tx.AmountDgb);
    }

    [Fact]
    public void PendingMultisigTransaction_SignatureCount_ReflectsSignedBy()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 1000,
            FeeSatoshis = 100,
            RequiredSignatures = 2,
            SignedBy = ["pubkey1", "pubkey2"],
        };

        Assert.Equal(2, tx.SignatureCount);
    }

    [Fact]
    public void PendingMultisigTransaction_HasEnoughSignatures_True()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 1000,
            FeeSatoshis = 100,
            RequiredSignatures = 2,
            SignedBy = ["pubkey1", "pubkey2"],
        };

        Assert.True(tx.HasEnoughSignatures);
    }

    [Fact]
    public void PendingMultisigTransaction_HasEnoughSignatures_False()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 1000,
            FeeSatoshis = 100,
            RequiredSignatures = 3,
            SignedBy = ["pubkey1"],
        };

        Assert.False(tx.HasEnoughSignatures);
    }

    [Fact]
    public void PendingMultisigTransaction_DefaultStatus_IsAwaitingSignatures()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 1000,
            FeeSatoshis = 100,
            RequiredSignatures = 2,
        };

        Assert.Equal(PendingTxStatus.AwaitingSignatures, tx.Status);
    }

    [Fact]
    public void PendingMultisigTransaction_DefaultSignedBy_IsEmpty()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "tx1",
            WalletId = "w1",
            PsbtBase64 = "base64",
            DestinationAddress = "addr",
            AmountSatoshis = 1000,
            FeeSatoshis = 100,
            RequiredSignatures = 2,
        };

        Assert.Empty(tx.SignedBy);
        Assert.Equal(0, tx.SignatureCount);
    }

    [Fact]
    public void CoSigner_DefaultIsLocal_IsFalse()
    {
        var cs = new CoSigner
        {
            Name = "Alice",
            PublicKeyHex = "deadbeef",
        };

        Assert.False(cs.IsLocal);
    }
}
