using System.Globalization;

namespace DigiByte.Wallet.Services;

/// <summary>What a typed amount turned out to be.</summary>
public enum AmountEntryKind
{
    /// <summary>Nothing was typed.</summary>
    Empty,
    /// <summary>One clear reading, in <see cref="AmountEntryResult.Amount"/>.</summary>
    Value,
    /// <summary>"1.234"-style input that could mean either reading; the person must pick one.</summary>
    Ambiguous,
    /// <summary>Not a usable amount; the reason is in <see cref="AmountEntryResult.Error"/>.</summary>
    Invalid,
}

/// <summary>Why a typed amount can't be used.</summary>
public enum AmountEntryError
{
    NotANumber,
    MoreThanOneMark,
    TooManyDecimals,
    NotPositive,
    TooLarge,
}

/// <summary>The outcome of <see cref="AmountEntry.Parse"/>.</summary>
public sealed record AmountEntryResult(
    AmountEntryKind Kind,
    decimal? Amount = null,
    decimal? AsDecimal = null,
    decimal? AsThousands = null,
    AmountEntryError? Error = null)
{
    public static readonly AmountEntryResult Empty = new(AmountEntryKind.Empty);
    public static AmountEntryResult Of(decimal amount) => new(AmountEntryKind.Value, Amount: amount);
    public static AmountEntryResult Fail(AmountEntryError error) => new(AmountEntryKind.Invalid, Error: error);
}

/// <summary>
/// Reads an amount a person typed (spec 028, FR-013 / FR-013a; contracts/amount-entry.md).
/// <para>
/// The result never depends on the display language, so the same keystrokes always mean the same amount.
/// A comma or a point may be the decimal mark. More than one mark is refused rather than guessed, and one mark
/// followed by exactly three digits, where it could be a thousands mark ("1.234"), is returned as
/// <see cref="AmountEntryKind.Ambiguous"/> so the person picks. Spaces are ignored, so "1 234,5" is 1234.5.
/// </para>
/// Amounts from payment links and QR codes must not come through here: they have their own format.
/// </summary>
public static class AmountEntry
{
    public static AmountEntryResult Parse(string? text, int maxDecimals, decimal maxValue)
    {
        if (string.IsNullOrWhiteSpace(text)) return AmountEntryResult.Empty;

        // Rule 2: drop every kind of space, including no-break (U+00A0) and narrow no-break (U+202F) spaces.
        var chars = new List<char>(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == ' ' || c == ' ') continue;
            chars.Add(c);
        }
        var s = new string(chars.ToArray());

        // Rule 3: only digits and the two marks.
        var marks = 0;
        var markAt = -1;
        var digits = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is >= '0' and <= '9') { digits++; continue; }
            if (c is ',' or '.') { marks++; markAt = i; continue; }
            return AmountEntryResult.Fail(AmountEntryError.NotANumber);
        }
        if (digits == 0) return AmountEntryResult.Fail(AmountEntryError.NotANumber);

        // Rule 4: never guess between two marks ("1.234,5" or "1,234.5").
        if (marks > 1) return AmountEntryResult.Fail(AmountEntryError.MoreThanOneMark);

        // Rule 5: no mark, a whole number.
        if (marks == 0) return Check(s, "", maxDecimals, maxValue);

        var whole = s[..markAt];
        var fraction = s[(markAt + 1)..];

        // Rule 6: "1.234" / "1,234" could be about one or about one thousand.
        var couldBeThousands = fraction.Length == 3 && whole.Length is >= 1 and <= 3 && whole[0] != '0';
        if (!couldBeThousands) return Check(whole, fraction, maxDecimals, maxValue); // Rule 7

        // Rule 10: if only one reading is a usable amount, that is the answer.
        var asDecimal = Check(whole, fraction, maxDecimals, maxValue);
        var asThousands = Check(whole + fraction, "", maxDecimals, maxValue);
        return (asDecimal.Kind, asThousands.Kind) switch
        {
            (AmountEntryKind.Value, AmountEntryKind.Value) =>
                new AmountEntryResult(AmountEntryKind.Ambiguous, AsDecimal: asDecimal.Amount, AsThousands: asThousands.Amount),
            (AmountEntryKind.Value, _) => asDecimal,
            (_, AmountEntryKind.Value) => asThousands,
            _ => asDecimal,
        };
    }

    /// <summary>Rules 8–10 for one reading: <paramref name="whole"/> and <paramref name="fraction"/> are digits only.</summary>
    private static AmountEntryResult Check(string whole, string fraction, int maxDecimals, decimal maxValue)
    {
        if (fraction.Length > maxDecimals) return AmountEntryResult.Fail(AmountEntryError.TooManyDecimals);

        var number = (whole.Length == 0 ? "0" : whole) + (fraction.Length == 0 ? "" : "." + fraction);
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return AmountEntryResult.Fail(AmountEntryError.TooLarge); // only digits reach here, so a failure is an overflow

        if (value <= 0) return AmountEntryResult.Fail(AmountEntryError.NotPositive);
        if (value > maxValue) return AmountEntryResult.Fail(AmountEntryError.TooLarge);

        return AmountEntryResult.Of(value);
    }
}
