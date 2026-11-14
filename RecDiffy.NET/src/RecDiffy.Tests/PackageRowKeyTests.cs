using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #217, case 1. Spec v3 §5.2: "No value ever comes from 'the first row'", and a used field takes the members' agreed
// value or is unknown with the warning MembersDisagree. §10.1: "No decision reads RowKey", and after an ID scramble
// "the results must be identical" (AC-7).
public class PackageRoleRowKeyTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "package-role-row-keys",
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

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Link", row => row.Link).Text("Role", row => row.Role)
        .Text("Uti", row => "UTI-1").Text("Asset", row => row.Asset).Text("Cash", row => row.Cash);

    // Two legs carry the PAY role with different amounts. Swapping their row keys is an ID scramble.
    [Fact]
    public void A_package_with_two_legs_in_one_role_does_not_depend_on_row_keys()
    {
        var original = Describe(Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("B", "SWAP-1", "PAY", "Swap", "70"), new("C", "SWAP-1", "REC", "Swap", "30")], "Swap"));
        var scrambled = Describe(Run([new("B", "SWAP-1", "PAY", "Swap", "100"), new("A", "SWAP-1", "PAY", "Swap", "70"), new("C", "SWAP-1", "REC", "Swap", "30")], "Swap"));

        Assert.True(string.Equals(original, scrambled, StringComparison.Ordinal), $"original: {original} || scrambled: {scrambled}");
    }

    // The two PAY legs disagree, so the net cash is unknown and every leg reports MembersDisagree, as FromRole does.
    [Fact]
    public void Two_legs_in_one_role_that_disagree_make_the_difference_unknown_with_MembersDisagree()
    {
        var result = Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("B", "SWAP-1", "PAY", "Swap", "70"), new("C", "SWAP-1", "REC", "Swap", "30")], "Swap");

        Assert.Equal("Matched/Break left null [NetCash:Break:0] codes [MembersDisagree]", Describe(result));
    }

    // Two PAY legs that agree by value (100 and 100.00) still net against the REC leg.
    [Fact]
    public void Two_legs_in_one_role_that_agree_give_the_difference()
    {
        var result = Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("B", "SWAP-1", "PAY", "Swap", "100.00"), new("C", "SWAP-1", "REC", "Swap", "30")], "Swap");

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(70m, result.Projection(entry.Id, Side.Left)["NetCash"].Number);
        Assert.Empty(result.Rows.SelectMany(row => row.Diagnostics));
    }

    private static string Describe(IReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Outcome} left {NetCash(result.Projection(entry.Id, Side.Left)["NetCash"])} "
            + $"[{string.Join(", ", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"))}] "
            + $"codes [{string.Join(",", result.Rows.SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal))}]"));

    private static string NetCash(CanonicalValue value) =>
        value.IsNull ? "null" : value.Number.ToString(CultureInfo.InvariantCulture);

    private static IReconciliationResult Run(Row[] left, string streetAsset) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("R1", "", "", streetAsset, "70")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Asset, string Cash);
}

