using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// #224: what each overflow becomes. A subset whose sum leaves the decimal range is not a group. A combined item's sum or
// difference past the range is unknown, with AggregateOverflow naming the field. A tolerance allowance or an attribution
// past the range does not excuse a delta.
public class DecimalOverflowOutcomeTests
{
    private const string Group = """
        {
          "formatVersion": 1,
          "profileId": "decimal-overflow-outcome-group",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty"], "conserve": ["Quantity"] } ] } ]
        }
        """;

    private const string Package = """
        {
          "formatVersion": 1,
          "profileId": "decimal-overflow-outcome-package",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Uti", "type": "Identifier", "role": "Key" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "NetCash": "DifferenceByRole(PAY,REC)" } } ] } },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] } ] } ]
        }
        """;

    private const string Trade = """
        {
          "formatVersion": 1,
          "profileId": "decimal-overflow-outcome-trade",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Quantity": { "tolerance": { "relative": 10 } },
              "Accrued": { "attributed": "trading.proRataToQuantity" },
              "NetCash": { "tolerance": { "attributed": "trading.netCash" } }
            } }
          } ]
        }
        """;

    private const string Big3 = "30000000000000000000000000000";
    private const string Big4 = "40000000000000000000000000000";
    private const string Big5 = "50000000000000000000000000000";
    private const string Big6 = "60000000000000000000000000000";
    private const string Big7 = "70000000000000000000000000000";

    [Fact]
    public void A_subset_whose_sum_leaves_the_range_is_not_a_group()
    {
        var result = Run(Group, [$"Id=L1;Cpty=JPM;Qty={Big5};Cash=1", $"Id=L2;Cpty=JPM;Qty={Big5};Cash=1"], [$"Id=R1;Cpty=JPM;Qty={Big7};Cash=2"]);

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched);
    }

    // 5 + 3 and 5 + 4 (×10²⁸) leave the range; 3 + 4 is the target. The walk skips the first two and finds the third.
    [Fact]
    public void The_group_search_skips_a_count_past_the_range_and_finds_the_subset_that_fits()
    {
        var result = Run(
            Group,
            [$"Id=L1;Cpty=JPM;Qty={Big5};Cash=1", $"Id=L2;Cpty=JPM;Qty={Big3};Cash=1", $"Id=L3;Cpty=JPM;Qty={Big4};Cash=1"],
            [$"Id=R1;Cpty=JPM;Qty={Big7};Cash=2"]);

        var matched = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);
        Assert.Equal(["L2", "L3"], matched.Left.Select(row => row.RowKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, matched.Outcome);
    }

    [Fact]
    public void A_group_sum_past_the_range_is_unknown_and_named_on_each_allocation()
    {
        var result = Run(Group, [$"Id=L1;Cpty=JPM;Qty=50;Cash={Big5}", $"Id=L2;Cpty=JPM;Qty=50;Cash={Big5}"], ["Id=R1;Cpty=JPM;Qty=100;Cash=2"]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Break), (entry.Alignment, entry.Outcome));
        Assert.True(result.Projection(entry.Id, Side.Left)["NetCash"].IsNull);
        AssertOverflowNamed(result, "left", 2);
    }

    [Fact]
    public void A_package_Sum_past_the_range_is_unknown_and_named_on_each_leg()
    {
        var result = Run(
            Package.Replace("DifferenceByRole(PAY,REC)", "Sum", StringComparison.Ordinal),
            [$"Id=A;Link=SWAP-1;Role=PAY;Uti=UTI-1;Cash={Big5}", $"Id=C;Link=SWAP-1;Role=REC;Uti=UTI-1;Cash={Big5}"],
            ["Id=R1;Uti=UTI-1;Cash=70"]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Break), (entry.Alignment, entry.Outcome));
        Assert.True(result.Projection(entry.Id, Side.Left)["NetCash"].IsNull);
        AssertOverflowNamed(result, "left", 2);
    }

    [Fact]
    public void A_package_difference_past_the_range_is_unknown_and_named_on_each_leg()
    {
        var result = Run(
            Package,
            [$"Id=A;Link=SWAP-1;Role=PAY;Uti=UTI-1;Cash={Big7}", $"Id=C;Link=SWAP-1;Role=REC;Uti=UTI-1;Cash=-{Big7}"],
            ["Id=R1;Uti=UTI-1;Cash=70"]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Break), (entry.Alignment, entry.Outcome));
        Assert.True(result.Projection(entry.Id, Side.Left)["NetCash"].IsNull);
        AssertOverflowNamed(result, "left", 2);
    }

    [Fact]
    public void A_relative_allowance_past_the_range_does_not_excuse_the_delta() =>
        Assert.Equal(
            FieldOutcome.Break,
            Outcome(Run(Trade, [$"Id=L1;Ref=T1;Asset=Equity;Qty={Big7};Px=1;Acc=1;Cash=1"], [$"Id=R1;Ref=T1;Asset=Equity;Qty={Big6};Px=1;Acc=1;Cash=1"]), "Quantity"));

    [Fact]
    public void A_pro_rata_expectation_past_the_range_leaves_the_field_unexplained() =>
        Assert.Equal(
            FieldOutcome.Break,
            Outcome(
                Run(Trade,
                    ["Id=L1;Ref=T1;Asset=Bond;Qty=1000000000000000;Px=100;Acc=100000000000000000;Cash=1"],
                    ["Id=R1;Ref=T1;Asset=Bond;Qty=900000000000000;Px=100;Acc=90000000000000000;Cash=1"]),
                "Accrued"));

    [Fact]
    public void A_principal_past_the_range_leaves_net_cash_unexplained() =>
        Assert.Equal(
            FieldOutcome.Break,
            Outcome(
                Run(Trade,
                    ["Id=L1;Ref=T1;Asset=Equity;Qty=1000000000000000;Px=100000000000000;Acc=1;Cash=1"],
                    ["Id=R1;Ref=T1;Asset=Equity;Qty=1000000000000000;Px=100000000000000;Acc=1;Cash=2"]),
                "NetCash"));

    private static void AssertOverflowNamed(IReconciliationResult result, string source, int rows)
    {
        var named = result.Rows.Where(row => row.Row.Source == source).ToArray();
        Assert.Equal(rows, named.Length);
        Assert.All(named, row => Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "AggregateOverflow", Field: "NetCash", Severity: DiagnosticSeverity.Warning }));
    }

    private static FieldOutcome Outcome(IReconciliationResult result, string field) =>
        Assert.Single(Assert.Single(result.Entries).Diffs, diff => diff.Field.Name == field).Outcome;

    private static IReconciliationResult Run(string profile, string[] left, string[] right)
    {
        var columns = left.Concat(right).Select(Parse).SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var map = columns.Aggregate(
            new SourceMap<Dictionary<string, string>>(),
            (current, column) => current.Text(column, row => row.GetValueOrDefault(column, "")));
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromObjects("left", left.Select(Parse), map),
            RecordSources.FromObjects("right", right.Select(Parse), map),
            ReferenceDataSet.Empty);
    }

    private static Dictionary<string, string> Parse(string row) =>
        row.Split(';').Select(cell => cell.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
}
