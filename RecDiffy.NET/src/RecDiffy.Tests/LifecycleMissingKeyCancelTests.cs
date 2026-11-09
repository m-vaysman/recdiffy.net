using RecDiffy.Results;

namespace RecDiffy.Tests;

// #215. Spec v3 §5.1 rule 5: "If any chain row is quarantined (for example by DuplicateConflict), every row of the
// chain is quarantined (ChainMemberQuarantined)." A cancel whose row key is blank is quarantined (MissingKey), and it
// still names the trade it cancels. §3.4: a row quarantined for one reason does not also report another stage's code.
public class LifecycleMissingKeyCancelTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "lifecycle-missing-key-cancel",
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

    // The street file holds a trade and its cancel. The cancel row lost its own id, and it still names E1 as its target.
    [Fact]
    public void A_trade_whose_cancel_has_no_row_key_is_not_matched_as_live()
    {
        var result = Run([new Row("L1", "T1", "", "", "100")], [new Row("E1", "T1", "NEW", "", "100"), new Row("", "T1", "CANC", "E1", "100")]);

        var described = Describe(result);
        var cancelled = Assert.Single(result.Rows, row => row.Row.RowKey == "E1");
        Assert.True(cancelled.Disposition is Disposition.Quarantined, described);
    }

    // The trade reports ChainMemberQuarantined, the keyless cancel reports MissingKey alone, and our trade stays open.
    [Fact]
    public void The_keyless_cancel_reports_only_MissingKey_and_its_target_reports_ChainMemberQuarantined()
    {
        var result = Run([new Row("L1", "T1", "", "", "100")], [new Row("E1", "T1", "NEW", "", "100"), new Row("", "T1", "CANC", "E1", "100")]);

        Assert.Equal("left:L1#0=LeftOnly[]; right:#1=Quarantined[MissingKey]; right:E1#0=Quarantined[ChainMemberQuarantined]", Describe(result));
    }

    // Two keyless rows are two rows: each is quarantined on its own, and each trade a keyless cancel names is quarantined.
    [Fact]
    public void Two_keyless_cancels_quarantine_the_trades_they_name_and_leave_the_other_trade_live()
    {
        var result = Run(
            [new Row("L1", "T1", "", "", "100"), new Row("L2", "T2", "", "", "100"), new Row("L3", "T3", "", "", "100")],
            [
                new Row("E1", "T1", "NEW", "", "100"),
                new Row("E2", "T2", "NEW", "", "100"),
                new Row("E3", "T3", "NEW", "", "100"),
                new Row("", "T1", "CANC", "E1", "100"),
                new Row("", "T2", "CANC", "E2", "100"),
            ]);

        Assert.Equal(
            "left:L1#0=LeftOnly[]; left:L2#1=LeftOnly[]; left:L3#2=Matched[]; right:#3=Quarantined[MissingKey]; right:#4=Quarantined[MissingKey]; right:E1#0=Quarantined[ChainMemberQuarantined]; right:E2#1=Quarantined[ChainMemberQuarantined]; right:E3#2=Matched[]",
            Describe(result));
    }

    // A keyless row that a rule also rejects is quarantined for its missing key alone.
    [Fact]
    public void A_keyless_row_with_an_unreadable_quantity_reports_only_MissingKey()
    {
        var result = Run([], [new Row("", "T1", "CANC", "E1", "abc")]);

        Assert.Equal("right:#0=Quarantined[MissingKey]", Describe(result));
    }

    private static string Describe(ReconciliationResult result) =>
        string.Join("; ", result.Rows
            .Select(row => $"{row.Row.Source}:{row.Row.RowKey}#{row.Row.Ordinal}={row.Disposition}[{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code))}]")
            .Order(StringComparer.Ordinal));

    private static ReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Ref, string Action, string Target, string Qty);
}
