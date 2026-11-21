using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// text.regex (#547), built through the core registry the way compile builds it, and applied to one row.
public class RegexTextRuleTests
{
    private static readonly RuleRegistry Registry = RuleRegistry.CreateCore();

    private static readonly ImmutableDictionary<string, string> LeftIds = Args(
        ("from", "TradeId"), ("to", "Key"), ("pattern", @"^TRD-0*(\d+)$"), ("replacement", "$1"));

    [Fact]
    public void Text_regex_is_registered_in_identify_to_the_regex_rule()
    {
        Assert.True(Registry.TryGet("text.regex", out var registration));
        Assert.Equal(RulePhase.Identify, registration.Phase);
        Assert.IsType<RegexTextRule>(Build(LeftIds));
    }

    [Theory]
    [InlineData("TRD-000001", "1")]
    [InlineData("TRD-000640", "640")]
    [InlineData("  TRD-0042 ", "42")]
    [InlineData("TRD-1000000", "1000000")]
    public void A_matching_cell_is_rewritten_to_the_replacement(string raw, string expected)
    {
        var row = Apply(LeftIds, ("TradeId", raw));

        Assert.Equal(expected, row.Display("Key"));
        Assert.Equal(new TraceStep("text.regex", "Key", raw.Trim(), expected), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Two_spellings_of_one_id_share_one_interned_symbol()
    {
        var right = Args(("from", "Other"), ("to", "Second"), ("pattern", @"^trd(\d+)$"), ("replacement", "$1"), ("ignoreCase", "true"));
        var first = Apply(LeftIds, ("TradeId", "TRD-000007"), ("Other", "TRD7"));
        var both = Apply(right, first);

        Assert.Equal("7", both.Display("Second"));
        Assert.Equal(both.Values["Key"].Interned, both.Values["Second"].Interned);
    }

    [Fact]
    public void Matching_is_case_sensitive_unless_ignoreCase_is_true()
    {
        var lower = Args(("from", "TradeId"), ("to", "Key"), ("pattern", @"^trd(\d+)$"), ("replacement", "$1"), ("passthrough", "false"));

        Assert.Equal("PatternNoMatch", Assert.Single(Apply(lower, ("TradeId", "TRD5")).Diagnostics).Code);
        Assert.Equal("5", Apply(lower.SetItem("ignoreCase", "true"), ("TradeId", "TRD5")).Display("Key"));
    }

    [Fact]
    public void An_unmatched_cell_passes_through_by_default()
    {
        var row = Apply(LeftIds, ("TradeId", "ABC-9"));

        Assert.Equal("ABC-9", row.Display("Key"));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Without_passthrough_an_unmatched_cell_is_PatternNoMatch_and_null()
    {
        var row = Apply(LeftIds.SetItem("passthrough", "false"), ("TradeId", " ABC-9 "));

        Assert.True(row.Values["Key"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("PatternNoMatch", DiagnosticSeverity.Invalid, "text.regex", "Key", "ABC-9"), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_cell_is_a_silent_null_even_without_passthrough(string? raw)
    {
        var row = Apply(LeftIds.SetItem("passthrough", "false"), ("TradeId", raw));

        Assert.True(row.Values["Key"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void An_absent_column_is_a_silent_null()
    {
        var row = Apply(LeftIds.SetItem("passthrough", "false"), ("Other", "TRD-000001"));

        Assert.True(row.Values["Key"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_rewrite_that_leaves_nothing_is_a_blank_cell()
    {
        var blankOut = Args(("from", "TradeId"), ("to", "Key"), ("pattern", "^N/A$"), ("replacement", ""), ("passthrough", "false"));

        var row = Apply(blankOut, ("TradeId", "N/A"));

        Assert.True(row.Values["Key"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("upper", "ACME LTD")]
    [InlineData("lower", "acme ltd")]
    public void Case_folds_the_rewritten_value(string textCase, string expected)
    {
        var fold = Args(("from", "Name"), ("to", "Key"), ("pattern", @"\s+"), ("replacement", " "), ("case", textCase));

        Assert.Equal(expected, Apply(fold, ("Name", "Acme   Ltd")).Display("Key"));
    }

    [Fact]
    public void Case_also_folds_a_passed_through_value()
    {
        var fold = Args(("from", "Name"), ("to", "Key"), ("pattern", "^x$"), ("replacement", "y"), ("case", "upper"));

        Assert.Equal("ÄBC", Apply(fold, ("Name", "äbc")).Display("Key"));
    }

    [Fact]
    public void A_number_cell_is_read_as_invariant_text()
    {
        var digits = Args(("from", "Amount"), ("to", "Key"), ("pattern", @"^(\d+)\.(\d+)$"), ("replacement", "$1_$2"));

        Assert.Equal("12_5", Apply(digits, ("Amount", 12.5m)).Display("Key"));
    }

    [Fact]
    public void A_text_target_is_written_as_text()
    {
        var row = Apply(LeftIds.SetItem("to", "Note"), ("TradeId", "TRD-000003"));

        Assert.Equal(CanonicalCell.FromText("3"), row.Values["Note"]);
    }

    private static ImmutableDictionary<string, string> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static INormalizationRule Build(IReadOnlyDictionary<string, string> strings)
    {
        var call = new RuleCall(
            "text.regex",
            null,
            strings,
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);
        var schema = new FieldSchema(
        [
            new FieldDeclaration("Key", FieldType.Symbol, FieldRole.Key, 0),
            new FieldDeclaration("Second", FieldType.Symbol, FieldRole.Key, 0),
            new FieldDeclaration("Note", FieldType.Text, FieldRole.Text, 0),
        ]);
        return Registry.TryGet("text.regex", out var registration)
            ? registration.CreateWithSettings(call, schema, RuleSettings.Invariant(MidpointMode.ToEven))
            : throw new InvalidOperationException("text.regex is not registered.");
    }

    private static RowContext Apply(IReadOnlyDictionary<string, string> strings, params (string Column, object? Value)[] cells) =>
        Apply(strings, RowContext.Create("L1", Side.Left, cells.ToDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal)));

    private static RowContext Apply(IReadOnlyDictionary<string, string> strings, RowContext row)
    {
        var next = row;
        Build(strings).Apply(ref next);
        return next;
    }
}
