using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>What the activity rows show for a transaction's wallet addresses (spec 010, FR-001/FR-002).</summary>
public class ActivityAddressIndexTests
{
    private const string A1 = "dgb1qreceive0000000000000001abcdef";
    private const string A2 = "dgb1qreceive0000000000000002abcdef";
    private const string A3 = "dgb1qreceive0000000000000003abcdef";

    private static TransactionRecord Tx(params string[] wallet) => new()
    {
        TxId = Guid.NewGuid().ToString("N"),
        Direction = TransactionDirection.Received,
        AmountSatoshis = 100,
        FeeSatoshis = 0,
        Timestamp = DateTime.UtcNow,
        Confirmations = 1,
        WalletAddresses = wallet.ToList(),
    };

    private static IReadOnlyDictionary<string, string> Labels(params (string Address, string Label)[] items) =>
        ActivityAddressIndex.BuildLabelLookup(items.Select(i => new KeyValuePair<string, string>(i.Address, i.Label)));

    [Fact]
    public void Display_PrefersLabelOverAddress()
    {
        var (primary, more) = ActivityAddressIndex.Display(Tx(A1), Labels((A1, "Donations")));

        Assert.Equal("Donations", primary);
        Assert.Equal(0, more);
    }

    [Fact]
    public void Display_NoLabel_ReturnsTheAddress()
    {
        var (primary, _) = ActivityAddressIndex.Display(Tx(A1), Labels());

        Assert.Equal(A1, primary);
    }

    [Fact]
    public void Display_SeveralAddresses_ShowsFirstAndCountsTheOthers()
    {
        var (primary, more) = ActivityAddressIndex.Display(Tx(A2, A1, A3), Labels((A1, "Salary")));

        Assert.Equal(A2, primary);
        Assert.Equal(2, more);
    }

    [Fact]
    public void Display_NoAddressKnown_ReturnsNull()
    {
        var (primary, more) = ActivityAddressIndex.Display(Tx(), Labels((A1, "Salary")));

        Assert.Null(primary);
        Assert.Equal(0, more);
    }

    [Fact]
    public void Display_LabelMatchesAcrossBech32ChecksumVariants()
    {
        var (primary, _) = ActivityAddressIndex.Display(Tx("dgb1qreceive0000000000000001zzzzzz"), Labels((A1, "Salary")));

        Assert.Equal("Salary", primary);
    }
}
