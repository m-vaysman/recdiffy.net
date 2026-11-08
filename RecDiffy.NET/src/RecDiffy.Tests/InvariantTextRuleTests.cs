using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// Text that RecDiffy makes from a typed value is its own format, so it is invariant: a numeric key must read the same
// on every server and on both sides, whatever culture each side is read with (#158).
public class InvariantTextRuleTests
{
    [Fact]
    public void The_text_rules_are_registered_to_their_invariant_replacements()
    {
        var rules = CultureProfile.Rules("en-US");

        Assert.IsType<InvariantIdentifierMapRule>(rules.Build("identifier.map", Strings(("from", "Id"), ("to", "Id"), ("scheme", "Id"))));
        Assert.IsType<InvariantSymbolMapRule>(rules.Build("symbol.map", Strings(("from", "Code"), ("to", "Code"))));
        Assert.IsType<InvariantTextCopyRule>(rules.Build("text.copy", Strings(("from", "Note"), ("to", "Note"))));
        Assert.IsType<CultureSideResolveRule>(rules.Build("side.resolve", Strings(("code", "Dir"), ("quantity", "Qty"))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_numeric_identifier_reads_the_same_on_every_server(string server) =>
        OnServer(server, () =>
        {
            var row = Apply("identifier.map", Strings(("from", "Id"), ("to", "Id"), ("scheme", "Id")), ("Id", 1.5m));

            Assert.Equal("1.5", row.Display("Id"));
        });

    [Fact]
    public void A_numeric_symbol_is_mapped_by_its_invariant_text() =>
        OnServer("de-DE", () =>
        {
            var rules = CultureProfile.Rules("en-US");
            var rule = new InvariantSymbolMapRule("Code", "Code", new Dictionary<string, string>(StringComparer.Ordinal) { ["2.5"] = "HALF" }, passthrough: false);
            var row = Row(("Code", 2.5m));
            rule.Apply(ref row);

            Assert.Equal("HALF", row.Display("Code"));
            Assert.IsType<InvariantSymbolMapRule>(rules.Build("symbol.map", Strings(("from", "Code"), ("to", "Code"))));
        });

    [Fact]
    public void A_copied_number_is_invariant_text() =>
        OnServer("sv-SE", () =>
        {
            var row = Apply("text.copy", Strings(("from", "Note"), ("to", "Note")), ("Note", -5m));

            Assert.Equal("-5", row.Values["Note"].Text);
        });

    [Fact]
    public void A_when_clause_reads_a_raw_number_as_invariant_text() =>
        OnServer("de-DE", () => Assert.Equal("1.5", WhenMatcher.Text(Row(("Flag", 1.5m)), "Flag")));

    [Theory]
    [InlineData("de-DE", "-1.000,50", "SELL")]
    [InlineData("en-US", "-1,000.50", "SELL")]
    [InlineData("de-DE", "1.000,50", "BUY")]
    public void Side_resolve_reads_a_text_quantity_with_the_sides_culture(string culture, string quantity, string side)
    {
        var row = Row(("Dir", ""), ("Qty", quantity));
        CultureProfile.Rules(culture).Build("side.resolve", Strings(("code", "Dir"), ("quantity", "Qty"))).Apply(ref row);

        Assert.Equal(side, row.Display("Side"));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Side_resolve_does_not_read_a_floating_point_quantity()
    {
        var row = Row(("Dir", ""), ("Qty", -2.5d));
        CultureProfile.Rules("en-US").Build("side.resolve", Strings(("code", "Dir"), ("quantity", "Qty"))).Apply(ref row);

        Assert.Null(row.Display("Side"));
        Assert.Equal("UndeterminedSide", Assert.Single(row.Diagnostics).Code);
    }

    private static RowContext Apply(string ruleId, Dictionary<string, string> strings, params (string Column, object? Value)[] cells)
    {
        var row = Row(cells);
        CultureProfile.Rules("en-US").Build(ruleId, strings).Apply(ref row);
        return row;
    }

    private static RowContext Row(params (string Column, object? Value)[] cells) =>
        RowContext.Create("L1", Side.Left, cells.ToDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal));

    private static Dictionary<string, string> Strings(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static void OnServer(string culture, Action test)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
