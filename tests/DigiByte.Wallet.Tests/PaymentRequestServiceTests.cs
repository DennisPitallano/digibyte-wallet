using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Services;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Tests;

/// <summary>
/// Requests are stored per wallet under <c>payment_requests:{walletId}</c> (spec 036, research R8), and the old
/// device-wide list is split between wallets the first time each one loads.
/// </summary>
public class PaymentRequestServiceTests
{
    private const string LegacyKey = "payment_requests";
    private static readonly HashSet<string> WalletA = ["dgb1qa1", "dgb1qa2"];

    private static string Legacy(params (string Id, string Address)[] items) =>
        JsonSerializer.Serialize(items.Select(i => new PaymentRequest { Id = i.Id, Address = i.Address }).ToList());

    [Fact]
    public async Task GetAll_ReadsTheWalletsOwnKey()
    {
        var storage = new InMemorySecureStorage();
        var stored = new List<PaymentRequest> { new() { Id = "x", Address = "dgb1qa1", Tracked = true } };
        await storage.SetAsync("payment_requests:A", JsonSerializer.Serialize(stored, PaymentRequestService.JsonOptions));

        var all = await new PaymentRequestService(storage).GetAllAsync("A");

        Assert.Equal("x", Assert.Single(all).Id);
    }

    [Fact]
    public async Task GetAll_MovesThisWalletsLegacyRequests_AndRemovesTheEmptyLegacyKey()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync(LegacyKey, Legacy(("a", "dgb1qa1")));

        var all = await new PaymentRequestService(storage).GetAllAsync("A", WalletA);

