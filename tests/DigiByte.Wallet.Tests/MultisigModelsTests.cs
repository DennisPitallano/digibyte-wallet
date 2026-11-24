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

    // ─── Backward compatibility with data saved before spec 016 ───

    [Fact]
    public void OldConfigJson_WithoutNewFields_StillLoads_WithDefaults()
    {
        const string oldJson = """
        {"WalletId":"w1","RequiredSignatures":2,"TotalSigners":3,
         "CoSigners":[{"Name":"A","PublicKeyHex":"02aa","IsLocal":true}],
         "RedeemScriptHex":"00","Address":"addr","AddressType":"p2sh-p2wsh","OwnKeyIndex":0,
         "CreatedAt":"2026-01-01T00:00:00Z"}
        """;

        var config = System.Text.Json.JsonSerializer.Deserialize<MultisigWalletConfig>(oldJson)!;

        Assert.Empty(config.OwnerWalletIds);
        Assert.Null(config.Name);
        Assert.Null(config.BackupAt);
        Assert.Equal("2-of-3 Multisig", config.DisplayName);
    }

    [Fact]
    public void OldPendingJson_WithoutNewFields_StillLoads_WithDefaults()
    {
        const string oldJson = """
        {"Id":"p1","WalletId":"w1","PsbtBase64":"cHNidA==","DestinationAddress":"D1",
         "AmountSatoshis":100,"FeeSatoshis":10,"RequiredSignatures":2,"SignedBy":[],
         "CreatedAt":"2026-01-01T00:00:00Z","Status":0}
        """;

        var tx = System.Text.Json.JsonSerializer.Deserialize<PendingMultisigTransaction>(oldJson)!;

        Assert.Equal("", tx.UnsignedTxId);
        Assert.Null(tx.Memo);
        Assert.Null(tx.FailureReason);
        Assert.Null(tx.CancelledAt);
        Assert.Equal(PendingTxStatus.AwaitingSignatures, tx.Status);
    }

    [Fact]
    public void OldPendingJson_WithoutRecipients_ReadsAsOneRecipient()
    {
        const string oldJson = """
        {"Id":"p1","WalletId":"w1","PsbtBase64":"cHNidA==","DestinationAddress":"D1",
         "AmountSatoshis":100,"FeeSatoshis":10,"RequiredSignatures":2,"SignedBy":[],
         "CreatedAt":"2026-01-01T00:00:00Z","Status":0}
        """;

        var tx = System.Text.Json.JsonSerializer.Deserialize<PendingMultisigTransaction>(oldJson)!;

        Assert.Empty(tx.Recipients);
        Assert.Equal([new ProposalRecipient("D1", 100)], tx.AllRecipients);
        Assert.Equal(1, tx.RecipientCount);
    }

    [Fact]
    public void PendingWithSeveralRecipients_RoundTripsThroughJson()
    {
        var tx = new PendingMultisigTransaction
        {
            Id = "p2",
            WalletId = "w1",
            PsbtBase64 = "cHNidA==",
            DestinationAddress = "D1",
            AmountSatoshis = 600,
            FeeSatoshis = 10,
            RequiredSignatures = 2,
            Recipients = [new("D1", 100), new("D2", 200), new("D3", 300)],
        };

        var back = System.Text.Json.JsonSerializer.Deserialize<PendingMultisigTransaction>(
            System.Text.Json.JsonSerializer.Serialize(tx))!;

        Assert.Equal(tx.Recipients, back.Recipients);
        Assert.Equal(tx.Recipients, back.AllRecipients);
        Assert.Equal(3, back.RecipientCount);
        Assert.Equal(600, back.AmountSatoshis);
    }

    [Fact]
    public void OldDraftJson_WithoutOwner_StillLoads_WithEmptyOwner()
    {
        const string oldJson = """{"Id":"d1","Name":"Draft","RequiredSignatures":2,"TotalSigners":3,"OwnPublicKeyHex":"02aa","CoSigners":[]}""";

        var draft = System.Text.Json.JsonSerializer.Deserialize<MultisigDraft>(oldJson)!;

        Assert.Equal("", draft.OwnerWalletId);
        Assert.Equal("02aa", draft.OwnPublicKeyHex);
    }
}
