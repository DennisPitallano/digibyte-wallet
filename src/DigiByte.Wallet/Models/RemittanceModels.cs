namespace DigiByte.Wallet.Models;

/// <summary>
/// A registered user in the directory — maps a human-readable username
/// or phone number to a DGB address for easy remittances.
/// </summary>
public class DirectoryEntry
{
    public required string Username { get; set; }
    public string? PhoneNumber { get; set; }
    public required string Address { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Fee comparison between DGB and traditional remittance services.
/// </summary>
public class FeeComparison
{
    public required string ServiceName { get; set; }
    public required decimal FeeAmount { get; set; }
    public required string FeeCurrency { get; set; }
    public required decimal FeePercent { get; set; }
    public required TimeSpan EstimatedTime { get; set; }
    public string? LogoUrl { get; set; }
}

public static class RemittanceFeeData
{
    /// <summary>
    /// Sample fee comparison data for a $200 USD transfer.
    /// Based on publicly known average fees for international remittances.
    /// </summary>
    public static List<FeeComparison> GetComparisons(decimal amountUsd)
    {
        return
        [
            new FeeComparison
            {
                ServiceName = "DigiByte",
                FeeAmount = 0.01m, // ~0.0001 DGB at any price
                FeeCurrency = "USD",
                FeePercent = amountUsd > 0 ? Math.Round(0.01m / amountUsd * 100, 4) : 0,
                EstimatedTime = TimeSpan.FromSeconds(15),
            },
            new FeeComparison
            {
                ServiceName = "Western Union",
                FeeAmount = Math.Round(amountUsd * 0.07m, 2),
                FeeCurrency = "USD",
                FeePercent = 7.0m,
                EstimatedTime = TimeSpan.FromMinutes(30),
            },
            new FeeComparison
            {
                ServiceName = "MoneyGram",
                FeeAmount = Math.Round(amountUsd * 0.06m, 2),
                FeeCurrency = "USD",
                FeePercent = 6.0m,
                EstimatedTime = TimeSpan.FromHours(1),
            },
            new FeeComparison
            {
                ServiceName = "PayPal",
                FeeAmount = Math.Round(amountUsd * 0.05m, 2),
                FeeCurrency = "USD",
                FeePercent = 5.0m,
                EstimatedTime = TimeSpan.FromDays(3),
            },
            new FeeComparison
            {
                ServiceName = "Bank Wire",
                FeeAmount = Math.Max(25m, Math.Round(amountUsd * 0.03m, 2)),
                FeeCurrency = "USD",
                FeePercent = amountUsd > 0 ? Math.Round(Math.Max(25m, amountUsd * 0.03m) / amountUsd * 100, 2) : 0,
                EstimatedTime = TimeSpan.FromDays(5),
            },
        ];
    }
}
