using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// decimal.scale (#547), built through the core registry the way compile builds it, and applied to one row.
public class FactorDecimalRuleTests
{
    private static readonly RuleRegistry Registry = RuleRegistry.CreateCore();

    private static readonly ImmutableDictionary<string, string> Thousands = Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000"));

    [Fact]
    public void Decimal_scale_is_registered_in_parse_to_the_factor_rule()
    {
        Assert.True(Registry.TryGet("decimal.scale", out var registration));
        Assert.Equal(RulePhase.Parse, registration.Phase);
        Assert.IsType<FactorDecimalRule>(Build(Thousands, scale: 0));
    }

    [Theory]
    [InlineData("0.1", "100")]
    [InlineData("3.7", "3700")]
    [InlineData("4", "4000")]
    [InlineData("-0.25", "-250")]
    [InlineData("1,234.5", "1234500")]
    public void A_number_is_multiplied_by_the_factor(string raw, string expected)
    {
        var row = Apply(Thousands, 0, ("Qty", raw));

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), row.Values["Quantity"].Number);
        Assert.Equal(new TraceStep("decimal.scale", "Quantity", raw, expected), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void The_factor_is_applied_before_rounding_to_the_field_scale()
    {
        Assert.Equal(100m, Apply(Thousands, 0, ("Qty", "0.1")).Values["Quantity"].Number);
        Assert.Equal(0.002m, Apply(Thousands.SetItem("factor", "0.001"), 3, ("Qty", "1.5")).Values["Quantity"].Number);
    }

    [Fact]
    public void The_profile_midpoint_rounds_the_product()
    {
        var byFactor = Thousands.SetItem("factor", "0.001");

        Assert.Equal(0.002m, Apply(byFactor, 3, ("Qty", "2.5")).Values["Quantity"].Number);
        Assert.Equal(0.003m, Apply(byFactor, 3, ("Qty", "2.5"), MidpointMode.AwayFromZero).Values["Quantity"].Number);
    }

    [Fact]
    public void A_number_is_read_in_the_side_culture()
    {
        var row = Apply(Thousands, 0, ("Qty", "1.234,5"), culture: "de-DE");

        Assert.Equal(1234500m, row.Values["Quantity"].Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_blank_cell_is_a_silent_null(string? raw)
    {
        var row = Apply(Thousands, 0, ("Qty", raw));

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Text_that_is_not_a_number_is_DecimalParse()
    {
        var row = Apply(Thousands, 0, ("Qty", "abc"));

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal(("DecimalParse", DiagnosticSeverity.Invalid, "decimal.scale", "Quantity", "abc"), Shape(Assert.Single(row.Diagnostics)));
    }

    [Fact]
    public void A_double_is_FloatValue()
    {
        var row = Apply(Thousands, 0, ("Qty", 0.1d));

        Assert.Equal("FloatValue", Assert.Single(row.Diagnostics).Code);
    }

    [Fact]
    public void A_product_outside_the_decimal_range_is_DecimalParse_not_a_crash()
    {
        var row = Apply(Thousands, 0, ("Qty", decimal.MaxValue));

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.000")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("thousand")]
    [InlineData("")]
    public void A_zero_or_unreadable_factor_fails_compile_with_InvalidParameter(string factor)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Build(Thousands.SetItem("factor", factor), scale: 0));

        Assert.Equal(
            new ProfileError("InvalidParameter", $"decimal.scale factor \"{factor}\" must be a nonzero invariant number such as \"1000\" or \"0.001\"."),
            Assert.Single(failure.Errors));
    }

    [Theory]
    [InlineData(FieldType.Symbol)]
    [InlineData(FieldType.Text)]
    [InlineData(FieldType.Date)]
    public void A_target_that_is_not_decimal_fails_compile_with_InvalidParameter(FieldType type)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Build(Thousands, scale: 0, type));

        Assert.Equal("InvalidParameter", Assert.Single(failure.Errors).Code);
    }

    private static (string, DiagnosticSeverity, string, string?, string?) Shape(Diagnostic diagnostic) =>
        (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText);

    private static ImmutableDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static INormalizationRule Build(
        IReadOnlyDictionary<string, string> strings,
        int scale,
        FieldType type = FieldType.Decimal,
        MidpointMode midpoint = MidpointMode.ToEven,
        string culture = "")
    {
        var call = new RuleCall(
            "decimal.scale",
            null,
            strings,
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);
        var schema = new FieldSchema([new FieldDeclaration("Quantity", type, FieldRole.Economic, scale)]);
        var settings = new RuleSettings(midpoint, CultureInfo.GetCultureInfo(culture), StrictDateFormats: true);
        return Registry.TryGet("decimal.scale", out var registration)
            ? registration.CreateWithSettings(call, schema, settings)
            : throw new InvalidOperationException("decimal.scale is not registered.");
    }

    private static RowContext Apply(
        IReadOnlyDictionary<string, string> strings,
        int scale,
        (string Column, object? Value) cell,
        MidpointMode midpoint = MidpointMode.ToEven,
        string culture = "")
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { [cell.Column] = cell.Value });
        Build(strings, scale, midpoint: midpoint, culture: culture).Apply(ref row);
        return row;
    }
}
