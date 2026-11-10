using RecDiffy.Rules;

namespace RecDiffy.Tests;

// identifier.map, as the registry builds it today (InvariantIdentifierMapRule): trimming, blank cells, and how scheme and
// scope separate identifiers that share a value.
public class IdentifierMapInputTests
{
    private static readonly RuleBench Map = new RuleBench("identifier.map").With("from", "Ref").With("to", "Id").With("scheme", "TRADE");

    [Fact]
    public void A_value_is_trimmed_and_interned_under_its_scheme_and_scope()
    {
        var row = Map.With("scope", "desk-1").Apply(("Ref", "  T-100 "));

        Assert.Equal("T-100", row.Display("Id"));
        Assert.Equal(new IdentifierKey("TRADE", "desk-1", "T-100"), row.Pool.Identifier(row.Values["Id"].Interned));
        Assert.Equal(new TraceStep("identifier.map", "Id", "T-100", "T-100"), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("spaces")]
    [InlineData("absent")]
    public void A_blank_cell_is_a_silent_null(string kind)
    {
        var row = kind switch
        {
            "null" => Map.Apply(("Ref", null)),
            "empty" => Map.Apply(("Ref", "")),
            "spaces" => Map.Apply(("Ref", "   ")),
            _ => Map.Apply(("Other", "T-100")),
        };

        Assert.True(row.Values["Id"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void The_same_value_in_two_schemes_is_two_identifiers()
    {
        var row = Both(Map.With("to", "A"), Map.With("to", "B").With("scheme", "ORDER"));

        Assert.NotEqual(row.Values["A"].Interned, row.Values["B"].Interned);
        Assert.Equal(row.Display("A"), row.Display("B"));
    }

    [Fact]
    public void The_same_value_in_two_scopes_is_two_identifiers()
    {
        var row = Both(Map.With("to", "A").With("scope", "desk-1"), Map.With("to", "B").With("scope", "desk-2"));

        Assert.NotEqual(row.Values["A"].Interned, row.Values["B"].Interned);
    }

    [Fact]
    public void A_missing_scope_and_an_empty_scope_are_one_identifier()
    {
        var row = Both(Map.With("to", "A"), Map.With("to", "B").With("scope", ""));

        Assert.Equal(row.Values["A"].Interned, row.Values["B"].Interned);
    }

    private static RowContext Both(RuleBench first, RuleBench second) =>
        second.Apply(first.Apply(("Ref", "T-100")));
}
