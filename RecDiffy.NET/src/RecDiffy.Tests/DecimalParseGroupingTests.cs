using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class DecimalParseGroupingTests
{
    [Theory]
    [InlineData("1,5")]
    [InlineData("12,34")]
    [InlineData("1234,5")]
    public void A_comma_that_does_not_group_thousands_is_Invalid(string raw)
    {
        var call = new RuleCall(
            "decimal.parse",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = "Quantity" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
        Assert.True(RuleRegistry.CreateCore().TryGet("decimal.parse", out var registration));
        var rule = registration.Create(call, new FieldSchema([new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, 2)]), MidpointMode.ToEven);
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = raw });
        rule.Apply(ref row);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }
}
