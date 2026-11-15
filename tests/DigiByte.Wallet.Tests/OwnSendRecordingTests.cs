using DigiByte.Crypto.Networks;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;
using NBitcoin;

namespace DigiByte.Wallet.Tests;

/// <summary>A transaction the wallet just sent is in the record at once: inputs gone, change spendable, removable if rejected (T082, T087).</summary>
public class OwnSendRecordingTests
{
    private const string WalletId = "w1";
    private static readonly Network Net = DigiByteNetwork.Mainnet;

    private sealed class MemoryStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _data = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value) { _data[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _data.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_data.ContainsKey(key));
        public Task ClearAsync() { _data.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) => Task.FromResult(_data.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }

    private static string Hash(char c) => new(c, 64);

    private sealed record Setup(string OwnAddress, string ChangeAddress, string OtherAddress, OutPoint Funding, GatewayWalletScope Scope, byte[] Raw, string Txid);

    /// <summary>A wallet with one funded output, and a signed-looking send of it: 700 to someone else, 250 back as change.</summary>
    private static Setup NewSend()
    {
        var own = new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString();
        var change = new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString();
        var other = new Key().GetAddress(ScriptPubKeyType.Segwit, Net).ToString();
        var funding = new OutPoint(uint256.Parse(Hash('f')), 0);

        var tx = Transaction.Create(Net);
        tx.Inputs.Add(new TxIn(funding));
        tx.Outputs.Add(Money.Satoshis(700), BitcoinAddress.Create(other, Net));
        tx.Outputs.Add(Money.Satoshis(250), BitcoinAddress.Create(change, Net));
        var scope = new GatewayWalletScope(WalletId, [own, change], new HashSet<string> { change });
        return new Setup(own, change, other, funding, scope, tx.ToBytes(), tx.GetHash().ToString());
    }

    private static async Task<(GatewayOwnSendRecorder Recorder, WalletSyncStateStore States)> RigAsync(Setup setup, bool withState = true)
    {
        var states = new WalletSyncStateStore(new MemoryStorage());
        if (withState)
        {
            var fundingTx = new SyncTransaction(Hash('f'), 100, Hash('e'), "0100", SyncOrigin.GatewayProven);
            var fundingOutput = new SyncOutpoint(Hash('f'), 0, 1000, BitcoinAddress.Create(setup.OwnAddress, Net).ScriptPubKey.ToHex());
            await states.SaveAsync(new WalletSyncState(WalletId, 200, Hash('d'), [fundingTx], [fundingOutput], DateTimeOffset.UtcNow));
        }
        return (new GatewayOwnSendRecorder(states, Net), states);
    }

    [Fact]
    public async Task A_send_is_recorded_as_unconfirmed_with_its_raw_bytes_and_marked_as_the_wallets_own()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);

        Assert.True(await recorder.RecordAsync(setup.Scope, setup.Raw));

        var sent = (await states.GetAsync(WalletId))!.Transactions.Single(t => t.Txid == setup.Txid);
        Assert.Equal(0, sent.Height);
        Assert.True(sent.IsOwnSend);
        Assert.Equal(Convert.ToHexString(setup.Raw).ToLowerInvariant(), sent.Raw);
    }

    [Fact]
    public async Task The_outputs_it_spends_are_no_longer_offered_and_are_remembered_as_spent()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);

        await recorder.RecordAsync(setup.Scope, setup.Raw);

        var state = (await states.GetAsync(WalletId))!;
        Assert.DoesNotContain(state.UnspentOutpoints, o => o.Key == $"{Hash('f')}:0");
        var remembered = Assert.Single(state.SpentOutpoints!);
        Assert.Equal(1000, remembered.Amount);
    }

    [Fact]
    public async Task The_change_is_added_and_spendable_at_once_while_the_payment_to_someone_else_is_not_the_wallets()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);

        await recorder.RecordAsync(setup.Scope, setup.Raw);

        var state = (await states.GetAsync(WalletId))!;
        var change = Assert.Single(state.UnspentOutpoints, o => o.Txid == setup.Txid);
        Assert.Equal(250, change.Amount);
        Assert.True(change.IsChangeToSelf);
        // The payment output is someone else's, so it never appears among the wallet outputs.
        Assert.Equal(1, state.UnspentOutpoints.Count(o => o.Txid == setup.Txid));

        // And the spend rules let that change be used straight away although the transaction is not in a block.
        var facts = new TransactionFacts(0, IsOwnSend: true, ProofChecked: true, ExplorerSourced: false);
        Assert.True(GatewaySpendRules.IsSpendable(new OutputFacts(facts, change.IsChangeToSelf), false));
    }

    [Fact]
    public async Task Recording_the_same_send_twice_changes_nothing()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);
        await recorder.RecordAsync(setup.Scope, setup.Raw);
        var once = await states.GetAsync(WalletId);

        Assert.False(await recorder.RecordAsync(setup.Scope, setup.Raw));

        var twice = await states.GetAsync(WalletId);
        Assert.Equal(once!.Transactions.Count, twice!.Transactions.Count);
        Assert.Equal(once.UnspentOutpoints.Count, twice.UnspentOutpoints.Count);
    }

    [Fact]
    public async Task A_wallet_without_a_record_records_nothing_and_the_send_is_not_affected()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup, withState: false);

        Assert.False(await recorder.RecordAsync(setup.Scope, setup.Raw));
        Assert.Null(await states.GetAsync(WalletId));
    }

    [Fact]
    public async Task A_rejected_send_is_taken_back_out_and_the_spent_output_is_offered_again()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);
        await recorder.RecordAsync(setup.Scope, setup.Raw);

        Assert.True(await recorder.RemoveAsync(WalletId, setup.Txid));

        var state = (await states.GetAsync(WalletId))!;
        Assert.DoesNotContain(state.Transactions, t => t.Txid == setup.Txid);
        Assert.DoesNotContain(state.UnspentOutpoints, o => o.Txid == setup.Txid);
        Assert.Contains(state.UnspentOutpoints, o => o.Key == $"{Hash('f')}:0" && o.Amount == 1000);
        Assert.Empty(state.SpentOutpoints!);
    }

    [Fact]
    public async Task Removing_something_that_was_never_recorded_does_nothing()
    {
        var setup = NewSend();
        var (recorder, _) = await RigAsync(setup);

        Assert.False(await recorder.RemoveAsync(WalletId, Hash('9')));
    }

    [Fact]
    public async Task A_transaction_that_is_already_in_a_block_in_the_record_is_not_recorded_again_as_unconfirmed()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);
        var state = (await states.GetAsync(WalletId))!;
        await states.SaveAsync(state with { Transactions = [.. state.Transactions, new SyncTransaction(setup.Txid, 150, Hash('c'), "0100", SyncOrigin.GatewayProven)] });

        Assert.False(await recorder.RecordAsync(setup.Scope, setup.Raw));
    }

    [Fact]
    public async Task Only_a_pending_own_send_can_be_removed_not_a_confirmed_transaction()
    {
        var setup = NewSend();
        var (recorder, states) = await RigAsync(setup);
        await recorder.RecordAsync(setup.Scope, setup.Raw);
        var state = (await states.GetAsync(WalletId))!;
        var confirmed = state.Transactions.Select(t => t.Txid == setup.Txid ? t with { Height = 150, BlockHash = Hash('c') } : t).ToList();
        await states.SaveAsync(state with { Transactions = confirmed });

        Assert.False(await recorder.RemoveAsync(WalletId, setup.Txid));
    }
}

