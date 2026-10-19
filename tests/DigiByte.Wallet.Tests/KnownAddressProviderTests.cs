using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

public class KnownAddressProviderTests
{
    [Fact]
    public void Build_MergesContactsAndSentAddresses()
    {
        var set = KnownAddressProvider.Build(["DContact1"], ["DSent1"]);
        Assert.Contains("DContact1", set);
        Assert.Contains("DSent1", set);
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void Build_DropsNullEmptyAndWhitespace_AndTrims()
    {
        var set = KnownAddressProvider.Build([null, "", "  ", " DContact1 "], [null]);
        Assert.Single(set);
        Assert.Contains("DContact1", set);
    }

    [Fact]
    public void Build_Deduplicates()
    {
        var set = KnownAddressProvider.Build(["DSame"], ["DSame", "DSame"]);
        Assert.Single(set);
    }

    [Fact]
    public void Build_NormalisesBech32ToLowerCase_ButLeavesBase58Alone()
    {
        var set = KnownAddressProvider.Build(["DGB1QABC"], ["DMixedCaseBase58"]);
        Assert.Contains("dgb1qabc", set);
        Assert.Contains("DMixedCaseBase58", set);
        Assert.DoesNotContain("dmixedcasebase58", set);
    }

    [Fact]
    public void SentAddresses_KeepsOnlySentRecords_WithACounterparty()
    {
        var records = new List<TransactionRecord>
        {
            Record(TransactionDirection.Sent, "DSent1"),
            Record(TransactionDirection.Received, "DReceivedFrom"), // an address that paid us is not one we sent to
            Record(TransactionDirection.Sent, null),
            Record(TransactionDirection.Sent, " "),
        };
        var sent = KnownAddressProvider.SentAddresses(records).ToList();
        Assert.Equal(["DSent1"], sent);
    }

    [Fact]
    public void SentAddresses_OfNothing_IsEmpty()
    {
        Assert.Empty(KnownAddressProvider.SentAddresses([]));
    }

    [Fact]
    public void SentDigiDollarAddresses_KeepsOnlySentEntries_WithACounterparty()
    {
        DigiDollarTransaction Dd(DdDirection dir, string? cp) =>
            new("tx" + Guid.NewGuid().ToString("N"), dir, cp, 500, 10, 3, DateTimeOffset.UtcNow, default);
        var sent = KnownAddressProvider.SentDigiDollarAddresses(
        [
            Dd(DdDirection.Sent, "DDRecipient1"),
            Dd(DdDirection.Received, "DDPayer"),
            Dd(DdDirection.Sent, null),
        ]).ToList();
        Assert.Equal(["DDRecipient1"], sent);
    }

    private static TransactionRecord Record(TransactionDirection dir, string? counterparty) => new()
    {
        TxId = Guid.NewGuid().ToString("N"),
        Direction = dir,
        AmountSatoshis = 1_000,
        FeeSatoshis = 10,
        Timestamp = DateTime.UtcNow,
        Confirmations = 3,
        CounterpartyAddress = counterparty,
    };
}
