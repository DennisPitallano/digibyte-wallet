using DigiByte.Wallet.Models;

namespace DigiByte.Api.Endpoints;

public static class DirectoryEndpoints
{
    // In-memory store for now — will move to DB
    private static readonly Dictionary<string, DirectoryEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static RouteGroupBuilder MapDirectoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/lookup/{username}", LookupByUsername).WithName("LookupUsername");
        group.MapGet("/lookup/phone/{phone}", LookupByPhone).WithName("LookupPhone");
        group.MapPost("/register", Register).WithName("RegisterUsername");
        group.MapDelete("/{username}", Unregister).WithName("UnregisterUsername");
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

    private static IResult Register(DirectoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Username) || entry.Username.Length < 3)
            return Results.BadRequest(new { error = "Username must be at least 3 characters" });

        if (Entries.ContainsKey(entry.Username))
            return Results.Conflict(new { error = $"Username '{entry.Username}' is already taken" });

        entry.RegisteredAt = DateTime.UtcNow;
        entry.LastUpdated = DateTime.UtcNow;
        Entries[entry.Username] = entry;
        return Results.Created($"/api/directory/lookup/{entry.Username}", entry);
    }

    private static IResult Unregister(string username)
    {
        return Entries.Remove(username)
            ? Results.Ok(new { message = $"Username '{username}' unregistered" })
            : Results.NotFound(new { error = $"Username '{username}' not found" });
    }
}
