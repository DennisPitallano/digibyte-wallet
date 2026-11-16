using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>Chain transactions and local proposals merge into one activity list (spec 016, US6).</summary>
public class MultisigActivityBuilderTests
{
    private const string Me = "dgb1qmultisigaddress";
    private const string Alice = "dgb1qalice";
    private const string Bob = "dgb1qbob";

    private static readonly DateTime Day1 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TransactionInfo Tx(string id, DateTime when, int confirmations, long fee,
        (string Address, long Amount)[] inputs, (string Address, long Amount)[] outputs) => new()
    {
        TxId = id,
        Timestamp = when,
        Confirmations = confirmations,
        FeeSatoshis = fee,
        Inputs = inputs.Select(i => new TxInput { Address = i.Address, AmountSatoshis = i.Amount }).ToList(),
        Outputs = outputs.Select((o, i) => new TxOutput { Address = o.Address, AmountSatoshis = o.Amount, Index = (uint)i }).ToList(),
    };

    private static PendingMultisigTransaction Proposal(string id, PendingTxStatus status, DateTime created, int signed = 0, string? txid = null, string? memo = null) => new()
    {
        Id = id,
        WalletId = "ms",
        PsbtBase64 = "x",
        DestinationAddress = Alice,
        AmountSatoshis = 100_000_000,
        FeeSatoshis = 1_000,
        RequiredSignatures = 2,
        CreatedAt = created,
        Status = status,
        BroadcastTxId = txid,
        Memo = memo,
        SignedBy = Enumerable.Range(0, signed).Select(i => $"02{i:00}").ToList(),
    };

    [Fact]
    public void PaymentToTheMultisig_IsReceived_WithAmountAndSender()
    {
        var tx = Tx("rx1", Day1, 3, 500, [(Alice, 500_000_000)], [(Me, 300_000_000), (Alice, 199_999_500)]);

        var entry = Assert.Single(MultisigActivityBuilder.Build(Me, [tx], []));

        Assert.Equal(ActivityKind.Received, entry.Kind);
        Assert.Equal(ActivityDirection.In, entry.Direction);
        Assert.Equal(300_000_000, entry.AmountSatoshis);
        Assert.Equal(Alice, entry.Counterparty);
        Assert.Equal(3, entry.Confirmations);
    }

    [Fact]
    public void SpendFromTheMultisig_IsSent_ExcludingChange_WithFirstExternalRecipient()
    {
        var tx = Tx("tx1", Day1, 10, 2_000,
            [(Me, 1_000_000_000)],
            [(Alice, 200_000_000), (Bob, 300_000_000), (Me, 499_998_000)]);

        var entry = Assert.Single(MultisigActivityBuilder.Build(Me, [tx], []));

        Assert.Equal(ActivityKind.Sent, entry.Kind);
        Assert.Equal(500_000_000, entry.AmountSatoshis);   // 2 + 3 DGB, change excluded
        Assert.Equal(Alice, entry.Counterparty);
        Assert.Equal(1, entry.MoreCount);
        Assert.Equal(2_000, entry.FeeSatoshis);
    }

    [Fact]
    public void BroadcastProposalThatIsOnChain_AppearsOnce_AsSent_WithItsMemo()
    {
        var tx = Tx("tx9", Day1, 4, 1_000, [(Me, 300_000_000)], [(Alice, 100_000_000), (Me, 199_999_000)]);
        var proposal = Proposal("p1", PendingTxStatus.Broadcast, Day1.AddMinutes(-5), signed: 2, txid: "tx9", memo: "Rent");

        var entries = MultisigActivityBuilder.Build(Me, [tx], [proposal]);

        var entry = Assert.Single(entries);
        Assert.Equal(ActivityKind.Sent, entry.Kind);
        Assert.Equal("Rent", entry.Memo);
        Assert.Equal("p1", entry.ProposalId);
    }

    [Fact]
    public void BroadcastProposalNotYetOnChain_StillShowsOnce_AsSent()
    {
        var proposal = Proposal("p1", PendingTxStatus.Broadcast, Day1, signed: 2, txid: "later");

        var entry = Assert.Single(MultisigActivityBuilder.Build(Me, [], [proposal]));

        Assert.Equal(ActivityKind.Sent, entry.Kind);
        Assert.Equal("later", entry.TxId);
    }

