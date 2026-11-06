using DigiByte.Wallet.Models;

namespace DigiByte.Api.Endpoints;

public static class DirectoryEndpoints
{
    // Read-only and empty. Public registration and deletion were removed (specs/008-security-hardening, DGB-01): anyone could
    // overwrite or delete entries, and the wallet would then send money to an attacker's address. Registration returns only with
    // proof of address ownership (separate spec). Lookups stay so the wallet's remittance flow still gets a clean 404.
    private static readonly Dictionary<string, DirectoryEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static RouteGroupBuilder MapDirectoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/lookup/{username}", LookupByUsername).WithName("LookupUsername");
        group.MapGet("/lookup/phone/{phone}", LookupByPhone).WithName("LookupPhone");
        return group;
    }

    private static IResult LookupByUsername(string username)
    {
        return Entries.TryGetValue(username, out var entry)
            ? Results.Ok(entry)
            : Results.NotFound(new { error = $"Username '{username}' not found" });
    }

    private static IResult LookupByPhone(string phone)
    {
        var entry = Entries.Values.FirstOrDefault(e =>
            e.PhoneNumber != null && e.PhoneNumber.Replace("+", "").Replace("-", "").Replace(" ", "")
            == phone.Replace("+", "").Replace("-", "").Replace(" ", ""));
        return entry != null ? Results.Ok(entry) : Results.NotFound(new { error = "Phone number not found" });
    }
}
