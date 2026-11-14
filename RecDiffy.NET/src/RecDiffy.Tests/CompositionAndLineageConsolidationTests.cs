using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class CompositionAndLineageConsolidationTests
{
    private static readonly FieldDeclaration[] AttributionFields =
    [
        new("Side", FieldType.Symbol, FieldRole.Key, 0),
        new("AssetClass", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("Price", FieldType.Decimal, FieldRole.Economic, 6),
        new("Accrued", FieldType.Decimal, FieldRole.Economic, 2),
        new("Fees", FieldType.Decimal, FieldRole.Economic, 2),
        new("NetCash", FieldType.Decimal, FieldRole.Economic, 2),
    ];

    [Fact]
    public void Lone_NEW_event_on_either_side_is_OneToOne_without_LifecycleCollapsed()
    {
        var result = RunLifecycle(
            [new("L1", "T1", null, null)],
            [new("R1", "T1", "NEW", null)]);

        var entry = Assert.Single(result.Entries);

        Assert.Equal((Alignment.Matched, Composition.OneToOne), (entry.Alignment, entry.Composition));
    }

    [Fact]
    public void Collapsed_correction_chain_marks_entry_LifecycleCollapsed_and_links_superseded_row()
    {
        var result = RunLifecycle(
            [new("L1", "T1", null, null)],
            [new("R1", "T1", "NEW", null), new("R2", "T1", "CORR", "R1")]);

        var entry = Assert.Single(result.Entries, candidate => candidate.Alignment is Alignment.Matched);
        var superseded = Assert.Single(result.Rows, row => row.Row.RowKey == "R1");

        Assert.Equal(Composition.OneToOne | Composition.LifecycleCollapsed, entry.Composition);
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded, (GroupId?)entry.Id), (superseded.Disposition, superseded.Suppression, superseded.EntryId));
    }

    [Fact]
    public void Multi_leg_package_on_left_against_one_row_on_right_is_ManyToOne_with_Package()
    {
        var result = RunPackage(
            [new("L1", "P1", "PAY", null, null, "10.00"), new("L2", "P1", "REC", null, null, "5.00")],
            [new("R1", "P1", null, null, null, "15.00")],
            LeftPackageProfile);

        var entry = Assert.Single(result.Entries);

        Assert.Equal((Alignment.Matched, Composition.ManyToOne | Composition.Package), (entry.Alignment, entry.Composition));
    }

    [Fact]
    public void One_row_on_left_against_multi_leg_package_on_right_is_OneToMany_with_Package()
    {
        var result = RunPackage(
            [new("L1", "P1", null, null, null, "15.00")],
            [new("R1", "P1", "PAY", null, null, "10.00"), new("R2", "P1", "REC", null, null, "5.00")],
            RightPackageProfile);

        var entry = Assert.Single(result.Entries);

        Assert.Equal((Alignment.Matched, Composition.OneToMany | Composition.Package), (entry.Alignment, entry.Composition));
    }

    [Fact]
    public void Package_of_lone_NEW_legs_is_ManyToOne_and_Package_without_LifecycleCollapsed()
    {
        var result = RunPackage(
            [new("L1", "P1", "PAY", "NEW", null, "10.00"), new("L2", "P1", "REC", "NEW", null, "5.00")],
            [new("R1", "P1", null, null, null, "15.00")],
            LeftLifecycleAndPackageProfile);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(Composition.ManyToOne | Composition.Package, entry.Composition);
    }

    [Fact]
    public void Lifecycle_survivor_inside_package_preserves_Lineage_and_LifecycleCollapsed()
    {
        var result = RunPackage(
            [
                new("L1", "P1", "PAY", "NEW", null, "10.00"),
                new("L2", "P1", "PAY", "CANC", "L1", "10.00"),
                new("L3", "P1", "PAY", "CORR", "L2", "12.00"),
                new("L4", "P1", "REC", "NEW", null, "5.00"),
            ],
            [new("R1", "P1", null, null, null, "17.00")],
            LeftLifecycleAndPackageProfile);

        var entry = Assert.Single(result.Entries, candidate => candidate.Alignment is Alignment.Matched);
        var superseded = Assert.Single(result.Rows, row => row.Row.RowKey == "L1");
        var cancelled = Assert.Single(result.Rows, row => row.Row.RowKey == "L2");

        Assert.Equal(Composition.ManyToOne | Composition.Package | Composition.LifecycleCollapsed, entry.Composition);
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded, (GroupId?)entry.Id), (superseded.Disposition, superseded.Suppression, superseded.EntryId));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.CancelEvent, (GroupId?)entry.Id), (cancelled.Disposition, cancelled.Suppression, cancelled.EntryId));
    }

    [Fact]
    public void Explained_attributed_fields_list_roots_while_unexplained_fields_clear_AttributedTo()
    {
        var explained = FieldDiffer.Compare(
            Trade(1_000_000m, 4_784.72m, 1_004_834.72m),
            Trade(800_000m, 3_827.78m, 803_877.78m),
            AttributionPlan());
        var unexplained = FieldDiffer.Compare(
            Trade(1_000_000m, 4_784.72m, 1_004_834.72m),
            Trade(800_000m, 4_000.00m, 900_000.00m),
            AttributionPlan());

        Assert.Equal(["Quantity"], Assert.Single(explained, diff => diff.Field.Name == "Accrued").AttributedTo, StringComparer.Ordinal);
        Assert.Equal(["Quantity", "Accrued"], Assert.Single(explained, diff => diff.Field.Name == "NetCash").AttributedTo, StringComparer.Ordinal);
        Assert.Empty(Assert.Single(unexplained, diff => diff.Field.Name == "Accrued").AttributedTo);
        Assert.Empty(Assert.Single(unexplained, diff => diff.Field.Name == "NetCash").AttributedTo);
    }

    private sealed record EventRow(string Id, string Trade, string? Action, string? Target);

    private sealed record PackageRow(string Id, string Link, string? Role, string? Action, string? Target, string Amount);

    private static IReconciliationResult RunLifecycle(IReadOnlyList<EventRow> left, IReadOnlyList<EventRow> right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(LifecycleProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, EventMap()),
            RecordSources.FromObjects("right", right, EventMap()),
            ReferenceDataSet.Empty);

    private static IReconciliationResult RunPackage(IReadOnlyList<PackageRow> left, IReadOnlyList<PackageRow> right, string profile) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, PackageMap()),
            RecordSources.FromObjects("right", right, PackageMap()),
            ReferenceDataSet.Empty);

    private static SourceMap<EventRow> EventMap() =>
        new SourceMap<EventRow>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("Trade", row => row.Trade)
            .Text("Quantity", _ => "100")
            .Text("Action", row => row.Action)
            .Text("Target", row => row.Target);

    private static SourceMap<PackageRow> PackageMap() =>
        new SourceMap<PackageRow>()
            .Text("Id", row => row.Id)
            .Text("Link", row => row.Link)
            .Text("LegRole", row => row.Role)
            .Text("Action", row => row.Action)
            .Text("Target", row => row.Target)
            .Text("Counterparty", _ => "C1")
            .Text("Amount", row => row.Amount);

    private static DiffPlan AttributionPlan() => DiffPlan.From(
        new PathDeclaration("trade", new RouteDeclaration("route.all", null, null), [], new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
        {
            ["Accrued"] = new(new ToleranceDeclaration("Exact", null, null, null, null), "trading.proRataToQuantity", null),
            ["NetCash"] = new(new ToleranceDeclaration("Attributed", null, null, "trading.netCash", null), null, null),
        })),
        AttributionFields,
        MidpointMode.ToEven,
        TradingRulePack.AddTo(RuleRegistry.CreateCore()).Attributions);

    private static RowContext Trade(decimal quantity, decimal accrued, decimal cash)
    {
        var row = RowContext.Create("T", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "Side", "BUY");
        row = Symbol(row, "AssetClass", "Bond");
        row = row.Write("Quantity", CanonicalCell.FromDecimal(quantity), "test", null, null);
        row = row.Write("Price", CanonicalCell.FromDecimal(100m), "test", null, null);
        row = row.Write("Accrued", CanonicalCell.FromDecimal(accrued), "test", null, null);
        row = row.Write("Fees", CanonicalCell.FromDecimal(50m), "test", null, null);
        return row.Write("NetCash", CanonicalCell.FromDecimal(cash), "test", null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, interned) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, interned), "test", null, value);
    }

    private const string LifecycleProfile = """
        {
          "formatVersion": 1,
          "profileId": "r4-lifecycle-composition",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Trade", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Trade", "to": "Trade" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Trade", "to": "Trade" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "NEW" } } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Counterparty", "Trade"] }
          ] } ]
        }
        """;

    private const string LeftPackageProfile = """
        {
          "formatVersion": 1,
          "profileId": "r4-left-package",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Link", "type": "Symbol", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Link", "to": "Link" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
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
              "packages": [ { "link": "Link", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "Amount": "Sum" } } ]
            }
          },
          "paths": [ { "id": "packages", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Link"] } ] } ]
        }
        """;

    private const string RightPackageProfile = """
        {
          "formatVersion": 1,
          "profileId": "r4-right-package",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Link", "type": "Symbol", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Link", "to": "Link" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Link", "to": "Link" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            }
          },
          "consolidation": {
            "right": {
              "packages": [ { "link": "Link", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "Amount": "Sum" } } ]
            }
          },
          "paths": [ { "id": "packages", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Link"] } ] } ]
        }
        """;

    private const string LeftLifecycleAndPackageProfile = """
        {
          "formatVersion": 1,
          "profileId": "r4-left-lifecycle-and-package",
          "version": "1.0.0",
          "culture": "en-US",
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
              "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" },
              "packages": [ { "link": "Link", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "Amount": "Sum" } } ]
            }
          },
          "paths": [ { "id": "packages", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Link"] } ] } ]
        }
        """;
}