// #217, case 2. Spec v3 §10.1: "No decision reads RowKey", and after an ID scramble "the results must be identical"
// (AC-7). §5.2: "No value ever comes from 'the first row'", and "the profile says which leg counts, and row order never
// does."
public class PackageRawColumnRowKeyTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "package-raw-column-row-keys",
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

    // The pay leg says Swap and the receive leg says Bond in the raw Asset column, which the NetCash condition reads.
    // The package nets to 100 - 20 = 80 against the street's 70. Swapping the two legs' row keys is an ID scramble.
    [Fact]
    public void A_condition_on_a_raw_column_of_a_package_does_not_depend_on_row_keys()
    {
        var original = Describe(Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Bond", "20")]));
        var scrambled = Describe(Run([new("C", "SWAP-1", "PAY", "Swap", "100"), new("A", "SWAP-1", "REC", "Bond", "20")]));

        Assert.True(string.Equals(original, scrambled, StringComparison.Ordinal), $"original: {original} || scrambled: {scrambled}");
    }

    // The legs disagree on Asset, so the package does not carry it, and neither side holds Swap: NetCash is not compared.
    [Fact]
    public void A_raw_column_the_legs_disagree_on_is_unknown_on_the_package()
    {
        var result = Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Bond", "20")]);

        Assert.Equal("Matched/Exact []", Describe(result));
        Assert.All(result.Rows.Where(row => row.Row.Source == "left"), row => Assert.Null(result.Raw(row.Row, "Asset")));
    }

    // When the legs agree on Asset, the package carries it, and the condition holds.
    [Fact]
    public void A_raw_column_the_legs_agree_on_is_carried_by_the_package()
    {
        var result = Run([new("A", "SWAP-1", "PAY", "Swap", "100"), new("C", "SWAP-1", "REC", "Swap", "20")]);

        Assert.Equal("Matched/Break [NetCash:Break:-10]", Describe(result));
        Assert.All(result.Rows.Where(row => row.Row.Source == "left"), row => Assert.Equal("Swap", result.Raw(row.Row, "Asset")));
    }

    private static string Describe(IReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Outcome} [{string.Join(", ", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"))}]"));

    private static IReconciliationResult Run(Row[] left) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("R1", "", "", "Bond", "70")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Asset, string Cash);
}

// #217, case 3. Spec v3 §3.2: identical copies of a row are merged with the warning DuplicateRowCollapsed. §3.4: each
// diagnostic is written once, by the stage that owns it. §10.1: the same rows give the same "dispositions and diagnostic
// codes", and after an ID scramble "the results must be identical" (AC-7).
public class PackageLegCopyRowKeyTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "package-leg-copy-row-keys",
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

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Link", row => row.Link).Text("Role", row => row.Role).Text("Uti", row => "UTI-1").Text("Cash", row => row.Cash);

    // The REC leg arrives twice, as two identical copies. Only the two legs' row keys differ between the runs.
    [Fact]
    public void The_warning_for_a_copied_leg_does_not_depend_on_row_keys()
    {
        var copiedLegSortsLast = Describe(Run(pay: "A", receive: "B"));
        var copiedLegSortsFirst = Describe(Run(pay: "B", receive: "A"));

        Assert.True(
            string.Equals(copiedLegSortsLast, copiedLegSortsFirst, StringComparison.Ordinal),
            $"copied leg's key sorts last: {copiedLegSortsLast} || copied leg's key sorts first: {copiedLegSortsFirst}");
    }

    // The warning stays on the copied leg. The pay leg has no copy and reports nothing.
    [Theory]
    [InlineData("A", "B")]
    [InlineData("B", "A")]
    public void Only_the_copied_leg_reports_DuplicateRowCollapsed(string pay, string receive)
    {
        Assert.Equal(
            "PAY Matched Valid []; REC Matched ValidWithWarnings [DuplicateRowCollapsed]; REC Matched ValidWithWarnings [DuplicateRowCollapsed]",
            Describe(Run(pay, receive)));
    }

    // One line per left row, named by its role, so the two runs compare leg for leg.
    private static string Describe((IReconciliationResult Result, Row[] Left) run) =>
        string.Join("; ", run.Result.Rows
            .Where(row => row.Row.Source == "left")
            .Select(row => $"{run.Left.First(leg => leg.Id == row.Row.RowKey).Role} {row.Disposition} {row.Validity} [{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal))}]")
            .Order(StringComparer.Ordinal));

    private static (IReconciliationResult Result, Row[] Left) Run(string pay, string receive)
    {
        Row[] left = [new(pay, "SWAP-1", "PAY", "100"), new(receive, "SWAP-1", "REC", "30"), new(receive, "SWAP-1", "REC", "30")];
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("R1", "", "", "70")], Map),
            ReferenceDataSet.Empty);
        return (result, left);
    }

    public sealed record Row(string Id, string Link, string Role, string Cash);
}
