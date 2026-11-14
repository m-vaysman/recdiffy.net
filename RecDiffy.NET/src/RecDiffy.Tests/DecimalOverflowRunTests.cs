using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// The proof test from #224, from an adversarial pass on dev. Spec §3.1: only an "unreadable source or missing mapped
// column fails the run"; a bad value is a row-level diagnostic. Every amount below is a valid decimal that
// decimal.parse accepts. The run must return a result for each case, as it does for the delta itself
// (ArgonDecimalCompareOverflowTests).
public class DecimalOverflowRunTests
{
    private const string Group = """
        {
          "formatVersion": 1,
          "profileId": "decimal-overflow-group",
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
          "profileId": "decimal-overflow-package",
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
          "profileId": "decimal-overflow-trade",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Quantity": { "tolerance": { "relative": 0.0001 } },
              "Accrued": { "attributed": "trading.proRataToQuantity" },
              "NetCash": { "tolerance": { "attributed": "trading.netCash" } }
            } }
          } ]
        }
        """;

    private const string Big5 = "50000000000000000000000000000";
    private const string Big7 = "70000000000000000000000000000";

    [Fact]
    public void A_group_search_over_large_quantities_returns_a_result() =>
        AssertRuns(Group,
            [$"Id=L1;Cpty=JPM;Qty={Big5};Cash=1", $"Id=L2;Cpty=JPM;Qty={Big5};Cash=1"],
            [$"Id=R1;Cpty=JPM;Qty={Big7};Cash=2"]);

    [Fact]
    public void A_group_whose_unconserved_amounts_are_large_returns_a_result() =>
        AssertRuns(Group,
            [$"Id=L1;Cpty=JPM;Qty=50;Cash={Big5}", $"Id=L2;Cpty=JPM;Qty=50;Cash={Big5}"],
            ["Id=R1;Cpty=JPM;Qty=100;Cash=2"]);

    [Fact]
    public void A_package_Sum_over_large_legs_returns_a_result() =>
        AssertRuns(Package.Replace("DifferenceByRole(PAY,REC)", "Sum", StringComparison.Ordinal),
            [$"Id=A;Link=SWAP-1;Role=PAY;Uti=UTI-1;Cash={Big5}", $"Id=C;Link=SWAP-1;Role=REC;Uti=UTI-1;Cash={Big5}"],
            ["Id=R1;Uti=UTI-1;Cash=70"]);

    [Fact]
    public void A_package_DifferenceByRole_over_large_legs_returns_a_result() =>
        AssertRuns(Package,
            [$"Id=A;Link=SWAP-1;Role=PAY;Uti=UTI-1;Cash={Big7}", $"Id=C;Link=SWAP-1;Role=REC;Uti=UTI-1;Cash=-{Big7}"],
            ["Id=R1;Uti=UTI-1;Cash=70"]);

    [Fact]
    public void A_pro_rata_attribution_over_large_amounts_returns_a_result() =>
        AssertRuns(Trade,
            ["Id=L1;Ref=T1;Asset=Bond;Qty=1000000000000000;Px=100;Acc=100000000000000000;Fee=50;Cash=1"],
            ["Id=R1;Ref=T1;Asset=Bond;Qty=900000000000000;Px=100;Acc=90000000000000000;Fee=50;Cash=2"]);

    [Fact]
    public void A_net_cash_attribution_over_a_large_principal_returns_a_result() =>
        AssertRuns(Trade,
            ["Id=L1;Ref=T1;Asset=Equity;Qty=1000000000000000;Px=100000000000000;Acc=1;Fee=50;Cash=1"],
            ["Id=R1;Ref=T1;Asset=Equity;Qty=1000000000000000;Px=100000000000000;Acc=1;Fee=50;Cash=2"]);

    [Fact]
    public void A_relative_tolerance_over_a_large_quantity_returns_a_result() =>
        AssertRuns(Trade.Replace("\"relative\": 0.0001", "\"relative\": 10", StringComparison.Ordinal),
            [$"Id=L1;Ref=T1;Asset=Equity;Qty={Big7};Px=1;Acc=1;Fee=50;Cash=1"],
            ["Id=R1;Ref=T1;Asset=Equity;Qty=60000000000000000000000000000;Px=1;Acc=1;Fee=50;Cash=1"]);

    private static void AssertRuns(string profile, string[] left, string[] right)
    {
        var failure = Record.Exception(() => Run(profile, left, right));

        Assert.True(failure is null, $"{failure?.GetType().Name}: {failure?.Message} {FirstFrame(failure)}");
    }

    private static string FirstFrame(Exception? failure) =>
        (failure?.StackTrace ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Contains("RecDiffy", StringComparison.Ordinal)) ?? "";

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
