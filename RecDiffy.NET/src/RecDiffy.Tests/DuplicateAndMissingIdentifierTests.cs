using RecDiffy.Results;

namespace RecDiffy.Tests;

// #40: duplicate and missing identifiers, through a compiled profile. A shared reference on two rows of one side is not
// a match on P1: the pass waits, and a later pass or the ambiguity rule decides. A blank reference is missing, not a
// value two rows can share. A missing reference never vetoes; two different shared references always do.
public class DuplicateAndMissingIdentifierTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "identifiers-duplicate-missing",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeRef", "type": "Identifier", "role": "Identity" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.sharedRef" },
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "Quantity"] },
            { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument"] }
          ] } ]
        }
        """;

    private static readonly SourceMap<Trade> Map = new SourceMap<Trade>()
        .Text("Id", trade => trade.Id)
        .Text("Counterparty", trade => trade.Counterparty)
        .Text("Instrument", trade => trade.Instrument)
        .Text("TradeRef", trade => trade.TradeRef)
        .Text("Quantity", trade => trade.Quantity);

    // U-1 is on two left rows, so P1 does not pick one. P2 pairs the row whose economics agree; the twin is left only.
    [Fact]
    public void A_shared_reference_on_two_rows_of_one_side_waits_for_a_later_pass()
    {
        var result = Run(
            [new("L1", "ACME", "BOND1", "U-1", "100"), new("L2", "ACME", "BOND2", "U-1", "200")],
            [new("R1", "ACME", "BOND2", "U-1", "200")]);

        var entry = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);
        Assert.Equal(("left:L2|right:R1", "P2"), (Members(entry), entry.Explanation.CommittedBy));
        Assert.Equal(Disposition.LeftOnly, DispositionOf(result, "left", "L1"));
    }

    // Nothing after P1 tells the two left rows apart, so all three rows are one ambiguous entry and nothing is consumed.
    [Fact]
    public void A_shared_reference_on_two_rows_that_no_pass_separates_is_ambiguous()
    {
        var result = Run(
            [new("L1", "ACME", "BOND1", "U-1", "100"), new("L2", "ACME", "BOND1", "U-1", "100")],
            [new("R1", "ACME", "BOND1", "U-1", "100")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Ambiguous, "left:L1|left:L2|right:R1"), (entry.Alignment, Members(entry)));
        Assert.All(result.Rows, row => Assert.Equal(Disposition.Ambiguous, row.Disposition));
        Assert.Equal(0, result.Summary.Matched);
    }

    // A blank reference is no reference. Were it a value, P1 would pair these two rows on ("ACME", blank).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_references_on_both_sides_are_missing_not_a_shared_value(string? blank)
    {
        var result = Run(
            [new("L1", "ACME", "BOND1", blank, "100")],
            [new("R1", "ACME", "BOND2", blank, "300")]);

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched);
        Assert.Equal((Disposition.LeftOnly, Disposition.RightOnly), (DispositionOf(result, "left", "L1"), DispositionOf(result, "right", "R1")));
    }

    // The quantities differ, so only P1 or the reduced key P4 can pair the rows. P4 applies the shared-reference veto.
    [Theory]
    [InlineData("U-1", "U-1", "P1")]
    [InlineData("U-1", null, "P4")]
    [InlineData(null, "U-1", "P4")]
    [InlineData("U-1", "  ", "P4")]
    [InlineData("U-1", "U-2", null)]
    public void A_missing_reference_never_vetoes_and_two_different_references_always_do(string? leftRef, string? rightRef, string? committedBy)
    {
        var result = Run(
            [new("L1", "ACME", "BOND1", leftRef, "100")],
            [new("R1", "ACME", "BOND1", rightRef, "90")]);

        string?[] expected = committedBy is null ? [] : [committedBy];
        Assert.Equal(expected, result.Entries.Where(entry => entry.Alignment == Alignment.Matched).Select(entry => entry.Explanation.CommittedBy), StringComparer.Ordinal);
    }

    // A blank counterparty is no counterparty, so it is not a key two rows can share on any pass.
    [Fact]
    public void Rows_whose_key_field_is_blank_on_both_sides_are_not_matched_on_it()
    {
        var result = Run(
            [new("L1", "", "BOND1", null, "100")],
            [new("R1", "", "BOND1", null, "100")]);

        Assert.Equal((Disposition.LeftOnly, Disposition.RightOnly), (DispositionOf(result, "left", "L1"), DispositionOf(result, "right", "R1")));
    }

    // Two copies of row key D disagree, so both are quarantined. R1 equals one of them, and is right only: it is neither
    // matched to a quarantined copy nor made ambiguous by the pair of them.
    [Fact]
    public void The_partner_of_a_row_key_whose_copies_conflict_is_right_only()
    {
        var result = Run(
            [new("D", "ACME", "BOND1", null, "100"), new("D", "ACME", "BOND1", null, "200")],
            [new("R1", "ACME", "BOND1", null, "100")]);

        Assert.Equal(
            ["left:D=Quarantined[DuplicateConflict]", "left:D=Quarantined[DuplicateConflict]", "right:R1=RightOnly[]"],
            result.Rows.Select(row => $"{row.Row.Source}:{row.Row.RowKey}={row.Disposition}[{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code))}]").Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static IReconciliationResult Run(Trade[] left, Trade[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static string Members(ResultEntry entry) =>
        string.Join("|", entry.Left.Concat(entry.Right).Select(member => $"{member.Source}:{member.RowKey}").Order(StringComparer.Ordinal));

    private static Disposition DispositionOf(IReconciliationResult result, string source, string rowKey) =>
        result.Rows.Single(row => row.Row.Source == source && row.Row.RowKey == rowKey).Disposition;

    private sealed record Trade(string Id, string Counterparty, string Instrument, string? TradeRef, string Quantity);
}
