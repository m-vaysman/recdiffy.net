using RecDiffy.Rules;

namespace RecDiffy.Tests;

// symbol.map, as the registry builds it today (InvariantSymbolMapRule): a mapped code, an unmapped code with and without
// passthrough, and a blank cell.
public class SymbolMapInputTests
{
    private static readonly RuleBench Map = new RuleBench("symbol.map").With("from", "Code").With("to", "Symbol").WithMap("map", ("IBM US", "IBM"), ("IBM.N", "IBM"));

    [Theory]
    [InlineData("IBM US")]
    [InlineData("IBM.N")]
    [InlineData("  IBM US ")]
    public void A_mapped_code_is_written_as_its_symbol(string raw)
    {
        var row = Map.Apply(("Code", raw));

        Assert.Equal("IBM", row.Display("Symbol"));
        Assert.Equal(new TraceStep("symbol.map", "Symbol", raw.Trim(), "IBM"), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("False")]
    [InlineData("0")]
    public void An_unmapped_code_passes_through_unless_passthrough_is_exactly_false(string? passthrough)
    {
        var rule = passthrough is null ? Map : Map.With("passthrough", passthrough);

        var row = rule.Apply(("Code", "MSFT"));

        Assert.Equal("MSFT", row.Display("Symbol"));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Without_passthrough_an_unmapped_code_is_UnknownSymbol()
    {
        var row = Map.With("passthrough", "false").Apply(("Code", " MSFT "));

        Assert.True(row.Values["Symbol"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("UnknownSymbol", DiagnosticSeverity.Invalid, "symbol.map", "Symbol", "MSFT"), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Fact]
    public void Without_passthrough_a_mapped_code_still_maps() =>
        Assert.Equal("IBM", Map.With("passthrough", "false").Apply(("Code", "IBM.N")).Display("Symbol"));

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("spaces")]
    [InlineData("absent")]
    public void A_blank_cell_is_a_silent_null_even_without_passthrough(string kind)
    {
        var rule = Map.With("passthrough", "false");

        var row = kind switch
        {
            "null" => rule.Apply(("Code", null)),
            "empty" => rule.Apply(("Code", "")),
            "spaces" => rule.Apply(("Code", "   ")),
            _ => rule.Apply(("Other", "IBM US")),
        };

        Assert.True(row.Values["Symbol"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void Two_codes_mapped_to_one_symbol_share_one_interned_id()
    {
        var row = Map.With("to", "First").Apply(RuleBench.Row(("Code", "IBM US"), ("Other", "IBM.N")));
        var both = Map.With("from", "Other").With("to", "Second").Apply(row);

        Assert.Equal(both.Values["First"].Interned, both.Values["Second"].Interned);
    }
}
