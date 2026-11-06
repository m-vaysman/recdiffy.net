using RecDiffy.Results;

namespace RecDiffy.Tests;

// How a group-sum pass combines a non-additive decimal (a rate) depends on how the profile defines it:
// in the pass's block it is equal across the group and kept; outside the block it is summed.
public class GroupSumFieldDefinitionTests
{
    [Fact]
    public void A_rate_in_the_group_block_is_kept_and_the_allocations_match_exactly()
    {
        var result = Run(block: """["Counterparty", "Instrument", "TradeDate", "Side", "FixedRate"]""");
        var entry = Assert.Single(result.Entries);

        Assert.Equal(Composition.ManyToOne, entry.Composition);
        Assert.Equal(4.25m, result.Projection(entry.Id, Side.Left)["FixedRate"].Number);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void A_rate_outside_the_group_block_is_summed_and_breaks()
    {
        var result = Run(block: """["Counterparty", "Instrument", "TradeDate", "Side"]""");
        var entry = Assert.Single(result.Entries);

        Assert.Equal(Composition.ManyToOne, entry.Composition);
        Assert.Equal(12.75m, result.Projection(entry.Id, Side.Left)["FixedRate"].Number);
        Assert.Equal(FieldOutcome.Break, entry.Outcome);
        Assert.Equal("FixedRate", Assert.Single(entry.Diffs).Field.Name);
    }

    [Fact]
    public void Lint_flags_a_compared_decimal_that_a_group_sum_pass_will_sum_without_meaning_to()
    {
        var blocked = Reconciler.Compile(ProfileDocument.Parse(Profile("""["Counterparty", "Instrument", "TradeDate", "Side", "FixedRate"]""")), RuleRegistry.CreateCore());
        var unblocked = Reconciler.Compile(ProfileDocument.Parse(Profile("""["Counterparty", "Instrument", "TradeDate", "Side"]""")), RuleRegistry.CreateCore());

        Assert.Empty(GroupSumLint.SummedButNotAdditive(blocked.Document, Additive));
        Assert.Equal(["trades/P3: FixedRate"], GroupSumLint.SummedButNotAdditive(unblocked.Document, Additive));
    }

    // Fields that are meant to add up across allocations; every other compared decimal must be blocked, keyed or conserved.
    private static readonly IReadOnlySet<string> Additive = new HashSet<string>(StringComparer.Ordinal) { "Fees", "Accrued" };

    private sealed record Row(string Id, string Quantity, string Rate, string NetCash);

    private static ReconciliationResult Run(string block)
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile(block)), RuleRegistry.CreateCore());
        Row[] allocations = [new("A1", "300", "4.25", "3000.00"), new("A2", "200", "4.25", "2000.00"), new("A3", "500", "4.25", "5000.00")];
        Row[] street = [new("B1", "1000", "4.25", "10000.00")];
        return Reconciler.Run(profile, RecordSources.FromObjects("ours", allocations, Map()), RecordSources.FromObjects("theirs", street, Map()), ReferenceDataSet.Empty);
    }

    private static SourceMap<Row> Map() =>
        new SourceMap<Row>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("Instrument", _ => "SWAP-5Y")
            .Text("TradeDate", _ => "2026-11-02")
            .Text("Side", _ => "PAY_FIXED")
            .Text("Quantity", row => row.Quantity)
            .Text("FixedRate", row => row.Rate)
            .Text("NetCash", row => row.NetCash);

    private const string Normalization = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "FixedRate", "to": "FixedRate" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ]
        }
        """;

    private static string Profile(string block) => $$"""
        {
          "formatVersion": 1,
          "profileId": "group-sum-rate",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "FixedRate", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": { "left": {{Normalization}}, "right": {{Normalization}} },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": {{block}}, "conserve": ["Quantity", "NetCash"] }
          ] } ]
        }
        """;
}

// Reads a profile through the public API: in each group-sum pass, a compared decimal (role Key or Economic) that is not
// in the pass's key, block or conserve list is summed across the group. That is right only for fields meant to add up.
internal static class GroupSumLint
{
    public static IReadOnlyList<string> SummedButNotAdditive(ProfileDocument profile, IReadOnlySet<string> additive) =>
        [.. profile.Paths.SelectMany(path => path.Passes
            .Where(pass => pass.Pass == "match.groupSum")
            .SelectMany(pass => profile.Fields
                .Where(field => field is { Type: FieldType.Decimal, Role: FieldRole.Key or FieldRole.Economic })
                .Where(field => pass.Key.Concat(pass.Block).Concat(pass.Conserve).Contains(field.Name) is false && additive.Contains(field.Name) is false)
                .Select(field => $"{path.Id}/{pass.Id}: {field.Name}")))];
}