        var r = Assert.Single(all);
        Assert.False(r.Tracked);
        Assert.Equal(PaymentRequestStatus.NotTracked, r.Status);
        Assert.Null(await storage.GetAsync(LegacyKey));
        Assert.NotNull(await storage.GetAsync("payment_requests:A"));
    }

    [Fact]
    public async Task GetAll_LeavesOtherWalletsLegacyRequests()
    {
        var storage = new InMemorySecureStorage();
        await storage.SetAsync(LegacyKey, Legacy(("a", "dgb1qa1"), ("b", "dgb1qb1")));
        var service = new PaymentRequestService(storage);

        Assert.Single(await service.GetAllAsync("A", WalletA));
        var b = await service.GetAllAsync("B", new HashSet<string> { "dgb1qb1" });

        Assert.Equal("b", Assert.Single(b).Id);
        Assert.Null(await storage.GetAsync(LegacyKey));
    }

    [Fact]
    public async Task GetAll_WithoutAddresses_DoesNotTouchTheLegacyList()
    {
        var storage = new InMemorySecureStorage();
        var legacy = Legacy(("a", "dgb1qa1"));
        await storage.SetAsync(LegacyKey, legacy);

        var all = await new PaymentRequestService(storage).GetAllAsync("A");

        Assert.Empty(all);
        Assert.Equal(legacy, await storage.GetAsync(LegacyKey));
    }

    [Fact]
    public async Task TwoWallets_KeepSeparateLists()
    {
        var storage = new InMemorySecureStorage();
        var service = new PaymentRequestService(storage);

        await service.CreateUntrackedAsync("A", "dgb1qa1", 1m, "a", null);
        await service.CreateUntrackedAsync("B", "dgb1qb1", 2m, "b", null);

        Assert.Equal("a", Assert.Single(await service.GetAllAsync("A")).Label);
        Assert.Equal("b", Assert.Single(await service.GetAllAsync("B")).Label);
        // A fresh service reads the same thing back from storage.
        Assert.Equal("a", Assert.Single(await new PaymentRequestService(storage).GetAllAsync("A")).Label);
    }

    [Fact]
    public async Task Apply_StoresTheComputedResult()
    {
        var storage = new InMemorySecureStorage();
        var service = new PaymentRequestService(storage);
        var r = await service.CreateUntrackedAsync("A", "dgb1qa1", 1m, null, null);
        var at = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);
        var payment = new RequestPayment { TxId = "t", AmountSatoshis = 100_000_000, Confirmed = true, FirstSeenAt = at };

        await service.ApplyAsync("A", r.Id, new RequestUpdate(PaymentRequestStatus.Paid, [payment], at,
            PaidAnnouncedAt: at, LockNotifiedAt: at.AddSeconds(1)));

        var back = Assert.Single(await new PaymentRequestService(storage).GetAllAsync("A"));
        Assert.Equal(PaymentRequestStatus.Paid, back.Status);
        Assert.Equal("t", Assert.Single(back.Payments).TxId);
        Assert.Equal(at, back.LastCheckedAt);
        Assert.Equal(at, back.PaidAnnouncedAt);
        Assert.Equal(at.AddSeconds(1), back.LockNotifiedAt);
    }

    [Fact]
    public async Task Apply_NeverClearsAnnouncementTimes()
    {
        var storage = new InMemorySecureStorage();
        var service = new PaymentRequestService(storage);
        var r = await service.CreateUntrackedAsync("A", "dgb1qa1", 1m, null, null);
        var at = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);
        await service.ApplyAsync("A", r.Id, new RequestUpdate(PaymentRequestStatus.Paid, [], at, at, at));

        await service.ApplyAsync("A", r.Id, new RequestUpdate(PaymentRequestStatus.Paid, [], at.AddMinutes(1), null, null));

        var back = Assert.Single(await service.GetAllAsync("A"));
        Assert.Equal(at, back.PaidAnnouncedAt);
        Assert.Equal(at, back.LockNotifiedAt);
        Assert.Equal(at.AddMinutes(1), back.LastCheckedAt);
    }

    [Fact]
    public async Task Apply_ToAnUnknownRequest_DoesNothing()
    {
        var storage = new InMemorySecureStorage();
        var service = new PaymentRequestService(storage);

        await service.ApplyAsync("A", "missing", new RequestUpdate(PaymentRequestStatus.Paid, [], DateTime.UtcNow, null, null));

        Assert.Empty(await service.GetAllAsync("A"));
    }

    [Fact]
    public async Task MarkOpened_SetsOpenedSincePaid()
    {
        var storage = new InMemorySecureStorage();
        var service = new PaymentRequestService(storage);
        var r = await service.CreateUntrackedAsync("A", "dgb1qa1", 1m, null, null);

        await service.MarkOpenedAsync("A", r.Id);

        Assert.True(Assert.Single(await new PaymentRequestService(storage).GetAllAsync("A")).OpenedSincePaid);
    }

    // ── Making a request (T018) ──

    private static async Task<(ChangeAddressTests.Env Env, WalletService Wallet, PaymentRequestService Requests, AddressLabelService Labels)> RealWalletAsync()
    {
        var env = new ChangeAddressTests.Env();
        var wallet = env.NewService();
        await wallet.CreateWalletAsync("Test", env.Words, ChangeAddressTests.Pin);
        await wallet.GetBalanceAsync();
        var labels = new AddressLabelService(env.Storage);
        return (env, wallet, new PaymentRequestService(env.Storage, wallet, labels), labels);
    }

    [Fact]
    public async Task Create_GivesTheRequestItsOwnTrackedAddress()
    {
        var (env, wallet, requests, _) = await RealWalletAsync();
        var shown = await wallet.GetReceivingAddressAsync();

        var r = await requests.CreateAsync(wallet.ActiveWallet!.Id, new NewRequest(2.5m, "Lunch", "Thanks"));

        Assert.True(r.Tracked);
        Assert.NotNull(r.AddressIndex);
        Assert.Equal(env.ReceiveAddress(r.AddressIndex!.Value), r.Address);
        Assert.NotEqual(shown, r.Address);
        Assert.Equal(PaymentRequestStatus.Waiting, r.Status);
        Assert.Equal(2.5m, r.AmountDgb);
        Assert.Equal("Thanks", r.Message);
        Assert.Equal(r.Id, Assert.Single(await requests.GetAllAsync(wallet.ActiveWallet.Id)).Id);
    }

    [Fact]
    public async Task Create_WritesTheLabelAsTheAddressLabel()
    {
        var (_, wallet, requests, labels) = await RealWalletAsync();

        var r = await requests.CreateAsync(wallet.ActiveWallet!.Id, new NewRequest(null, "  Rent  ", null));

        Assert.Equal("Rent", r.Label);
        Assert.Equal("Rent", await labels.GetLabelAsync(wallet.ActiveWallet.Id, r.Address));
        Assert.Null(r.AmountDgb);
    }

    [Fact]
    public async Task Create_OnASingleAddressWallet_MakesAnUntrackedRequestOnItsAddress()
    {
        var env = new ChangeAddressTests.Env();
        var wallet = env.NewService();
        await wallet.CreateWalletFromWifAsync("Paper", new NBitcoin.Key().GetWif(ChangeAddressTests.Network).ToString(), ChangeAddressTests.Pin);
        var requests = new PaymentRequestService(env.Storage, wallet, new AddressLabelService(env.Storage));

        var r = await requests.CreateAsync(wallet.ActiveWallet!.Id, new NewRequest(1m, null, null));

        Assert.False(r.Tracked);
        Assert.Equal(PaymentRequestStatus.NotTracked, r.Status);
        Assert.Equal(await wallet.GetReceivingAddressAsync(), r.Address);
    }

    [Fact]
    public async Task Create_AtTheLimit_PassesTheRefusalOn_AndStoresNothing()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();
        var id = wallet.ActiveWallet!.Id;
        for (var n = 0; n < 19; n++) await requests.CreateAsync(id, new NewRequest(1m, null, null));

        var ex = await Assert.ThrowsAsync<ReceiveAddressException>(() => requests.CreateAsync(id, new NewRequest(1m, null, null)));

        Assert.Equal(ReceiveAddressFailure.TooManyUnusedPastLastUsed, ex.Reason);
        Assert.Equal(19, (await requests.GetAllAsync(id)).Count);
    }

    // ── Requests in the person's currency (T044, FR-017, FR-018) ──

    private static readonly PriceQuote Usd = new(0.1m, "USD", DateTime.UtcNow);

    [Fact]
    public async Task Create_InTheCurrency_FixesTheDgbAmount_AndExpiresInFifteenMinutes()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();
        var before = DateTime.UtcNow;

        var r = await requests.CreateAsync(wallet.ActiveWallet!.Id, new NewRequest(null, "Coffee", null, FiatAmount: 12.50m, Quote: Usd));

        Assert.Equal(125m, r.AmountDgb);
        Assert.Equal(12.50m, r.FiatAmount);
        Assert.Equal("USD", r.FiatCurrency);
        Assert.Equal(0.1m, r.FiatPrice);
        Assert.NotNull(r.ExpiresAt);
        Assert.Equal(TimeSpan.FromMinutes(15), r.ExpiresAt!.Value - r.CreatedAt);
        Assert.True(r.CreatedAt >= before);
    }

    [Fact]
    public async Task Create_InTheCurrency_WithoutAPrice_IsRefused()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();
        var id = wallet.ActiveWallet!.Id;

        await Assert.ThrowsAsync<ArgumentException>(() => requests.CreateAsync(id, new NewRequest(null, null, null, FiatAmount: 5m, Quote: null)));
        await Assert.ThrowsAsync<ArgumentException>(() => requests.CreateAsync(id, new NewRequest(null, null, null, FiatAmount: 5m, Quote: Usd with { Price = 0 })));
        Assert.Empty(await requests.GetAllAsync(id));
    }

    [Fact]
    public async Task Create_InDgb_HasNoExpiry()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();

        var r = await requests.CreateAsync(wallet.ActiveWallet!.Id, new NewRequest(5m, null, null));

        Assert.Null(r.ExpiresAt);
        Assert.Null(r.FiatAmount);
    }

    [Fact]
    public async Task Renew_AnExpiredRequest_AsksTheSameCurrencyAmountAtTheNewPrice_OnANewAddress()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();
        var id = wallet.ActiveWallet!.Id;
        var old = await requests.CreateAsync(id, new NewRequest(null, "Coffee", "Thanks", FiatAmount: 10m, Quote: Usd));

        var renewed = await requests.RenewAsync(id, old.Id, Usd with { Price = 0.2m });

        Assert.NotNull(renewed);
        Assert.Equal(10m, renewed!.FiatAmount);
        Assert.Equal(50m, renewed.AmountDgb);
        Assert.Equal(0.2m, renewed.FiatPrice);
        Assert.Equal(old.Id, renewed.RenewedFromId);
        Assert.NotEqual(old.Address, renewed.Address);
        Assert.Equal("Coffee", renewed.Label);
        Assert.Equal("Thanks", renewed.Message);
        Assert.Equal(2, (await requests.GetAllAsync(id)).Count);
    }

    [Fact]
    public async Task Renew_APartlyPaidExpiredRequest_AsksOnlyForTheRest()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();
        var id = wallet.ActiveWallet!.Id;
        var old = await requests.CreateAsync(id, new NewRequest(null, null, null, FiatAmount: 20m, Quote: Usd)); // 200 DGB
        var half = new RequestPayment { TxId = "t", AmountSatoshis = 100 * 100_000_000L, Confirmed = true, FirstSeenAt = DateTime.UtcNow };
        await requests.ApplyAsync(id, old.Id, new RequestUpdate(PaymentRequestStatus.PartlyPaidExpired, [half], DateTime.UtcNow, null, null));

        var renewed = await requests.RenewAsync(id, old.Id, Usd with { Price = 0.15m });

        // 100 DGB still missing, now worth 15.00 USD.
        Assert.Equal(15m, renewed!.FiatAmount);
        Assert.Equal(100m, renewed.AmountDgb);
    }

    [Fact]
    public async Task Renew_IsRefused_ForARequestInDgb_OrWhenTheCurrencyChanged()
    {
        var (_, wallet, requests, _) = await RealWalletAsync();
        var id = wallet.ActiveWallet!.Id;
        var dgb = await requests.CreateAsync(id, new NewRequest(5m, null, null));
        var usd = await requests.CreateAsync(id, new NewRequest(null, null, null, FiatAmount: 10m, Quote: Usd));

        Assert.Null(await requests.RenewAsync(id, dgb.Id, Usd));
        Assert.Null(await requests.RenewAsync(id, usd.Id, new PriceQuote(0.09m, "EUR", DateTime.UtcNow)));
        Assert.Null(await requests.RenewAsync(id, "missing", Usd));
    }

    internal sealed class InMemorySecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _store = new();
        public Task<string?> GetAsync(string key) => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value) { _store[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key) { _store.Remove(key); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_store.ContainsKey(key));
        public Task ClearAsync() { _store.Clear(); return Task.CompletedTask; }
        public Task<List<string>> GetKeysWithPrefixAsync(string prefix) =>
            Task.FromResult(_store.Keys.Where(k => k.StartsWith(prefix)).ToList());
    }
}
