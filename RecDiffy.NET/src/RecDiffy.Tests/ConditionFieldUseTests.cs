using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #220. Spec v3 §5.2: a field "read by a when or route condition" is used, and members that disagree on a used field
// without an aggregator make it unknown "with the warning MembersDisagree naming it". §3.4 lists MembersDisagree as
// "Warning: a used field without an aggregator is unknown on the combined item".
public class PackageConditionFieldTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "package-condition-field",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Uti", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Text" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" }, { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "NetCash": "DifferenceByRole(PAY,REC)" } } ] } },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] } ],
            "diff": { "fields": { "NetCash": { "applicableWhen": { "field": "AssetClass", "equals": "Swap" } } } }
          } ]
        }
        """;

    // The same package, with AssetClass read by a tolerance condition instead of an applicability condition.
    private const string ToleranceProfile = """
        {
          "formatVersion": 1,
          "profileId": "package-tolerance-condition-field",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Uti", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Text" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" }, { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "NetCash": "DifferenceByRole(PAY,REC)" } } ] } },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] } ],
            "diff": { "fields": { "NetCash": { "tolerance": { "absolute": "20", "when": { "field": "AssetClass", "equals": "Swap" } } } } }
          } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Link", row => row.Link).Text("Role", row => row.Role)
        .Text("Uti", row => "UTI-1").Text("Asset", row => row.Asset).Text("Cash", row => row.Cash);

    // AssetClass is read by the NetCash applicableWhen condition, so it is a used field. The two legs disagree on it,
    // and the package's net cash (100 - 20 = 80) differs from the street's 70.
    [Fact]
    public void Legs_that_disagree_on_a_field_a_when_condition_reads_raise_MembersDisagree()
    {
        var result = Run(Profile, [new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Bond", "20")], "Bond");

        var codes = result.Rows.SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code).ToArray();
        Assert.True(codes.Contains("MembersDisagree", StringComparer.Ordinal), Describe(result));
    }

    // Every leg reports the warning, and it names the field.
    [Fact]
    public void The_warning_names_the_field_on_every_leg()
    {
        var result = Run(Profile, [new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Bond", "20")], "Bond");

        Assert.All(result.Rows.Where(row => row.Row.Source == "left"), row =>
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "MembersDisagree", Field: "AssetClass" }));
    }

    // A field read only by a tolerance condition is used too.
    [Fact]
    public void Legs_that_disagree_on_a_field_a_tolerance_condition_reads_raise_MembersDisagree()
    {
        var result = Run(ToleranceProfile, [new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Bond", "20")], "Swap");

        Assert.Contains(result.Rows.SelectMany(row => row.Diagnostics), diagnostic => diagnostic is { Code: "MembersDisagree", Field: "AssetClass" });
    }

    // Legs that agree on the condition field give its value and raise nothing.
    [Fact]
    public void Legs_that_agree_on_a_condition_field_raise_nothing()
    {
        var result = Run(Profile, [new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Swap", "30")], "Swap");

        Assert.Empty(result.Rows.SelectMany(row => row.Diagnostics));
        Assert.Equal(FieldOutcome.Exact, Assert.Single(result.Entries).Outcome);
    }

    private static string Describe(ReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Outcome} [{string.Join(", ", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"))}] "
            + $"codes [{string.Join(",", result.Rows.SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal))}]"));

    private static ReconciliationResult Run(string profile, Row[] left, string streetAsset) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("R1", "", "", streetAsset, "70")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Asset, string Cash);
}

// #220, raw columns. A condition can read a raw column. The package carries a raw column only when its legs agree on it
// (#217); legs that disagree on a raw column a condition reads raise MembersDisagree naming it.
public class PackageRawConditionColumnTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "package-raw-condition-column",
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
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] } ],
            "diff": { "fields": { "NetCash": { "applicableWhen": { "field": "Asset", "equals": "Swap" } } } }
          } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Link", row => row.Link).Text("Role", row => row.Role)
        .Text("Uti", row => "UTI-1").Text("Asset", row => row.Asset).Text("Cash", row => row.Cash);

    [Fact]
    public void Legs_that_disagree_on_a_raw_column_a_condition_reads_raise_MembersDisagree_naming_it()
    {
        var result = Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Bond", "20")]);

        Assert.All(result.Rows.Where(row => row.Row.Source == "left"), row =>
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "MembersDisagree", Field: "Asset" }));
    }

    [Fact]
    public void Legs_that_agree_on_a_raw_column_a_condition_reads_raise_nothing()
    {
        var result = Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Swap", "30")]);

        Assert.Empty(result.Rows.SelectMany(row => row.Diagnostics));
    }

    private static ReconciliationResult Run(Row[] left) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("R1", "", "", "Swap", "70")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Asset, string Cash);
}

// #220, N:1 groups. §5.3: a P3 group's projection follows §5.2's field rules, so a condition field the allocations
// disagree on is unknown with MembersDisagree.
public class GroupConditionFieldTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "group-condition-field",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Text" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty"], "conserve": ["Quantity"] } ],
            "diff": { "fields": { "NetCash": { "applicableWhen": { "field": "AssetClass", "equals": "Swap" } } } }
          } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Cpty", row => "JPM").Text("Asset", row => row.Asset).Text("Qty", row => row.Qty).Text("Cash", row => row.Cash);

    [Fact]
    public void Allocations_that_disagree_on_a_condition_field_raise_MembersDisagree()
    {
        var result = Run([new("A1", "Swap", "50", "500"), new("A2", "Bond", "50", "500")]);

        Assert.All(result.Rows.Where(row => row.Row.Source == "left"), row =>
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "MembersDisagree", Field: "AssetClass" }));
    }

    [Fact]
    public void Allocations_that_agree_on_a_condition_field_raise_nothing()
    {
        var result = Run([new("A1", "Swap", "50", "500"), new("A2", "Swap", "50", "500")]);

        Assert.Empty(result.Rows.SelectMany(row => row.Diagnostics));
    }

    private static ReconciliationResult Run(Row[] left) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("B1", "Swap", "100", "1000")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Asset, string Qty, string Cash);
}
