using System.Text.Json;

namespace DigiByte.Web.Services;

/// <summary>
/// Reads the deployment's <c>config.json</c> (written when the web container starts) so the own-node settings can change without
/// a rebuild (specs/005-wallet-own-node-source/contracts/runtime-config.md). Only the <c>OwnNode</c> section is accepted: the file
/// can never override any other setting. A missing or invalid file simply means "no overrides"; nothing here throws, and no value is logged.
/// </summary>
public static class RuntimeConfigLoader
{
    public const string FileName = "config.json";
    private const string Section = "OwnNode";

    /// <summary>Fetches and parses <c>config.json</c> from the app's own origin. Any failure means no overrides.</summary>
    public static async Task<IReadOnlyDictionary<string, string?>> FetchAsync(HttpClient http)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, FileName);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, string?>();
            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception)
        {
            return new Dictionary<string, string?>();
        }
    }

    /// <summary>Flattens the <c>OwnNode</c> section into configuration keys (<c>OwnNode:Enabled</c>, ...). Anything else is ignored.</summary>
    public static IReadOnlyDictionary<string, string?> Parse(string? json)
    {
        var result = new Dictionary<string, string?>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var section in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(section.Name, Section, StringComparison.OrdinalIgnoreCase) ||
                    section.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var setting in section.Value.EnumerateObject())
                {
                    var text = setting.Value.ValueKind switch
                    {
                        JsonValueKind.String => setting.Value.GetString(),
                        JsonValueKind.Number => setting.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => null,   // objects, arrays and null are not settings
                    };
                    if (text is not null)
                        result[$"{Section}:{setting.Name}"] = text;
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
        return result;
    }
}
