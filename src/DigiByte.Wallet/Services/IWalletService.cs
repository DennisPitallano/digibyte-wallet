using DigiByte.Wallet.Models;

namespace DigiByte.Wallet.Services;

public interface IWalletService
{
    Task<WalletInfo> CreateWalletAsync(string name, string mnemonic, string pin);
    Task<WalletInfo?> GetWalletAsync(string walletId);
    Task<bool> UnlockWalletAsync(string walletId, string pin);
    Task LockWalletAsync();
    Task<WalletBalance> GetBalanceAsync();
    Task<string> GetReceivingAddressAsync(string format = "default");
    Task<string> SendAsync(string destinationAddress, decimal amountDgb, string? memo = null);
    Task<List<TransactionRecord>> GetTransactionHistoryAsync(int skip = 0, int take = 50);
    Task<List<Contact>> GetContactsAsync();
    Task AddContactAsync(Contact contact);
    Task RemoveContactAsync(string contactId);
}
