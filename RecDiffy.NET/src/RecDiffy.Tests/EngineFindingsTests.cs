using RecDiffy.Results;

namespace RecDiffy.Tests;

// Issue #88: three engine defects found while building RecDiffy.Excel. Each test uses only the public API.
public class EngineFindingsTests
{
    [Fact]
    public void Side_by_side_counts_a_two_leg_package_once()
    {
        var result = Run(
            [Trade("L1", "DELTA", "SWAP-1", "PAY_FIXED", "1000000", "3.5", "278961.22", uti: "U7", package: "PKG-1", leg: "LEG_1_PAY"),
             Trade("L2", "DELTA", "SWAP-1", "REC_FIXED", "1000000", "3.2", "259124.12", uti: "U7", package: "PKG-1", leg: "LEG_2_RECEIVE")],
            [Trade("R1", "DELTA", "SWAP-1", "PAY_FIXED", "1000000", "3.5", "19837.10", uti: "U7")]);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.True(entry.Composition.HasFlag(Composition.Package));

        var cell = Assert.Single(result.BuildSideBySide(new SideBySideSpec(["Counterparty"], [], [new MeasureSpec("Quantity")], null)).Lines).Cells["Quantity"];

        Assert.Equal(1_000_000m, cell.Left);
        Assert.Equal(1_000_000m, cell.Right);
        Assert.Equal(0m, cell.MatchedDelta);
    }

    [Fact]
    public void Group_sum_projection_does_not_add_up_the_price_of_its_allocations()
    {
        var result = Run(
            [Trade("A1", "GAMMA", "EQ-3", "BUY", "300", "10.00", "3000.00"),
             Trade("A2", "GAMMA", "EQ-3", "BUY", "200", "10.00", "2000.00"),
             Trade("A3", "GAMMA", "EQ-3", "BUY", "500", "10.00", "5000.00")],
            [Trade("B1", "GAMMA", "EQ-3", "BUY", "1000", "10.00", "10000.00")]);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(Composition.ManyToOne, entry.Composition);

        Assert.Equal(10.00m, result.Projection(entry.Id, Side.Left)["Price"].Number);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.DoesNotContain(entry.Diffs, diff => diff.Field.Name == "Price");
    }

    [Fact]
    public void A_break_on_a_text_field_has_no_numeric_delta()
    {
        var result = Run(
            [Trade("L1", "ACME", "EQ-1", "BUY", "100", "10.00", "1000.00", uti: "U1", currency: "USD")],
            [Trade("R1", "ACME", "EQ-1", "BUY", "100", "10.00", "1000.00", uti: "U1", currency: "GBP")]);
        var currency = Assert.Single(Assert.Single(result.Entries).Diffs, diff => diff.Field.Name == "Currency");

        Assert.Equal(FieldOutcome.Break, currency.Outcome);
        Assert.Equal(DeltaKind.None, currency.Delta.Kind);
    }

    [Fact]
    public void Side_by_side_still_counts_every_allocation_of_a_group()
    {
        var result = Run(Allocations, Block);
        var cell = Assert.Single(result.BuildSideBySide(new SideBySideSpec(["Counterparty"], [], [new MeasureSpec("Quantity")], null)).Lines).Cells["Quantity"];

        Assert.Equal(1000m, cell.Left);
        Assert.Equal(1000m, cell.Right);
        Assert.Equal(0m, cell.MatchedDelta);
    }

    [Fact]
    public void Side_by_side_counts_an_unmatched_package_once()
    {
        var result = Run(
            [Trade("L1", "DELTA", "SWAP-2", "PAY_FIXED", "500000", "3.5", "100.00", uti: "U9", package: "PKG-2", leg: "LEG_1_PAY"),
             Trade("L2", "DELTA", "SWAP-2", "REC_FIXED", "500000", "3.2", "40.00", uti: "U9", package: "PKG-2", leg: "LEG_2_RECEIVE")],
            []);
        var cell = Assert.Single(result.BuildSideBySide(new SideBySideSpec(["Counterparty"], [], [new MeasureSpec("Quantity")], null)).Lines).Cells["Quantity"];

        Assert.Equal(500_000m, cell.Left);
        Assert.Equal(-500_000m, cell.LeftOnly);
    }

    [Fact]
    public void Group_sum_projection_still_sums_the_conserved_fields()
    {
        var result = Run(Allocations, Block);
        var left = result.Projection(Assert.Single(result.Entries).Id, Side.Left);

        Assert.Equal(1000m, left["Quantity"].Number);
        Assert.Equal(10000.00m, left["NetCash"].Number);
    }

    private static readonly Row[] Allocations =
    [
        Trade("A1", "GAMMA", "EQ-3", "BUY", "300", "10.00", "3000.00"),
        Trade("A2", "GAMMA", "EQ-3", "BUY", "200", "10.00", "2000.00"),
        Trade("A3", "GAMMA", "EQ-3", "BUY", "500", "10.00", "5000.00"),
    ];

    private static readonly Row[] Block = [Trade("B1", "GAMMA", "EQ-3", "BUY", "1000", "10.00", "10000.00")];

    private sealed record Row(string Id, string Counterparty, string Instrument, string Side, string Quantity, string Price, string NetCash, string? Uti, string? Package, string? Leg, string Currency);

    private static Row Trade(string id, string counterparty, string instrument, string side, string quantity, string price, string netCash, string? uti = null, string? package = null, string? leg = null, string currency = "USD") =>
        new(id, counterparty, instrument, side, quantity, price, netCash, uti, package, leg, currency);

    private static ReconciliationResult Run(IReadOnlyList<Row> left, IReadOnlyList<Row> right)
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        return Reconciler.Run(profile, RecordSources.FromObjects("left", left, Map()), RecordSources.FromObjects("right", right, Map()), ReferenceDataSet.Empty);
    }

    private static SourceMap<Row> Map() =>
        new SourceMap<Row>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", row => row.Counterparty)
            .Text("Instrument", row => row.Instrument)
            .Text("Side", row => row.Side)
            .Text("TradeDate", _ => "2026-11-02")
            .Text("Currency", row => row.Currency)
            .Text("Quantity", row => row.Quantity)
            .Text("Price", row => row.Price)
            .Text("NetCash", row => row.NetCash)
            .Text("Uti", row => row.Uti)
            .Text("PackageRef", row => row.Package)
            .Text("LegRole", row => row.Leg);

    private const string Normalization = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" },
            { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
            { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" },
            { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
            { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "Price", "to": "Price" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ]
        }
        """;

    private static string Profile => $$"""
        {
          "formatVersion": 1,
          "profileId": "engine-findings",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Uti", "type": "Identifier", "role": "Identity" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": { "left": {{Normalization}}, "right": {{Normalization}} },
          "consolidation": { "left": { "packages": [ {
            "link": "PackageRef", "role": "LegRole", "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
            "aggregate": { "Quantity": "Same", "Price": "FromRole(LEG_1_PAY)", "Side": "FromRole(LEG_1_PAY)", "NetCash": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)" }
          } ] } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] },
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] }
          ] } ]
        }
        """;
}
