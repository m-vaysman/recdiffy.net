using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// party.resolveLei and party.resolveAlias (PartyLookup): a counterparty code from reference data, written as a PARTY
// identifier. The alias rule only fills a counterparty the LEI rule left empty.
public class PartyLookupTests
{
    private const string AcmeLei = "5493001KJTIIGC8Y1R12";

    private static readonly RuleBench Lei = new RuleBench("party.resolveLei").With("from", "Lei").With("to", "Counterparty");

    private static readonly RuleBench Alias = new RuleBench("party.resolveAlias").With("from", "Name").With("to", "Counterparty");

    [Theory]
    [InlineData(AcmeLei)]
    [InlineData("  " + AcmeLei + " ")]
    public void A_known_LEI_is_written_as_its_party_code(string lei)
    {
        var row = Lei.Apply(Row(("Lei", lei)));

        Assert.Equal(new IdentifierKey("PARTY", "", "ACME"), row.Pool.Identifier(row.Values["Counterparty"].Interned));
        Assert.Equal(new TraceStep("party.resolveLei", "Counterparty", AcmeLei, "ACME"), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("party.resolveLei", "Lei", "UNKNOWNLEI0000000000")]
    [InlineData("party.resolveLei", "Lei", "5493001kjtiigc8y1r12")]
    [InlineData("party.resolveAlias", "Name", "Acme Corporation")]
    [InlineData("party.resolveAlias", "Name", "acme corp")]
    public void An_unknown_LEI_or_alias_is_UnknownParty(string rule, string column, string raw)
    {
        var bench = rule == "party.resolveLei" ? Lei : Alias;

        var row = bench.Apply(Row((column, raw)));

        Assert.False(row.Values.ContainsKey("Counterparty"));
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("UnknownParty", DiagnosticSeverity.Invalid, rule, "Counterparty", raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Theory]
    [InlineData("party.resolveLei", "Lei", null)]
    [InlineData("party.resolveLei", "Lei", "")]
    [InlineData("party.resolveAlias", "Name", null)]
    [InlineData("party.resolveAlias", "Name", " ")]
    [InlineData("party.resolveLei", "Other", "x")]
    public void A_blank_LEI_or_alias_leaves_the_row_unchanged(string rule, string column, string? raw)
    {
        var row = (rule == "party.resolveLei" ? Lei : Alias).Apply(Row((column, raw)));

        Assert.Empty(row.Values);
        Assert.Empty(row.Diagnostics);
        Assert.Empty(row.Traces);
    }

    [Fact]
    public void A_known_alias_fills_an_empty_counterparty()
    {
        var row = Alias.Apply(Lei.Apply(Row(("Lei", ""), ("Name", "Acme Corp"))));

        Assert.Equal("ACME", row.Display("Counterparty"));
        Assert.Equal("party.resolveAlias", Assert.Single(row.Traces).RuleId);
    }

    [Fact]
    public void An_alias_does_not_replace_a_counterparty_the_LEI_resolved()
    {
        var row = Alias.Apply(Lei.Apply(Row(("Lei", AcmeLei), ("Name", "Unknown Name"))));

        Assert.Equal("ACME", row.Display("Counterparty"));
        Assert.Empty(row.Diagnostics);
        Assert.Equal("party.resolveLei", Assert.Single(row.Traces).RuleId);
    }

    [Fact]
    public void The_LEI_and_the_alias_of_one_party_intern_to_one_identifier()
    {
        var byLei = Lei.With("to", "ByLei").Apply(Row(("Lei", AcmeLei), ("Name", "Acme Corp")));

        var row = Alias.With("to", "ByAlias").Apply(byLei);

        Assert.Equal(row.Values["ByLei"].Interned, row.Values["ByAlias"].Interned);
    }

    private static RowContext Row(params (string Column, object? Value)[] cells) =>
        RuleBench.Row(cells) with
        {
            Reference = ReferenceDataSet.Create(
                "parties",
                new DateOnly(2025, 1, 1),
                "parties",
                new Dictionary<string, string>(StringComparer.Ordinal) { [AcmeLei] = "ACME" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Acme Corp"] = "ACME" },
                ImmutableDictionary<string, string>.Empty),
        };
}
