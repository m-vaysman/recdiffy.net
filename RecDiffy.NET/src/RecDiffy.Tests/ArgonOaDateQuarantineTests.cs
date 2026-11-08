using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonOaDateQuarantineTests
{
    [Fact]
    public void DateParse_rule_does_not_throw_on_out_of_range_oa_date_double()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["TradeDate"] = 1e100d });
#pragma warning disable CS0618 // DateParseRule is replaced by CultureDateRule (#158). This test keeps checking the type it was written for.
        var rule = new DateParseRule("TradeDate", "TradeDate", ["yyyy-MM-dd"], null);
#pragma warning restore CS0618
        var updated = row;
        rule.Apply(ref updated);

        Assert.Contains(updated.Diagnostics, d => d.Code == "DateParse");
    }
}
