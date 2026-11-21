using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Multisig wallets get the shared read improvements (spec 023, clarification 4): a busy multisig address shows its complete
/// history through the real Esplora client, past the explorer's 25-transaction page.
/// </summary>
public class MultisigHistoryTests
{
    [Fact]
    public async Task MultisigAddressWithSixtyTransactions_ShowsAllSixty()
    {
        var stub = new EsploraPagingTests.EsploraStub();
        var sut = new MultisigWalletService(new MultisigFakeStorage(), new BlockchainApiService(new HttpClient(stub)), new MultisigFakeCrypto());
        var config = await sut.CreateMultisigWalletAsync("Busy", 2,
            [new CoSigner { Name = "a", PublicKeyHex = new Key().PubKey.ToHex() }, new CoSigner { Name = "b", PublicKeyHex = new Key().PubKey.ToHex() }],
            ownerWalletId: "wallet-a");
        stub.Busy(config.Address, 60);

        var seen = new List<string>();
        for (var skip = 0; skip < 100; skip += 25)
        {
            var page = await sut.GetActivityAsync(config, skip, 25);
            Assert.False(page.IsPartial);
            seen.AddRange(page.Entries.Select(e => e.TxId ?? ""));
            if (!page.HasMore) break;
        }

        Assert.Equal(60, seen.Where(id => id.Length > 0).Distinct().Count());
    }
}
