using RecDiffy.Results;

namespace RecDiffy.Tests;

// A field in a group-sum pass's block is equal across the group by construction, so its projection keeps it instead of summing it.
// A group-sum pass forms its groups from block only; its key is not used, so a key field is not equal by construction.
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
    public void A_group_sum_pass_key_does_not_change_how_the_projection_combines_a_field()
    {
        var keyed = Run(block: """["Counterparty", "Instrument", "TradeDate", "Side"]""", key: """["FixedRate"]""");
        var unkeyed = Run(block: """["Counterparty", "Instrument", "TradeDate", "Side"]""");
        var keyedEntry = Assert.Single(keyed.Entries);
        var unkeyedEntry = Assert.Single(unkeyed.Entries);

        Assert.Equal(unkeyed.Projection(unkeyedEntry.Id, Side.Left)["FixedRate"].Number, keyed.Projection(keyedEntry.Id, Side.Left)["FixedRate"].Number);
        Assert.Equal(unkeyedEntry.Outcome, keyedEntry.Outcome);
    }

    private sealed record Row(string Id, string Quantity, string Rate, string NetCash);

    private static ReconciliationResult Run(string block, string key = "[]")
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile(block, key)), RuleRegistry.CreateCore());
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

    private static string Profile(string block, string key) => $$"""
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
            { "id": "P3", "pass": "match.groupSum", "many": "left", "key": {{key}}, "block": {{block}}, "conserve": ["Quantity", "NetCash"] }
          ] } ]
        }
        """;
}

