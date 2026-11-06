using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonDecimalQuarantineTests
{
    [Fact]
    public void DataReaderSource_and_DecimalParse_do_not_throw_on_NaN_infinity_or_huge_double()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Quantity"] = double.NaN });
        var rule = new DecimalParseRule("Quantity", "Quantity", 2, MidpointMode.ToEven);
        var updated = row;
        rule.Apply(ref updated);

        Assert.Contains(updated.Diagnostics, d => d.Code == "DecimalParse");
    }
}
