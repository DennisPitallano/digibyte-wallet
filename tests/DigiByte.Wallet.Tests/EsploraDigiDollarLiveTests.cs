using DigiByte.Crypto.Addresses;
using DigiByte.Wallet.Services;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Opt-in checks of <see cref="EsploraDigiDollarApi"/> against the real digiexplorer.info (set <c>DD_LIVE=1</c>). They use a real
/// mainnet transfer: $500.00 minted, then spent into $1.42 and $498.58. Skipped (pass trivially) when offline or not opted in.
/// </summary>
public class EsploraDigiDollarLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("DD_LIVE") == "1";

    private static EsploraDigiDollarApi Live() =>
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, "https://digiexplorer.info/api", "https://digiexplorer.info/testnet/api", () => false);

    private static readonly string Received = DigiDollarAddressValidator.Encode(Convert.FromHexString("b42be14e35aeafcd503cfa260e34b61a96845f717988869c504e21e0718a04f6"), "mainnet");
    private static readonly string Change = DigiDollarAddressValidator.Encode(Convert.FromHexString("d1242ac5231ddc645ed750bb5f086661a59c54f3362b03254106469e62adb06f"), "mainnet");

    [Fact]
    public async Task RealChain_Health_Utxos_History_Status()
    {
        if (!Enabled) return;
        var api = Live();

        var health = await api.GetHealthAsync(default);
        Assert.True(health.Available);
        Assert.True(health.DeploymentActive);
        Assert.True(health.NodeHeight > 24_000_000);

        var utxos = await api.GetUtxosAsync([Received, Change], 0, default);
        // Either still unspent (the sample outputs) or spent since: when present, the amounts must match the chain.
        foreach (var u in utxos[Received]) Assert.Equal(142, u.AmountCents);
        foreach (var u in utxos[Change].Where(u => u.Txid.StartsWith("4fb30cba"))) Assert.Equal(49858, u.AmountCents);

        var history = await api.GetHistoryAsync([Change], 50, null, default);
        var transfer = history.Items.Single(e => e.Txid == "4fb30cbaefd160d07717470ef4dc8a4aa6133718b76908954b050c0620cd0b1b");
        Assert.Equal(50000, transfer.DdInputs.Sum(i => i.AmountCents));
        Assert.Equal(50000, transfer.DdOutputs.Sum(o => o.AmountCents));
        Assert.Contains(history.Items, e => e.Txid.StartsWith("c59a6716") && e.DdOutputs.Single().AmountCents == 50000);

        var status = await api.GetTxStatusAsync(transfer.Txid, default);
        Assert.Equal(DdTxState.Confirmed, status.State);
        Assert.True(status.Confirmations >= 600);
    }
}
