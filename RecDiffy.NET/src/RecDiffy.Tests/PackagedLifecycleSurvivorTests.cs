using RecDiffy.Results;

namespace RecDiffy.Tests;

public class PackagedLifecycleSurvivorTests
{
    // On the left, L3 corrects the PAY leg L1, and the survivor L3 joins the REC leg L2 in package P. The right holds
    // the package as one row.
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "packaged-lifecycle-survivor",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Link", "type": "Symbol", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "Counterparty", "type": "Symbol", "role": "Identity" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Link", "to": "Link" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" },
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Link", "to": "Link" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            }
          },
          "consolidation": {
            "left": {
              "lifecycle": {},
              "packages": [ { "link": "Link", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "Amount": "Sum" } } ]
            }
          },
          "paths": [ { "id": "packages", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Link"] } ] } ]
        }
        """;

    private static readonly SourceMap<Leg> Map = new SourceMap<Leg>()
        .Text("Id", leg => leg.Id).Text("Link", leg => leg.Link).Text("LegRole", leg => leg.Role).Text("Action", leg => leg.Action)
        .Text("Target", leg => leg.Target).Text("Counterparty", leg => leg.Counterparty).Text("Amount", leg => leg.Amount);

    [Fact]
    public void The_correction_and_the_other_leg_are_matched_as_one_package()
    {
        var (result, entry) = Run();

        Assert.Equal(["L2", "L3"], entry.Left.Select(member => member.RowKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded), Superseded(result));
    }

    [Fact(Skip = "🔴 #290: Packages.Build makes the package row without the survivor's Lineage and LifecycleCollapsed.")]
    public void The_superseded_leg_links_to_the_package_entry_its_correction_joined()
    {
        var (result, entry) = Run();

        Assert.Equal(entry.Id, Assert.Single(result.Rows, row => row.Row.RowKey == "L1").EntryId);
    }

    [Fact(Skip = "🔴 #290: Packages.Build makes the package row without the survivor's Lineage and LifecycleCollapsed.")]
    public void The_package_entry_is_marked_as_a_collapsed_lifecycle()
    {
        var (_, entry) = Run();

        Assert.Equal(Composition.OneToOne | Composition.Package | Composition.LifecycleCollapsed, entry.Composition);
    }

    private static (ReconciliationResult Result, ResultEntry Entry) Run()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects(
                "left",
                [
                    new Leg("L1", "P", "PAY", "NEW", null, "C1", "10.00"),
                    new Leg("L2", "P", "REC", "NEW", null, "C1", "5.00"),
                    new Leg("L3", "P", "PAY", "CORR", "L1", "C1", "12.00"),
                ],
                Map),
            RecordSources.FromObjects("right", [new Leg("R1", "P", null, null, null, null, "17.00")], Map),
            ReferenceDataSet.Empty);
        return (result, Assert.Single(result.Entries, entry => entry.Alignment is Alignment.Matched));
    }

    private static (Disposition, SuppressionReason?) Superseded(ReconciliationResult result)
    {
        var row = Assert.Single(result.Rows, candidate => candidate.Row.RowKey == "L1");
        return (row.Disposition, row.Suppression);
    }

    private sealed record Leg(string Id, string Link, string? Role, string? Action, string? Target, string? Counterparty, string Amount);
}
