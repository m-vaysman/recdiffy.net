using RecDiffy.Rules;

namespace RecDiffy.Tests;

// 🔴 decimal.parse reads a comma anywhere as a thousands separator, so "1,5" becomes 15 and "1234,5" becomes 12345,
// with no diagnostic. quantity.absolute no longer does (#150): NumberRead only accepts commas that group thousands.
// DecimalParsing is on main; the fix is to move decimal.parse onto NumberRead by replace and repoint.
public class DecimalParseGroupingTests
{
    [Theory(Skip = "🔴 Known defect: decimal.parse reads a decimal comma as a thousands separator. Remove Skip with the fix.")]
    [InlineData("1,5")]
    [InlineData("12,34")]
    [InlineData("1234,5")]
    public void A_comma_that_does_not_group_thousands_is_Invalid(string raw)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = raw });
        new DecimalParseRule("Qty", "Quantity", 2, MidpointMode.ToEven).Apply(ref row);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }
}