/// <summary>The wallet send path records what it sent, through the real wallet service (T087).</summary>
public class OwnSendThroughWalletServiceTests
{
    private const string Pin = ChangeAddressTests.Pin;

    private static async Task<(WalletService Service, WalletSyncStateStore States, ChangeAddressTests.Env Env, string WalletId)> RigAsync(bool withRecord = true)
    {
        var env = new ChangeAddressTests.Env();
        var states = new WalletSyncStateStore(env.Storage);
        var recorder = new GatewayOwnSendRecorder(states);
        var service = new WalletService(new WalletKeyStore(env.Storage), new ChangeAddressTests.PassThroughCrypto(), env.Chain, new TransactionTracker(env.Storage),
            syncStateStore: states, ownSendRecorder: recorder);
        service.SetNetworkMode("mainnet");
        service.AddressDiscoveryWait = TimeSpan.FromMinutes(1);
        service.AddressDiscoverySendWait = TimeSpan.FromMinutes(1);
        var wallet = await service.CreateWalletAsync("Test", env.Words, Pin);
        env.Chain.Credit(env.ReceiveAddress(0), 1000 * ChangeAddressTests.OneDgb);
        if (withRecord)
            await states.SaveAsync(new WalletSyncState(wallet.Id, 100, new string('a', 64), [], [], DateTimeOffset.UtcNow));
        return (service, states, env, wallet.Id);
    }

    [Fact]
    public async Task A_send_through_the_wallet_service_appears_in_the_record_as_unconfirmed_with_its_change()
    {
        var (service, states, env, walletId) = await RigAsync();
        env.Chain.LastRecipient = ChangeAddressTests.Stranger();

        var txId = await service.SendAsync(env.Chain.LastRecipient, 1m);

        var state = (await states.GetAsync(walletId))!;
        var sent = state.Transactions.Single(t => t.Txid == env.Chain.LastBroadcast!.GetHash().ToString());
        Assert.True(sent.IsOwnSend);
        Assert.Equal(0, sent.Height);
        var change = Assert.Single(state.UnspentOutpoints, o => o.Txid == sent.Txid);
        Assert.True(change.IsChangeToSelf);
        Assert.False(string.IsNullOrEmpty(txId));
    }

    [Fact]
    public async Task A_wallet_with_no_record_still_sends_normally_and_nothing_is_recorded()
    {
        var (service, states, env, walletId) = await RigAsync(withRecord: false);
        env.Chain.LastRecipient = ChangeAddressTests.Stranger();

        var txId = await service.SendAsync(env.Chain.LastRecipient, 1m);

        Assert.False(string.IsNullOrEmpty(txId));
        Assert.Null(await states.GetAsync(walletId));
    }

    [Fact]
    public async Task A_failure_while_recording_never_changes_the_result_of_the_send()
    {
        var (service, states, env, walletId) = await RigAsync();
        // A record the recorder cannot save over (storage write fails once the send has gone out).
        env.Storage.FailWritesWithPrefix = WalletSyncStateStore.KeyPrefix;
        env.Chain.LastRecipient = ChangeAddressTests.Stranger();

        var txId = await service.SendAsync(env.Chain.LastRecipient, 1m);

        Assert.False(string.IsNullOrEmpty(txId));
        env.Storage.FailWritesWithPrefix = null;
        Assert.Empty((await states.GetAsync(walletId))!.Transactions);
    }
}
