using System.Globalization;

namespace DigiByte.Web.Services;

/// <summary>
/// Numbers written into inline CSS. The wallet's culture follows its language (spec 028), and Spanish writes "12,5";
/// CSS only understands "12.5", so any fractional number in a <c>style</c> attribute must go through here.
/// </summary>
public static class Css
{
    public static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
