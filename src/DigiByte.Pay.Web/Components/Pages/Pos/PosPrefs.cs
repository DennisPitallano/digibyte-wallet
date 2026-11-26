using System.Text.Json.Serialization;

namespace DigiByte.Pay.Web.Components.Pages;

/// <summary>
/// Per-tablet display settings for the till (pos-store.js keeps them in localStorage). Applied as data attributes on
/// the POS root, so the layouts live in digipay-pos.css rather than in duplicated markup.
/// </summary>
public sealed class PosPrefs
{
    public static readonly string[] Layouts = ["auto", "split", "stacked", "compact"];
    public static readonly string[] KeypadOrders = ["phone", "calculator"];
    public static readonly string[] Sides = ["right", "left"];
    public static readonly string[] WaitingDisplays = ["standard", "fullscreen"];

    /// <summary>auto (split on a wide landscape screen, stacked otherwise), split, stacked or compact.</summary>
    [JsonPropertyName("layout")] public string Layout { get; set; } = "auto";

    /// <summary>phone (1-2-3 on top) or calculator (7-8-9 on top).</summary>
    [JsonPropertyName("keypad")] public string Keypad { get; set; } = "phone";

    /// <summary>Which side the keypad sits on in the split layout.</summary>
    [JsonPropertyName("side")] public string Side { get; set; } = "right";

    /// <summary>standard, or fullscreen: amount and QR fill the screen while the customer pays.</summary>
    [JsonPropertyName("waiting")] public string Waiting { get; set; } = "standard";

    /// <summary>A short vibration on each key press, where the device supports it.</summary>
    [JsonPropertyName("haptics")] public bool Haptics { get; set; }

    /// <summary>A "00" key for currencies with two decimals (and in place of "." for currencies with none).</summary>
    [JsonPropertyName("doubleZero")] public bool DoubleZero { get; set; }

    /// <summary>Preset amounts per currency code, at most six each.</summary>
    [JsonPropertyName("quickAmounts")] public Dictionary<string, List<decimal>> QuickAmounts { get; set; } = new();

    /// <summary>A copy with every value checked: unknown choices fall back to the defaults, bad amounts are dropped.</summary>
    public static PosPrefs Normalize(PosPrefs? p)
    {
        var n = new PosPrefs();
        if (p is null) return n;
        n.Layout = Pick(p.Layout, Layouts);
        n.Keypad = Pick(p.Keypad, KeypadOrders);
        n.Side = Pick(p.Side, Sides);
        n.Waiting = Pick(p.Waiting, WaitingDisplays);
        n.Haptics = p.Haptics;
        n.DoubleZero = p.DoubleZero;
        foreach (var (code, amounts) in p.QuickAmounts ?? new())
        {
            if (PosEntry.NormalizeCurrency(code) is not { } c || amounts is null) continue;
            var clean = amounts.Select(a => PosEntry.Round(a, c)).Where(a => a > 0).Distinct().Take(6).ToList();
            if (clean.Count > 0) n.QuickAmounts[c] = clean;
        }
        return n;
    }

    public IReadOnlyList<decimal> QuickAmountsFor(string code) =>
        QuickAmounts.TryGetValue(code, out var list) ? list : [];

    private static string Pick(string? value, string[] allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? allowed[0];
}
