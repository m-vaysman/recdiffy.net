using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// An adversarial pass over decimal.scale (#547). Every test asserts what the rule promises: it reads a number exactly as
// decimal.parse does, multiplies by the factor, rounds to the 'to' field's scale with the profile midpoint, never throws on
// a row, and rejects an unreadable or zero factor at compile time.
public class FactorDecimalRuleAdversarialTests
{
    private static readonly RuleRegistry Registry = RuleRegistry.CreateCore();

    private static readonly string[] ParityCultures = ["en-US", "de-DE", "fr-FR", "sv-SE", ""];

    private static readonly object?[] ParityInputs =
    [
        "1234.56", "1,234.56", "1,5", "1,23,456", "1.234,56", "1.5", "1,5,5", ",5", "1,", " 12 ", "  12.5", "12.5  ", "-5", "+5", "- 5", "5-",
        "(5)", "(1,234.50)", "$5", "â‚¬5", "5 USD", "1 234,56", "1â€¯234,56", "1Â 234,56", "1 234.56", "abc", "  ", "\t", "", null,
        DBNull.Value, true, false, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), 5, -5, 5L, long.MaxValue, 1.5m, 0.005m, 0.015m, 0.025m,
        -0.005m, decimal.MaxValue, decimal.MinValue, (short)7, (byte)7, 0.1d, 0.1f, (Half)0.5, "1e3", "1E-2", "NaN", "Infinity", "âˆž", "0.005", "0.015",
        "-0.005", "1.", ".5", "-.5", "âˆ’5", "0x10", "1_000", "12345678901234567890123456789012", "79228162514264337593543950335",
        "79228162514264337593543950336", "0.00000000000000000000000000005", "00012", "1,000,000", "1,000,00", "1,00", "Ù¡Ù¢Ù£", "1Â 000", "-1,234.5",
    ];

    // ---------------------------------------------------------------- parity with decimal.parse

    [Fact]
    public void With_a_factor_of_one_decimal_scale_reads_every_cell_exactly_as_decimal_parse_does()
    {
        var mismatches = ParityCultures.SelectMany(culture => ParityInputs.Select(input =>
        {
            var parsed = Shape(ApplyRule("decimal.parse", Args(("from", "Qty"), ("to", "Quantity")), input, culture, scale: 2));
            var scaled = Shape(ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1")), input, culture, scale: 2));
            return parsed.Equals(scaled, StringComparison.Ordinal) ? null : $"[{culture}] {Describe(input)}: parse={parsed} scale={scaled}";
        })).OfType<string>().ToArray();

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData("en-US", "1,5")]
    [InlineData("en-US", "1,23")]
    [InlineData("de-DE", "1.5")]
    [InlineData("de-DE", "1.23")]
    public void A_group_separator_in_the_wrong_place_is_DecimalParse_not_a_shifted_number(string culture, string raw)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000")), raw, culture, scale: 0);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }

    [Theory]
    [InlineData("en-US", "1,234.5", 1234500)]
    [InlineData("de-DE", "1.234,5", 1234500)]
    [InlineData("fr-FR", "1234,5", 1234500)]
    [InlineData("", "1234.5", 1234500)]
    public void The_side_culture_decides_which_separator_is_the_decimal_point(string culture, string raw, int expected)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000")), raw, culture, scale: 0);

        Assert.Equal(expected, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData(0.1d)]
    [InlineData(1e300)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_double_is_FloatValue_whatever_its_value(double raw)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "2")), raw, "en-US", scale: 0);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("FloatValue", Assert.Single(row.Diagnostics).Code);
    }

    [Fact]
    public void A_float_and_a_half_are_FloatValue()
    {
        Assert.Equal("FloatValue", Assert.Single(ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "2")), 0.5f, "en-US", scale: 0).Diagnostics).Code);
        Assert.Equal("FloatValue", Assert.Single(ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "2")), (Half)0.5, "en-US", scale: 0).Diagnostics).Code);
    }

    [Fact]
    public void An_absent_column_is_a_silent_null()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        Build("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000")), "en-US", 0, MidpointMode.ToEven).Apply(ref row);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Integer_cells_are_multiplied_as_they_are()
    {
        var args = Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000"));

        Assert.Equal(5000m, ApplyRule("decimal.scale", args, 5, "de-DE", 0).Values["Quantity"].Number);
        Assert.Equal(-5000m, ApplyRule("decimal.scale", args, -5L, "de-DE", 0).Values["Quantity"].Number);
        Assert.Equal(1500m, ApplyRule("decimal.scale", args, 1.5m, "de-DE", 0).Values["Quantity"].Number);
        Assert.Equal(7000m, ApplyRule("decimal.scale", args, (short)7, "de-DE", 0).Values["Quantity"].Number);
    }

    // ---------------------------------------------------------------- factors

    [Theory]
    [InlineData("1000", "2", 2000)]
    [InlineData("1000.00", "2", 2000)]
    [InlineData("+5", "2", 10)]
    [InlineData("-1", "2", -2)]
    [InlineData("-1.0", "2", -2)]
    [InlineData(".5", "4", 2)]
    [InlineData("5.", "2", 10)]
    [InlineData("0.5", "4", 2)]
    [InlineData("0000100", "2", 200)]
    [InlineData("1.00000000000000000000000000", "3", 3)]
    public void A_factor_is_read_as_a_strict_invariant_number(string factor, string raw, int expected)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", factor)), raw, "de-DE", scale: 0);

        Assert.Equal(expected, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("1e3")]
    [InlineData("1E3")]
    [InlineData("1e-3")]
    [InlineData("1.0e+3")]
    [InlineData("1,000")]
    [InlineData("1,5")]
    [InlineData("1 000")]
    [InlineData(" 1000 ")]
    [InlineData(" 1000")]
    [InlineData("1000 ")]
    [InlineData("\t1000")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("+0")]
    [InlineData("0.0")]
    [InlineData("0.0000000000000000000000000000")]
    [InlineData("-0.000")]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("--1")]
    [InlineData("+-1")]
    [InlineData("1-")]
    [InlineData("(1000)")]
    [InlineData("$1000")]
    [InlineData("1000d")]
    [InlineData("0x10")]
    [InlineData("1_000")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("âˆž")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("1.2.3")]
    [InlineData("Ù¡Ù Ù Ù ")]
    public void An_unreadable_or_zero_factor_fails_compile_with_InvalidParameter_and_the_stable_message(string factor)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Build("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", factor)), "en-US", 0, MidpointMode.ToEven));

        Assert.Equal(
            new ProfileError("InvalidParameter", $"decimal.scale factor \"{factor}\" must be a nonzero invariant number such as \"1000\" or \"0.001\"."),
            Assert.Single(failure.Errors));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void The_factor_does_not_follow_the_side_culture(string culture)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "0.5")), "3", culture, scale: 1);

        Assert.Equal(1.5m, row.Values["Quantity"].Number);
        var failure = Assert.Throws<ProfileCompileException>(() => Build("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "0,5")), culture, 1, MidpointMode.ToEven));
        Assert.Equal("InvalidParameter", Assert.Single(failure.Errors).Code);
    }

    [Fact]
    public void A_very_small_factor_with_28_decimal_places_is_exact()
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "0.0000000000000000000000000001")), "5", "en-US", scale: 28);

        Assert.Equal(0.0000000000000000000000000005m, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_factor_with_28_decimal_places_multiplies_exactly()
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1.2345678901234567890123456789")), "1", "en-US", scale: 28);

        Assert.Equal(1.2345678901234567890123456789m, row.Values["Quantity"].Number);
    }

    [Fact]
    public void A_very_large_factor_is_accepted_and_overflows_only_the_rows_it_overflows()
    {
        var args = Args(("from", "Qty"), ("to", "Quantity"), ("factor", "79228162514264337593543950335"));

        Assert.Equal(decimal.MaxValue, ApplyRule("decimal.scale", args, "1", "en-US", 0).Values["Quantity"].Number);
        Assert.Equal("DecimalParse", Assert.Single(ApplyRule("decimal.scale", args, "2", "en-US", 0).Diagnostics).Code);
    }

    [Fact]
    public void A_negative_factor_flips_the_sign_and_the_midpoint_mode_follows_the_flipped_value()
    {
        var args = Args(("from", "Qty"), ("to", "Quantity"), ("factor", "-1"));

        Assert.Equal(-2m, ApplyRule("decimal.scale", args, "2.5", "en-US", 0).Values["Quantity"].Number);
        Assert.Equal(-3m, ApplyRule("decimal.scale", args, "2.5", "en-US", 0, MidpointMode.AwayFromZero).Values["Quantity"].Number);
        Assert.Equal(3m, ApplyRule("decimal.scale", args, "-2.5", "en-US", 0, MidpointMode.AwayFromZero).Values["Quantity"].Number);
        Assert.Equal(2m, ApplyRule("decimal.scale", args, "-2.5", "en-US", 0).Values["Quantity"].Number);
    }

    // ---------------------------------------------------------------- precision and rounding

    [Theory]
    [InlineData("0.4", "5", 2)]
    [InlineData("2.5", "2", 5)]
    [InlineData("0.1", "1000", 100)]
    [InlineData("0.0004", "1000", 0)]
    [InlineData("0.0005", "1000", 0)]
    [InlineData("0.0015", "1000", 2)]
    [InlineData("0.4999", "2", 1)]
    public void The_factor_is_applied_before_the_rounding_to_scale_zero(string raw, string factor, int expected)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", factor)), raw, "en-US", scale: 0);

        Assert.Equal(expected, row.Values["Quantity"].Number);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("5")]
    [InlineData("-1")]
    [InlineData("-3")]
    [InlineData("-5")]
    [InlineData("7")]
    public void A_product_on_the_midpoint_rounds_by_the_profile_midpoint(string raw)
    {
        var args = Args(("from", "Qty"), ("to", "Quantity"), ("factor", "0.5"));
        var even = ApplyRule("decimal.scale", args, raw, "en-US", 0).Values["Quantity"].Number;
        var awayValue = ApplyRule("decimal.scale", args, raw, "en-US", 0, MidpointMode.AwayFromZero).Values["Quantity"].Number;

        Assert.Equal(Mid(raw, MidpointRounding.ToEven), even);
        Assert.Equal(Mid(raw, MidpointRounding.AwayFromZero), awayValue);
    }

    [Theory]
    [InlineData("1.0000000000000000000000000001", "1.0000000000000000000000000001")]
    [InlineData("0.5", "0.0000000000000000000000000001")]
    [InlineData("123456789012345", "1234567890123456")]
    public void A_product_decimal_cannot_hold_exactly_is_DecimalParse_under_either_midpoint(string raw, string factor)
    {
        var args = Args(("from", "Qty"), ("to", "Quantity"), ("factor", factor));

        foreach (var mode in new[] { MidpointMode.ToEven, MidpointMode.AwayFromZero })
        {
            var row = ApplyRule("decimal.scale", args, raw, "en-US", 28, mode);

            Assert.True(row.Values["Quantity"].IsNull);
            Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
        }
    }

    [Theory]
    [InlineData("79228162514264337593543950335", "1")]
    [InlineData("79228162514264337593543950335", "-1")]
    [InlineData("5", "0.0000000000000000000000000001")]
    [InlineData("12345678901234", "12345678901234")]
    public void A_product_decimal_holds_exactly_is_never_rejected_by_the_limit(string raw, string factor)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", factor)), raw, "en-US", 28);

        Assert.Empty(row.Diagnostics);
        Assert.Equal(decimal.Parse(raw, CultureInfo.InvariantCulture) * decimal.Parse(factor, CultureInfo.InvariantCulture), row.Values["Quantity"].Number);
    }

    [Fact]
    public void A_large_exact_product_keeps_every_digit()
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000000000")), "123456789.123456789", "en-US", scale: 0);

        Assert.Equal(123456789123456789m, row.Values["Quantity"].Number);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(28)]
    public void The_result_never_has_more_decimal_places_than_the_field_scale(int scale)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "0.3333333333333333333333333333")), "1.9999999999999999999999999999", "en-US", scale);

        var places = (decimal.GetBits(row.Values["Quantity"].Number)[3] >> 16) & 0xFF;
        Assert.True(places <= scale, $"scale {scale.ToInvariant()} gave {places.ToInvariant()} places");
    }

    [Fact]
    public void The_trace_records_the_raw_text_and_the_rounded_product()
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "0.5")), "5", "en-US", scale: 0);

        Assert.Equal(new TraceStep("decimal.scale", "Quantity", "5", "2"), Assert.Single(row.Traces));
    }

    [Fact]
    public void The_trace_canonical_text_is_invariant_in_every_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1000")), "1.234,5", "de-DE", scale: 1);

            Assert.Equal(new TraceStep("decimal.scale", "Quantity", "1.234,5", "1234500.0"), Assert.Single(row.Traces));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---------------------------------------------------------------- overflow

    [Theory]
    [InlineData("79228162514264337593543950335", "2")]
    [InlineData("79228162514264337593543950335", "-2")]
    [InlineData("-79228162514264337593543950335", "2")]
    [InlineData("-79228162514264337593543950335", "1000")]
    [InlineData("39614081257132168796771975168", "2")]
    [InlineData("10000000000000000000000000000", "10")]
    [InlineData("2", "79228162514264337593543950335")]
    [InlineData("79228162514264337593543950335", "1.0000000000000000000000000001")]
    public void A_text_cell_that_overflows_only_after_multiplication_is_DecimalParse_on_the_row(string raw, string factor)
    {
        var row = ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", factor)), raw, "en-US", scale: 0);

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DecimalParse", DiagnosticSeverity.Invalid, "decimal.scale", "Quantity", raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Fact]
    public void Decimal_cells_at_the_limits_overflow_without_throwing()
    {
        var args = Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1.5"));

        foreach (var limit in new[] { decimal.MaxValue, decimal.MinValue })
        {
            var row = ApplyRule("decimal.scale", args, limit, "en-US", 0);

            Assert.True(row.Values["Quantity"].IsNull);
            Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
        }
    }

    [Fact]
    public void Limits_that_still_fit_are_not_rejected()
    {
        Assert.Equal(decimal.MaxValue, ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1")), decimal.MaxValue, "en-US", 0).Values["Quantity"].Number);
        Assert.Equal(decimal.MinValue, ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "1")), decimal.MinValue, "en-US", 0).Values["Quantity"].Number);
        Assert.Equal(decimal.MinValue, ApplyRule("decimal.scale", Args(("from", "Qty"), ("to", "Quantity"), ("factor", "-1")), decimal.MaxValue, "en-US", 0).Values["Quantity"].Number);
    }

    // ---------------------------------------------------------------- compile

    [Theory]
    [InlineData("[ \"1000\" ]")]
    [InlineData("[]")]
    [InlineData("{ \"a\": \"b\" }")]
    [InlineData("{}")]
    public void A_factor_that_is_a_list_or_an_object_fails_compile_with_a_named_error_not_a_crash(string factorJson)
    {
        var errors = CompileErrors(Document(RightWith(ScaleCall(factorJson))));

        Assert.Contains(errors, error => error.Code is "InvalidParameter" or "MissingParameter" && error.Message.Contains("factor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("from", "[ 'Qty' ]")]
    [InlineData("from", "{ 'a': 'b' }")]
    [InlineData("to", "[ 'Quantity' ]")]
    [InlineData("to", "{ 'a': 'b' }")]
    public void Any_required_parameter_that_is_a_list_or_an_object_fails_compile_with_a_named_error(string name, string json)
    {
        var call = name switch
        {
            "from" => "{ 'rule': 'decimal.scale', 'from': " + json + ", 'to': 'Quantity', 'factor': 1000 }",
            _ => "{ 'rule': 'decimal.scale', 'from': 'Qty', 'to': " + json + ", 'factor': 1000 }",
        };

        var errors = CompileErrors(Document(RightWith(call)));

        Assert.Contains(errors, error => error.Code is "InvalidParameter" or "MissingParameter" && error.Message.Contains(name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("factor")]
    public void A_missing_required_parameter_fails_compile_with_MissingParameter(string missing)
    {
        var parts = new[] { ("from", "'Qty'"), ("to", "'Quantity'"), ("factor", "1000") }.Where(part => part.Item1 != missing).Select(part => "'" + part.Item1 + "': " + part.Item2);
        var call = "{ 'rule': 'decimal.scale', " + string.Join(", ", parts) + " }";

        var errors = CompileErrors(Document(RightWith(call)));

        Assert.Contains(errors, error => error.Code is "MissingParameter" && error.Message.Contains(missing, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    public void A_null_or_boolean_factor_fails_compile_with_a_profile_error(string factorJson)
    {
        var errors = CompileErrors(Document(RightWith(ScaleCall(factorJson))));

        Assert.Contains(errors, error => error.Code is "InvalidParameter" or "MissingParameter" && error.Message.Contains("factor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1e3")]
    [InlineData("1E3")]
    [InlineData("1e-3")]
    [InlineData("1.0e3")]
    [InlineData("0")]
    [InlineData("0.0")]
    [InlineData("-0")]
    [InlineData("-0.0")]
    public void A_json_number_keeps_its_spelling_so_an_exponent_or_a_zero_is_rejected(string factorJson)
    {
        var errors = CompileErrors(Document(RightWith(ScaleCall(factorJson))));

        Assert.Contains(errors, error => error.Code is "InvalidParameter" && error.Message.Contains("nonzero invariant number", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("1000.0")]
    [InlineData("0.001")]
    [InlineData("-1")]
    [InlineData("'1000'")]
    [InlineData("'+5'")]
    public void A_json_number_or_string_with_a_plain_invariant_spelling_compiles(string factorJson)
    {
        Assert.Empty(CompileErrors(Document(RightWith(ScaleCall(factorJson)))));
    }

    [Fact]
    public void A_target_that_is_not_declared_fails_compile_without_a_crash()
    {
        var errors = CompileErrors(Document(RightWith("{ 'rule': 'decimal.scale', 'from': 'Qty', 'to': 'Nowhere', 'factor': 1000 }")));

        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("Symbol", "Economic")]
    [InlineData("Identifier", "Economic")]
    [InlineData("Date", "Economic")]
    [InlineData("Text", "Text")]
    public void A_target_that_is_not_a_Decimal_field_fails_compile_with_InvalidParameter(string type, string role)
    {
        var fields = DefaultFields + ", { 'name': 'Other', 'type': '" + type + "', 'role': '" + role + "' }";
        var json = Document(RightWith("{ 'rule': 'decimal.scale', 'from': 'Qty', 'to': 'Other', 'factor': 1000 }"), fields);

        var errors = CompileErrors(json);

        Assert.Contains(errors, error => error.Code is "InvalidParameter" && error.Message.Contains("decimal.scale", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("identify")]
    [InlineData("derive")]
    [InlineData("validate")]
    public void Decimal_scale_listed_outside_the_parse_phase_is_WrongPhase(string phase)
    {
        var right = phase switch
        {
            "identify" => "'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' }, " + ScaleCall("1000") + " ], 'parse': [ " + ParseCall("Qty", "Quantity") + ", " + ParseCall("Mv", "MarketValue") + " ]",
            _ => "'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' } ], 'parse': [ " + ParseCall("Qty", "Quantity") + ", " + ParseCall("Mv", "MarketValue") + " ], '" + phase + "': [ " + ScaleCall("1000", to: "Other", from: "Qty") + " ]",
        };
        var fields = DefaultFields + ", { 'name': 'Other', 'type': 'Decimal', 'scale': 0, 'role': 'Economic' }";

        var errors = CompileErrors(Document(right, fields));

        Assert.Contains(errors, error => error.Code is "WrongPhase");
    }

    [Fact]
    public void Decimal_scale_and_decimal_parse_writing_one_field_are_AmbiguousWrite()
    {
        var right = "'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' } ], 'parse': [ " + ParseCall("Qty", "Quantity") + ", " + ScaleCall("1000") + ", " + ParseCall("Mv", "MarketValue") + " ]";

        Assert.Contains(CompileErrors(Document(right)), error => error.Code is "AmbiguousWrite");
    }

    [Fact]
    public void Two_decimal_scale_calls_writing_one_field_are_AmbiguousWrite()
    {
        var right = "'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' } ], 'parse': [ " + ScaleCall("1000") + ", " + ScaleCall("1000", from: "Qty2") + ", " + ParseCall("Mv", "MarketValue") + " ]";

        Assert.Contains(CompileErrors(Document(right)), error => error.Code is "AmbiguousWrite");
    }

    [Fact]
    public void A_field_with_no_writer_on_the_scaled_side_is_the_only_thing_decimal_scale_replaces()
    {
        Assert.Empty(CompileErrors(Document(RightWith(ScaleCall("1000")))));
    }

    [Fact]
    public void Compiling_the_same_profile_twice_gives_the_same_hash_in_any_current_culture()
    {
        var json = Document(RightWith(ScaleCall("1000")));
        var first = Compile(json).Hash;
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            Assert.Equal(first, Compile(json).Hash, StringComparer.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("'factor': 1000", "'factor': 100")]
    [InlineData("'factor': 1000", "'factor': -1000")]
    [InlineData("'factor': 1000", "'factor': 1001")]
    public void The_factor_is_part_of_the_profile_hash(string original, string changed)
    {
        var json = Document(RightWith(ScaleCall("1000")));

        Assert.NotEqual(Compile(json).Hash, Compile(json.Replace(original.Replace('\'', '"'), changed.Replace('\'', '"'), StringComparison.Ordinal)).Hash, StringComparer.Ordinal);
    }

    [Fact]
    public void The_from_and_to_names_are_part_of_the_profile_hash()
    {
        var json = Document(RightWith(ScaleCall("1000")));
        var other = Document(RightWith(ScaleCall("1000", from: "Qty2")));

        Assert.NotEqual(Compile(json).Hash, Compile(other).Hash, StringComparer.Ordinal);
    }

    [Fact]
    public void Decimal_parse_and_decimal_scale_with_a_factor_of_one_give_different_profile_hashes()
    {
        var scaled = Document(RightWith(ScaleCall("1")));
        var plain = Document("'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' } ], 'parse': [ " + ParseCall("Qty", "Quantity") + ", " + ParseCall("Mv", "MarketValue") + " ]");

        Assert.NotEqual(Compile(scaled).Hash, Compile(plain).Hash, StringComparer.Ordinal);
    }

    [Fact]
    public void The_two_spellings_of_one_factor_give_the_same_reconciliation_result()
    {
        var a = Run(Document(RightWith(ScaleCall("1000"))), [("L-1", "3700", "37000")], [("L-1", "3.7", "37")]);
        var b = Run(Document(RightWith(ScaleCall("'1000.00'"))), [("L-1", "3700", "37000")], [("L-1", "3.7", "37")]);

        Assert.Equal(Summary(a), Summary(b));
    }

    // ---------------------------------------------------------------- engine

    [Fact]
    public void P07_thousands_on_the_right_match_the_units_on_the_left_for_quantity_and_market_value()
    {
        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), P07Left, P07RightInThousands);

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.All(result.Entries, entry => Assert.Equal(FieldOutcome.Exact, entry.Outcome));
        Assert.Equal(P07Left.Length, result.Entries.Length);
    }

    [Fact]
    public void P07_gives_the_same_result_in_any_row_order()
    {
        var profile = Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv")));
        var ordered = Run(profile, P07Left, P07RightInThousands);
        var reversed = Run(profile, [.. P07Left.Reverse()], [.. P07RightInThousands.Reverse()]);
        var rotated = Run(profile, [.. P07Left.Skip(2), .. P07Left.Take(2)], [.. P07RightInThousands.Skip(1), .. P07RightInThousands.Take(1)]);

        Assert.Equal(Summary(ordered), Summary(reversed));
        Assert.Equal(Summary(ordered), Summary(rotated));
    }

    [Fact]
    public void A_real_difference_in_quantity_is_a_numeric_break_with_the_delta_in_units()
    {
        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), [("L-1", "3700", "37000")], [("L-1", "3.6", "37")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Break, entry.Outcome);
        var diff = Assert.Single(entry.Diffs, candidate => candidate.Field.Name is "Quantity");
        Assert.Equal(-100m, diff.Delta.Number);
        Assert.DoesNotContain(entry.Diffs, candidate => candidate.Field.Name is "MarketValue");
    }

    [Fact]
    public void A_real_difference_in_market_value_is_a_break_with_the_delta_in_units()
    {
        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), [("L-1", "3700", "37000.00")], [("L-1", "3.7", "37.5")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Break, entry.Outcome);
        Assert.Equal(500m, Assert.Single(entry.Diffs, candidate => candidate.Field.Name is "MarketValue").Delta.Number);
    }

    [Theory]
    [InlineData("37.00001", FieldOutcome.WithinTolerance)]
    [InlineData("36.99999", FieldOutcome.WithinTolerance)]
    [InlineData("37", FieldOutcome.Exact)]
    [InlineData("37.00002", FieldOutcome.Break)]
    [InlineData("36.99998", FieldOutcome.Break)]
    public void An_inclusive_tolerance_applies_to_the_scaled_and_rounded_value(string right, FieldOutcome expected)
    {
        var diff = ",'diff': { 'fields': { 'MarketValue': { 'tolerance': { 'absolute': '0.01' } } } }";
        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv")), diffJson: diff), [("L-1", "3700", "37000.00")], [("L-1", "3.7", right)]);

        Assert.Equal(expected, Assert.Single(result.Entries).Outcome);
    }

    [Theory]
    [InlineData("3.7004", FieldOutcome.Exact)]
    [InlineData("3.7006", FieldOutcome.Break)]
    public void The_tolerance_is_checked_after_the_rounding_to_the_field_scale(string right, FieldOutcome expected)
    {
        var diff = ",'diff': { 'fields': { 'Quantity': { 'tolerance': { 'absolute': '0.5' } } } }";
        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv")), diffJson: diff), [("L-1", "3700", "37000.00")], [("L-1", right, "37")]);

        Assert.Equal(expected, Assert.Single(result.Entries).Outcome);
    }

    [Fact]
    public void The_profile_midpoint_decides_a_scaled_midpoint_end_to_end()
    {
        var right = RightWith(ScaleCall("0.5"), ScaleCall("1000", to: "MarketValue", from: "Mv"));
        var even = Run(Document(right), [("L-1", "2", "37000.00")], [("L-1", "5", "37")]);
        var away = Run(Document(right, rootExtra: "'rounding': { 'midpoint': 'AwayFromZero' },"), [("L-1", "3", "37000.00")], [("L-1", "5", "37")]);

        Assert.Equal(FieldOutcome.Exact, Assert.Single(even.Entries).Outcome);
        Assert.Equal(FieldOutcome.Exact, Assert.Single(away.Entries).Outcome);
    }

    [Fact]
    public void An_overflowing_row_does_not_disturb_the_other_rows()
    {
        var left = new[] { ("L-1", "3700", "37000"), ("L-2", "1", "1"), ("L-3", "100", "1025") };
        var right = new[] { ("L-1", "3.7", "37"), ("L-2", "79228162514264337593543950335", "1"), ("L-3", "0.1", "1.025") };

        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), left, right);

        Assert.Equal(1, result.Rows.SelectMany(row => row.Diagnostics).Count(diagnostic => diagnostic.Code is "DecimalParse"));
        Assert.Equal(2, result.Entries.Count(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact }));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    public void A_declared_culture_gives_the_same_result_whatever_the_machine_culture(string machine)
    {
        var rows = (Left: new[] { ("L-1", "1234500", "1234500.00"), ("L-2", "100", "1025") }, Right: new[] { ("L-1", "1,234.5", "1,234.5"), ("L-2", "0.1", "1.025") });
        var baseline = Summary(Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), rows.Left, rows.Right));

        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(machine);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(machine);

            var shifted = Summary(Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), rows.Left, rows.Right));

            Assert.Equal(baseline, shifted);
            Assert.Equal(["Exact", "Exact"], baseline.Select(line => line.Split('|')[0]).ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void A_negative_factor_end_to_end_flips_the_sign_before_comparison()
    {
        var result = Run(Document(RightWith(ScaleCall("-1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), [("L-1", "-3700", "37000")], [("L-1", "3.7", "37")]);

        Assert.Equal(FieldOutcome.Exact, Assert.Single(result.Entries).Outcome);
    }

    [Fact]
    public void A_blank_scaled_cell_against_a_value_is_not_a_silent_match()
    {
        var result = Run(Document(RightWith(ScaleCall("1000"), ScaleCall("1000", to: "MarketValue", from: "Mv"))), [("L-1", "3700", "37000")], [("L-1", "", "37")]);

        Assert.NotEqual(FieldOutcome.Exact, Assert.Single(result.Entries).Outcome);
    }

    // ---------------------------------------------------------------- scaffolding

    private static readonly (string, string, string)[] P07Left =
    [
        ("L-1", "3700", "37000.00"), ("L-2", "100", "1025.00"), ("L-3", "1234500", "9999999.99"), ("L-4", "0", "0"), ("L-5", "-250", "-12.50"),
    ];

    private static readonly (string, string, string)[] P07RightInThousands =
    [
        ("L-1", "3.7", "37"), ("L-2", "0.1", "1.025"), ("L-3", "1,234.5", "9,999.99999"), ("L-4", "0", "0"), ("L-5", "-0.25", "-0.0125"),
    ];

    private const string DefaultFields =
        "{ 'name': 'Lot', 'type': 'Symbol', 'role': 'Key' }, { 'name': 'Quantity', 'type': 'Decimal', 'scale': 0, 'role': 'Economic' }, { 'name': 'MarketValue', 'type': 'Decimal', 'scale': 2, 'role': 'Economic' }";

    private const string LeftNormalization =
        "'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' } ], 'parse': [ { 'rule': 'decimal.parse', 'from': 'Qty', 'to': 'Quantity' }, { 'rule': 'decimal.parse', 'from': 'Mv', 'to': 'MarketValue' } ]";

    private static string ParseCall(string from, string to) => "{ 'rule': 'decimal.parse', 'from': '" + from + "', 'to': '" + to + "' }";

    private static string ScaleCall(string factorJson, string to = "Quantity", string from = "Qty") =>
        "{ 'rule': 'decimal.scale', 'from': '" + from + "', 'to': '" + to + "', 'factor': " + factorJson + " }";

    // The right side with the given calls in parse. A market-value call is added when only one call is given.
    private static string RightWith(params string[] calls) =>
        "'identify': [ { 'rule': 'symbol.map', 'from': 'LotId', 'to': 'Lot' } ], 'parse': [ "
        + string.Join(", ", calls.Length == 1 && calls[0].Contains("'to': 'Quantity'", StringComparison.Ordinal) ? [calls[0], ParseCall("Mv", "MarketValue")] : calls)
        + " ]";

    private static string Document(string rightNormalization, string fields = DefaultFields, string rootExtra = "", string diffJson = "") =>
        ("{ 'formatVersion': 1, 'profileId': 'adversarial-scale', 'version': '1.0.0', 'culture': 'en-US', " + rootExtra
            + "'sources': { 'left': { 'rowKey': 'LotId' }, 'right': { 'rowKey': 'LotId' } }, 'fields': [ " + fields + " ], "
            + "'normalization': { 'left': { " + LeftNormalization + " }, 'right': { " + rightNormalization + " } }, "
            + "'paths': [ { 'id': 'all', 'route': { 'rule': 'route.all' }, 'passes': [ { 'id': 'P1', 'pass': 'match.naturalKey', 'key': [ 'Lot' ] } ]" + diffJson + " } ] }")
        .Replace('\'', '"');

    private static CompiledProfile Compile(string json) => Reconciler.Compile(ProfileDocument.Parse(json), Registry);

    private static IReadOnlyList<ProfileError> CompileErrors(string json)
    {
        try
        {
            return ProfileCompiler.Compile(ProfileDocument.Parse(json), Registry, CultureInfo.InvariantCulture).Errors;
        }
        catch (ProfileCompileException failure)
        {
            return failure.Errors;
        }
    }

    private static IReconciliationResult Run(string json, (string LotId, string Qty, string Mv)[] left, (string LotId, string Qty, string Mv)[] right)
    {
        var map = new SourceMap<(string LotId, string Qty, string Mv)>().Text("LotId", row => row.LotId).Text("Qty", row => row.Qty).Text("Mv", row => row.Mv);
        return Reconciler.Run(
            Compile(json),
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);
    }

    private static string[] Summary(IReconciliationResult result) =>
        [.. result.Entries
            .Select(entry => entry.Outcome.ToString() + "|" + string.Join(",", entry.Diffs.OrderBy(diff => diff.Field.Name, StringComparer.Ordinal).Select(diff => diff.Field.Name + "=" + diff.Delta.Number.ToInvariant())))
            .Order(StringComparer.Ordinal)];

    private static decimal Mid(string raw, MidpointRounding rounding) =>
        decimal.Round(decimal.Parse(raw, CultureInfo.InvariantCulture) * 0.5m, 0, rounding);

    private static string Describe(object? input) => input is null ? "null" : input.GetType().Name + ":" + Convert.ToString(input, CultureInfo.InvariantCulture);

    private static string Shape(RowContext row)
    {
        var cell = row.Values["Quantity"];
        var value = cell.IsNull ? "null" : cell.Number.ToString(CultureInfo.InvariantCulture);
        var diagnostics = string.Join(";", row.Diagnostics.Select(diagnostic => $"{diagnostic.Code}/{diagnostic.Severity}/{diagnostic.Field}/{diagnostic.RawText}"));
        var traces = string.Join(";", row.Traces.Select(trace => $"{trace.Field}/{trace.Raw}/{trace.Canonical}"));
        return $"{value} | {diagnostics} | {traces}";
    }

    private static ImmutableDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static INormalizationRule Build(string ruleId, IReadOnlyDictionary<string, string> strings, string culture, int scale, MidpointMode midpoint)
    {
        var call = new RuleCall(
            ruleId,
            null,
            strings,
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);
        var schema = new FieldSchema([new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, scale)]);
        var settings = new RuleSettings(midpoint, CultureInfo.GetCultureInfo(culture), StrictDateFormats: true);
        return Registry.TryGet(ruleId, out var registration)
            ? registration.CreateWithSettings(call, schema, settings)
            : throw new InvalidOperationException(ruleId + " is not registered.");
    }

    private static RowContext ApplyRule(string ruleId, IReadOnlyDictionary<string, string> strings, object? cell, string culture, int scale, MidpointMode midpoint = MidpointMode.ToEven)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = cell });
        Build(ruleId, strings, culture, scale, midpoint).Apply(ref row);
        return row;
    }
}
