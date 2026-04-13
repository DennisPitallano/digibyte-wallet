using System.Text.Json;
using DigiByte.Wallet.Models;
using DigiByte.Wallet.Storage;

namespace DigiByte.Wallet.Services;

/// <summary>
/// Manages contacts stored locally in IndexedDB.
/// </summary>
public class ContactService
{
    private readonly ISecureStorage _storage;
    private const string ContactsKey = "contacts";
    private List<Contact>? _cache;

    public ContactService(ISecureStorage storage)
    {
        _storage = storage;
    }

    public async Task<List<Contact>> GetAllAsync()
    {
        if (_cache != null) return _cache;
        var json = await _storage.GetAsync(ContactsKey);
        _cache = json != null
            ? JsonSerializer.Deserialize<List<Contact>>(json) ?? []
            : [];
        return _cache;
    }

    public async Task<Contact?> GetByIdAsync(string id)
    {
        var contacts = await GetAllAsync();
        return contacts.FirstOrDefault(c => c.Id == id);
    }

    public async Task<Contact?> GetByAddressAsync(string address)
    {
        var contacts = await GetAllAsync();
        return contacts.FirstOrDefault(c =>
            string.Equals(c.Address, address, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<Contact>> SearchAsync(string query)
    {
        var contacts = await GetAllAsync();
        if (string.IsNullOrWhiteSpace(query)) return contacts;
        var q = query.ToLower();
        return contacts.Where(c =>
            c.Name.ToLower().Contains(q) ||
            c.Address.ToLower().Contains(q) ||
            (c.Notes?.ToLower().Contains(q) ?? false)
        ).ToList();
    }

    public async Task AddAsync(Contact contact)
    {
        var contacts = await GetAllAsync();
        contacts.Add(contact);
        await SaveAsync(contacts);
    }

    public async Task UpdateAsync(Contact contact)
    {
        var contacts = await GetAllAsync();
        var idx = contacts.FindIndex(c => c.Id == contact.Id);
        if (idx >= 0)
        {
            contacts[idx] = contact;
            await SaveAsync(contacts);
        }
    }

    public async Task DeleteAsync(string id)
    {
        var contacts = await GetAllAsync();
        contacts.RemoveAll(c => c.Id == id);
        await SaveAsync(contacts);
    }

    /// <summary>
    /// Returns all contacts with the user's other wallets prepended as virtual contacts.
    /// Wallets appear first, sorted by name, followed by regular contacts.
    /// The active wallet is excluded (you can't send to yourself).
    /// </summary>
    public async Task<List<Contact>> GetAllWithWalletsAsync(WalletService walletService)
    {
        var contacts = await GetAllAsync();

        if (!walletService.IsUnlocked) return contacts;

        var allWallets = await walletService.GetAllWalletsAsync();
        var activeId = walletService.ActiveWallet?.Id;

        // Only include other wallets that are unlocked (we need their address)
        var walletContacts = new List<Contact>();
        foreach (var w in allWallets)
        {
            if (w.Id == activeId) continue; // skip active wallet
            if (!walletService.IsWalletUnlocked(w.Id)) continue;

            try
            {
                var address = walletService.GetReceivingAddressForWallet(w.Id);
                if (string.IsNullOrEmpty(address)) continue;

                walletContacts.Add(new Contact
                {
                    Id = $"wallet:{w.Id}",
                    Name = w.Name,
                    Address = address,
                    Notes = "My wallet",
                    IsWallet = true,
                    WalletColor = w.Color,
                });
            }
            catch { /* wallet not accessible */ }
        }

        return [.. walletContacts, .. contacts];
    }

    private async Task SaveAsync(List<Contact> contacts)
    {
        _cache = contacts;
        var json = JsonSerializer.Serialize(contacts);
        await _storage.SetAsync(ContactsKey, json);
    }
}
