using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #40: rounding and tolerance boundaries, through a compiled profile. A value is rounded to its field's scale with the
// profile's midpoint rule when it is read. Keys, group sums and tolerances all see that rounded value, and a tolerance
// bound is inclusive. Delta is right minus left. Each case is chosen so the raw values would give a different answer.
public class RoundingBoundaryTests
{
    private const string PairProfile = """
        {
          "formatVersion": 1,
          "profileId": "rounding-boundaries",
          "version": "1.0.0",
          "culture": "",
          "rounding": { "midpoint": "ToEven" },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }
              ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }
              ]
            }
          },
          "paths": [ {
            "id": "pairs",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Sku"] } ],
            "diff": { "fields": {
              "Price": { "tolerance": { "absolute": "0.01" } },
              "Amount": { "tolerance": { "relative": "0.01" } }
            } }
          } ]
        }
        """;

    // The quantity is part of the key, so the key holds the rounded quantity.
    private const string KeyProfile = """
        {
          "formatVersion": 1,
          "profileId": "rounding-keys",
          "version": "1.0.0",
          "culture": "",
          "rounding": { "midpoint": "ToEven" },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "keys", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Sku", "Quantity"] } ] } ]
        }
        """;

    // Allocations on the left sum to one right row's quantity.
    private const string GroupProfile = """
        {
          "formatVersion": 1,
          "profileId": "rounding-groups",
          "version": "1.0.0",
          "culture": "",
          "rounding": { "midpoint": "ToEven" },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "groups", "route": { "rule": "route.all" }, "passes": [
            { "id": "G", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Sku", row => row.Sku)
        .Text("Quantity", row => row.Quantity)
        .Text("Price", row => row.Price)
        .Text("Amount", row => row.Amount);

    // Price allows 0.01, inclusive. 0.995 and 1.0149 are 0.0199 apart, but read as 1.00 and 1.01. 1.005 and 1.015 are
    // 0.010 apart, but ToEven reads them as 1.00 and 1.02; AwayFromZero reads them as 1.01 and 1.02.
    [Theory]
    [InlineData("ToEven", "100.00", "100.01", FieldOutcome.WithinTolerance, "0.01")]
    [InlineData("ToEven", "100.01", "100.00", FieldOutcome.WithinTolerance, "-0.01")]
    [InlineData("ToEven", "100.00", "100.02", FieldOutcome.Break, "0.02")]
    [InlineData("ToEven", "0.995", "1.0149", FieldOutcome.WithinTolerance, "0.01")]
    [InlineData("ToEven", "1.005", "1.015", FieldOutcome.Break, "0.02")]
    [InlineData("AwayFromZero", "1.005", "1.015", FieldOutcome.WithinTolerance, "0.01")]
    public void An_absolute_tolerance_is_inclusive_and_reads_the_rounded_values(string midpoint, string left, string right, FieldOutcome outcome, string delta)
    {
        var diff = Assert.Single(Pair(midpoint, Prices(left), Prices(right)).Diffs);

        Assert.Equal(("Price", outcome, Number(delta)), (diff.Field.Name, diff.Outcome, diff.Delta.Number));
    }

    // Amount allows 1% of the larger magnitude, inclusive. 98.995 and 100.00 are 1.005 apart, more than 1% of 100, but
    // 98.995 reads as 99.00.
    [Theory]
    [InlineData("99.00", "100.00", FieldOutcome.WithinTolerance, "1.00")]
    [InlineData("100.00", "99.00", FieldOutcome.WithinTolerance, "-1.00")]
    [InlineData("-99.00", "-100.00", FieldOutcome.WithinTolerance, "-1.00")]
    [InlineData("98.99", "100.00", FieldOutcome.Break, "1.01")]
    [InlineData("98.995", "100.00", FieldOutcome.WithinTolerance, "1.00")]
    public void A_relative_tolerance_is_inclusive_and_reads_the_rounded_values(string left, string right, FieldOutcome outcome, string delta)
    {
        var diff = Assert.Single(Pair("ToEven", Amounts(left), Amounts(right)).Diffs);

        Assert.Equal(("Amount", outcome, Number(delta)), (diff.Field.Name, diff.Outcome, diff.Delta.Number));
    }

    // Quantity has no tolerance, so a midpoint either reads the same as its partner or breaks by one unit of scale.
    [Theory]
    [InlineData("ToEven", "2.345", "2.34", FieldOutcome.Exact)]
    [InlineData("ToEven", "2.355", "2.36", FieldOutcome.Exact)]
    [InlineData("ToEven", "-2.345", "-2.34", FieldOutcome.Exact)]
    [InlineData("AwayFromZero", "2.345", "2.35", FieldOutcome.Exact)]
    [InlineData("AwayFromZero", "-2.345", "-2.35", FieldOutcome.Exact)]
    [InlineData("AwayFromZero", "2.345", "2.34", FieldOutcome.Break)]
    public void A_midpoint_is_rounded_with_the_profile_rule_before_it_is_compared(string midpoint, string left, string right, FieldOutcome outcome)
    {
        var entry = Pair(midpoint, Quantities(left), Quantities(right));

        Assert.Equal(outcome, entry.Outcome);
    }

    // 10.005 is a midpoint at scale 2. ToEven keys it as 10.00 and AwayFromZero as 10.01, so it matches only that partner.
    [Theory]
    [InlineData("ToEven", "10.005", "10.00", true)]
    [InlineData("ToEven", "10.005", "10.01", false)]
    [InlineData("AwayFromZero", "10.005", "10.00", false)]
    [InlineData("AwayFromZero", "10.005", "10.01", true)]
    [InlineData("ToEven", "-10.005", "-10.00", true)]
    [InlineData("AwayFromZero", "-10.005", "-10.01", true)]
    [InlineData("ToEven", "1.5", "1.50", true)]
    public void A_key_holds_the_rounded_value(string midpoint, string left, string right, bool matched)
    {
        var result = Run(KeyProfile, midpoint, [Quantities(left) with { Id = "L" }], [Quantities(right) with { Id = "R" }]);

        Assert.Equal(matched, result.Entries.Any(entry => entry.Alignment == Alignment.Matched));
    }

    // Both sets of allocations add up to 100.000 as written. The first reads as 33.33 three times, 99.99, so no group
    // conserves 100.00. The second reads as 33.34, 33.33 and 33.33, which does.
    [Theory]
    [InlineData("33.334", "33.333", "33.333", false)]
    [InlineData("33.335", "33.333", "33.332", true)]
    public void A_group_conserves_the_rounded_values(string first, string second, string third, bool grouped)
    {
        var result = Run(
            GroupProfile,
            "ToEven",
            [Quantities(first) with { Id = "A1" }, Quantities(second) with { Id = "A2" }, Quantities(third) with { Id = "A3" }],
            [Quantities("100.00") with { Id = "BLOCK" }]);

        Assert.Equal(grouped, result.Entries.Any(entry => entry is { Alignment: Alignment.Matched, Composition: Composition.ManyToOne, Left.Length: 3 }));
    }

    private static ResultEntry Pair(string midpoint, Row left, Row right) =>
        Assert.Single(Run(PairProfile, midpoint, [left with { Id = "L" }], [right with { Id = "R" }]).Entries);

    private static IReconciliationResult Run(string profile, string midpoint, Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile.Replace("\"ToEven\"", $"\"{midpoint}\"", StringComparison.Ordinal)), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static Row Prices(string price) => new("", "S", "1", price, "1");

    private static Row Amounts(string amount) => new("", "S", "1", "1", amount);

    private static Row Quantities(string quantity) => new("", "S", quantity, "1", "1");

    private static decimal Number(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private sealed record Row(string Id, string Sku, string Quantity, string Price, string Amount);
}
