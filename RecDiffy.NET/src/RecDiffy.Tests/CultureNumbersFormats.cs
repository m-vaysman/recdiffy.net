using System.Globalization;

namespace RecDiffy.Tests;

// #424: the number formats the grouping tests run over. Named cultures cover what real files use; the built formats
// cover the shapes a named culture may not have on every machine: group sizes of none, zero, a trailing zero and two
// sizes, a separator of two characters, and a no-break-space separator.
internal static class CultureNumbersFormats
{
    public static IReadOnlyDictionary<string, NumberFormatInfo> All { get; } = new Dictionary<string, NumberFormatInfo>(StringComparer.Ordinal)
    {
        ["en-US"] = CultureInfo.GetCultureInfo("en-US").NumberFormat,
        ["sv-SE"] = CultureInfo.GetCultureInfo("sv-SE").NumberFormat,
        ["fr-FR"] = CultureInfo.GetCultureInfo("fr-FR").NumberFormat,
        ["de-CH"] = CultureInfo.GetCultureInfo("de-CH").NumberFormat,
        ["en-IN"] = CultureInfo.GetCultureInfo("en-IN").NumberFormat,
        ["invariant"] = NumberFormatInfo.InvariantInfo,
        ["sizes none"] = Built(",", "."),
        ["sizes 0"] = Built(",", ".", 0),
        ["sizes 3,0"] = Built(",", ".", 3, 0),
        ["sizes 3,2"] = Built(",", ".", 3, 2),
        ["sizes 2"] = Built(",", ".", 2),
        ["separator of two characters"] = Built(", ", ".", 3),
        ["separator no-break space"] = Built(char.ConvertFromUtf32(0x00A0), ",", 3),
        ["separator narrow no-break space"] = Built(char.ConvertFromUtf32(0x202F), ",", 3),
        ["separator full stop"] = Built(".", ",", 3),
    };

    // A stable number per format, for seeding: the position of its name in ordinal order.
    public static int Index(string name) =>
        All.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList().IndexOf(name);

    private static NumberFormatInfo Built(string groupSeparator, string decimalSeparator, params int[] groupSizes)
    {
        var format = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        format.NumberGroupSeparator = groupSeparator;
        format.NumberDecimalSeparator = decimalSeparator;
        format.NumberGroupSizes = groupSizes;
        return format;
    }
}
