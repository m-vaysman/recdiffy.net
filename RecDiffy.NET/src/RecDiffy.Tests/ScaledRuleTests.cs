using System.Globalization;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// quantity.absolute and price.from32nds write their value rounded to the field's scale with the profile midpoint, so the
// match key and the diff see the same number (#170, spec v3 §7.3).
public class ScaledRuleTests
{
    [Fact]
    public void Quantity_absolute_and_price_from32nds_are_registered_to_the_scaled_rules()
    {
        var rules = Rules(MidpointMode.ToEven);

        Assert.IsType<ScaledQuantityRule>(rules.Build("quantity.absolute", Columns("Quantity")));
        Assert.IsType<Scaled32ndsRule>(rules.Build("price.from32nds", Columns("Parsed")));
    }

    [Theory]
    [InlineData("-100.125", MidpointMode.ToEven, "100.12")]
    [InlineData("-100.125", MidpointMode.AwayFromZero, "100.13")]
    [InlineData("100.135", MidpointMode.ToEven, "100.14")]
    [InlineData("-7", MidpointMode.ToEven, "7")]
    public void An_absolute_quantity_is_rounded_to_its_field_scale_with_the_profile_midpoint(string raw, MidpointMode midpoint, string expected)
    {
        var row = Apply(Rules(midpoint).Build("quantity.absolute", Columns("Quantity")), raw);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    // "Parsed" has scale 4 in the test schema. 100-01 is 100.03125, a midpoint at scale 4.
    [Theory]
    [InlineData("100-01", MidpointMode.ToEven, "100.0312")]
    [InlineData("100-01", MidpointMode.AwayFromZero, "100.0313")]
    [InlineData("96-02+", MidpointMode.ToEven, "96.0781")]
    public void A_32nds_price_is_rounded_to_its_field_scale_with_the_profile_midpoint(string raw, MidpointMode midpoint, string expected)
    {
        var row = Apply(Rules(midpoint).Build("price.from32nds", Columns("Parsed")), raw);

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), row.Values["Parsed"].Number);
        Assert.Empty(row.Diagnostics);
    }

    private static SideRules Rules(MidpointMode midpoint) => new(new RuleSettings(midpoint, CultureInfo.InvariantCulture, true));

    private static Dictionary<string, string> Columns(string to) => new(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = to };

    private static RowContext Apply(INormalizationRule rule, string raw)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = raw });
        rule.Apply(ref row);
        return row;
    }
}
