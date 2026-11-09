using RecDiffy.Results;

namespace RecDiffy.Tests;

// #216. Spec v3 §5.1: "Every target must exist, otherwise the chain is quarantined (MissingLifecycleTarget)"; "A CORR
// supersedes its predecessor"; "None means every row is Suppressed (FullyCancelled)". A correction that names no target
// has no predecessor to supersede, and a cancel that names no target cancels nothing.
public class LifecycleBlankTargetTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "lifecycle-blank-target",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Cpty", row => "ACME").Text("Ref", row => row.Ref)
        .Text("Action", row => row.Action).Text("Target", row => row.Target).Text("Qty", row => row.Qty);

    // The street sends only the corrected version of a trade, with no target. Our side holds the same trade.
    [Fact]
    public void A_correction_with_no_target_is_not_suppressed_as_fully_cancelled()
    {
        var result = Run([new("L1", "T1", "", "", "100")], [new("E1", "T1", "CORR", "", "100")]);

        var correction = Assert.Single(result.Rows, row => row.Row.RowKey == "E1");
        Assert.True(
            correction.Disposition is Disposition.Matched or Disposition.Quarantined,
            $"E1: {correction.Disposition} {correction.Suppression} [{string.Join(",", correction.Diagnostics.Select(diagnostic => diagnostic.Code))}]; "
            + $"L1: {result.Rows.Single(row => row.Row.RowKey == "L1").Disposition}");
    }

    // A cancel that names no target, next to the live trade it may have been meant for.
    [Fact]
    public void A_cancel_with_no_target_is_not_suppressed_as_fully_cancelled()
    {
        var result = Run([new("L1", "T1", "", "", "100")], [new("E1", "T1", "NEW", "", "100"), new("E2", "T1", "CANC", "", "100")]);

        var cancel = Assert.Single(result.Rows, row => row.Row.RowKey == "E2");
        Assert.True(
            cancel.Disposition is Disposition.Quarantined,
            $"E2: {cancel.Disposition} {cancel.Suppression} [{string.Join(",", cancel.Diagnostics.Select(diagnostic => diagnostic.Code))}]; "
            + $"E1: {result.Rows.Single(row => row.Row.RowKey == "E1").Disposition}");
    }

    // Both rows name no target, so each chain is quarantined with MissingLifecycleTarget. The trade the cancel sits next to
    // is not linked to it and still matches.
    [Theory]
    [InlineData("CORR")]
    [InlineData("CANC")]
    public void A_correction_or_cancel_with_no_target_reports_MissingLifecycleTarget(string action)
    {
        var result = Run(
            [new("L1", "T1", "", "", "100"), new("L2", "T2", "", "", "100")],
            [new("E1", "T1", "NEW", "", "100"), new("E2", "T2", action, "", "100")]);

        Assert.Equal(
            "left:L1=Matched[]; left:L2=LeftOnly[]; right:E1=Matched[]; right:E2=Quarantined[MissingLifecycleTarget]",
            Describe(result));
    }

    // A correction that names its predecessor still supersedes it, so the rule above is about a blank target only.
    [Fact]
    public void A_correction_that_names_its_predecessor_still_survives()
    {
        var result = Run([new("L1", "T1", "", "", "100")], [new("E1", "T1", "NEW", "", "90"), new("E2", "T1", "CORR", "E1", "100")]);

        Assert.Equal("left:L1=Matched[]; right:E1=Suppressed[]; right:E2=Matched[]", Describe(result));
    }

    private static string Describe(ReconciliationResult result) =>
        string.Join("; ", result.Rows
            .Select(row => $"{row.Row.Source}:{row.Row.RowKey}={row.Disposition}[{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code))}]")
            .Order(StringComparer.Ordinal));

    private static ReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Ref, string Action, string Target, string Qty);
}