    [Fact]
    public void ProposalsMapToTheirKinds_WithSignatureCounts()
    {
        var entries = MultisigActivityBuilder.Build(Me, [],
        [
            Proposal("a", PendingTxStatus.AwaitingSignatures, Day1, signed: 1),
            Proposal("r", PendingTxStatus.ReadyToFinalize, Day1.AddHours(1), signed: 2),
            Proposal("f", PendingTxStatus.Failed, Day1.AddHours(2)),
            Proposal("c", PendingTxStatus.Cancelled, Day1.AddHours(3)),
        ]);

        Assert.Equal(["c", "f", "r", "a"], entries.Select(e => e.ProposalId!).ToArray());   // newest first
        Assert.Equal(ActivityKind.ProposalAwaiting, entries.Single(e => e.ProposalId == "a").Kind);
        Assert.Equal(ActivityKind.ProposalReady, entries.Single(e => e.ProposalId == "r").Kind);
        Assert.Equal(ActivityKind.Failed, entries.Single(e => e.ProposalId == "f").Kind);
        Assert.Equal(ActivityKind.Cancelled, entries.Single(e => e.ProposalId == "c").Kind);
        var awaiting = entries.Single(e => e.ProposalId == "a");
        Assert.Equal(1, awaiting.SignedCount);
        Assert.Equal(2, awaiting.RequiredCount);
    }

    [Fact]
    public void TransactionsUnrelatedToTheMultisig_AreIgnored()
    {
        var tx = Tx("other", Day1, 1, 100, [(Alice, 10)], [(Bob, 9)]);

        Assert.Empty(MultisigActivityBuilder.Build(Me, [tx], []));
    }

    [Fact]
    public void Entries_AreNewestFirst_AndGroupedByDay()
    {
        var older = Tx("old", Day1, 50, 100, [(Alice, 100)], [(Me, 90)]);
        var newer = Tx("new", Day1.AddDays(3), 5, 100, [(Alice, 100)], [(Me, 90)]);
        var sameDayAsNewer = Tx("new2", Day1.AddDays(3).AddMinutes(30), 5, 100, [(Bob, 100)], [(Me, 90)]);

        var entries = MultisigActivityBuilder.Build(Me, [older, newer, sameDayAsNewer], []);
        var groups = MultisigActivityBuilder.GroupByDate(entries);

        Assert.Equal(["new2", "new", "old"], entries.Select(e => e.TxId!).ToArray());
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Entries.Count);
        Assert.True(groups[0].Date > groups[1].Date);
    }

    [Fact]
    public void Filters_ReturnOnlyTheirKinds()
    {
        var entries = MultisigActivityBuilder.Build(Me,
        [
            Tx("rx", Day1, 5, 1, [(Alice, 10)], [(Me, 9)]),
            Tx("tx", Day1.AddDays(1), 5, 1, [(Me, 10)], [(Bob, 9)]),
        ],
        [
            Proposal("a", PendingTxStatus.AwaitingSignatures, Day1.AddDays(2)),
            Proposal("r", PendingTxStatus.ReadyToFinalize, Day1.AddDays(3)),
            Proposal("f", PendingTxStatus.Failed, Day1.AddDays(4)),
            Proposal("c", PendingTxStatus.Cancelled, Day1.AddDays(5)),
        ]);

        Assert.Equal(6, MultisigActivityBuilder.Filter(entries, ActivityFilter.All).Count);
        Assert.Equal(["rx"], MultisigActivityBuilder.Filter(entries, ActivityFilter.Received).Select(e => e.TxId!).ToArray());
        Assert.Equal(["tx"], MultisigActivityBuilder.Filter(entries, ActivityFilter.Sent).Select(e => e.TxId!).ToArray());
        Assert.Equal(["r", "a"], MultisigActivityBuilder.Filter(entries, ActivityFilter.Pending).Select(e => e.ProposalId!).ToArray());
        Assert.Equal(["c", "f"], MultisigActivityBuilder.Filter(entries, ActivityFilter.Failed).Select(e => e.ProposalId!).ToArray());
    }

    [Fact]
    public void Received_WithManyInputAddresses_ReportsHowManyMore()
    {
        var tx = Tx("rx", Day1, 1, 1, [(Alice, 5), (Bob, 5), (Alice, 5)], [(Me, 14)]);

        var entry = Assert.Single(MultisigActivityBuilder.Build(Me, [tx], []));

        Assert.Equal(Alice, entry.Counterparty);
        Assert.Equal(1, entry.MoreCount);
    }
}

/// <summary>The service side of Activity: paging, "Load more", and the offline fallback (spec 016, US6).</summary>
public class MultisigActivityServiceTests
{
    private const string Owner = "wallet-a";

    private static async Task<(MultisigWalletService Sut, MultisigFakeChain Chain, MultisigWalletConfig Config)> Arrange(int chainTxCount)
    {
        var chain = new MultisigFakeChain();
        var sut = new MultisigWalletService(new MultisigFakeStorage(), chain, new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync("Activity", 2,
            [new CoSigner { Name = "a", PublicKeyHex = new Key().PubKey.ToHex() }, new CoSigner { Name = "b", PublicKeyHex = new Key().PubKey.ToHex() }],
            ownerWalletId: Owner);

        for (var i = 0; i < chainTxCount; i++)
        {
            chain.Transactions.Add(new TransactionInfo
            {
                TxId = $"rx{i:000}",
                Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(chainTxCount - i),
                Confirmations = 10,
                FeeSatoshis = 0,
                Inputs = [new TxInput { Address = "dgb1qsomeone", AmountSatoshis = 10_000 }],
                Outputs = [new TxOutput { Address = config.Address, AmountSatoshis = 9_000, Index = 0 }],
            });
        }
        return (sut, chain, config);
    }

    [Fact]
    public async Task FirstPage_Asks25_AndLoadMoreAdvancesByThePageSize()
    {
        var (sut, chain, config) = await Arrange(60);

        var first = await sut.GetActivityAsync(config, 0, 25);
        var second = await sut.GetActivityAsync(config, 25, 25);

        Assert.Equal([(0, 25), (25, 25)], chain.HistoryRequests.ToArray());
        Assert.Equal(25, first.Entries.Count);
        Assert.True(first.HasMore);
        Assert.Equal(25, second.Entries.Count);
        Assert.False(first.IsPartial);
    }

    [Fact]
    public async Task LastPage_HasNoMore()
    {
        var (sut, _, config) = await Arrange(30);

        var page = await sut.GetActivityAsync(config, 25, 25);

        Assert.Equal(5, page.Entries.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task WhenTheChainIsUnreachable_LocalProposalsStillShow_AndThePageIsPartial()
    {
        var (sut, chain, config) = await Arrange(0);
        var utxo = new UtxoInfo { TxId = RandomUtils.GetUInt256().ToString(), OutputIndex = 0, AmountSatoshis = 1_000_000_000, ScriptPubKey = "", Confirmations = 6 };
        chain.Utxos = [utxo];
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet).ToString();
        await sut.CreateSpendingPSBTAsync(config, dest, 1m, 10);

        chain.Offline = true;
        var page = await sut.GetActivityAsync(config, 0, 25);

        Assert.True(page.IsPartial);
        Assert.Contains(page.Entries, e => e.Kind == ActivityKind.ProposalAwaiting);
    }

    [Fact]
    public async Task LaterPages_DoNotRepeatTheProposals()
    {
        var (sut, chain, config) = await Arrange(30);
        var utxo = new UtxoInfo { TxId = RandomUtils.GetUInt256().ToString(), OutputIndex = 0, AmountSatoshis = 1_000_000_000, ScriptPubKey = "", Confirmations = 6 };
        chain.Utxos = [utxo];
        var dest = new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, DigiByte.Crypto.Networks.DigiByteNetwork.Mainnet).ToString();
        await sut.CreateSpendingPSBTAsync(config, dest, 1m, 10);

        var first = await sut.GetActivityAsync(config, 0, 25);
        var second = await sut.GetActivityAsync(config, 25, 25);

        Assert.Contains(first.Entries, e => e.Kind == ActivityKind.ProposalAwaiting);
        Assert.DoesNotContain(second.Entries, e => e.Kind == ActivityKind.ProposalAwaiting);
    }
}
