using RecDiffy.Results;

namespace RecDiffy.Tests;

// #214. Spec v3 §3.2: conflicting copies of one row key are "Every copy quarantined: DuplicateConflict", and "the result
// does not depend on which copy came first". §3.1: only an unreadable source or a missing mapped column fails the run.
// §3.4: a row quarantined for one reason does not also report another stage's code. §10.1: the same rows in any order
// give the same result.
public class LifecycleEventCopyTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "lifecycle-event-copies",
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
        .Text("Id", row => row.Id).Text("Cpty", row => row.Cpty).Text("Ref", row => row.Ref)
        .Text("Action", row => row.Action).Text("Target", row => row.Target).Text("Qty", row => row.Qty);

    // The same event id arrives twice, and the two copies name different counterparties.
    [Fact]
    public void Conflicting_copies_that_differ_in_counterparty_are_quarantined()
    {
        var result = Run([], [new("E1", "ACME", "T1", "NEW", "", "100"), new("E1", "BETA", "T1", "NEW", "", "100")]);

        Assert.All(result.Rows, row =>
        {
            Assert.Equal(Disposition.Quarantined, row.Disposition);
            Assert.Contains("DuplicateConflict", row.Diagnostics.Select(diagnostic => diagnostic.Code), StringComparer.Ordinal);
        });
    }

    // E3 arrives twice: one copy cancels E1, the other cancels E2. Which trade survives must not depend on copy order.
    [Fact]
    public void Conflicting_cancel_copies_with_different_targets_do_not_depend_on_copy_order()
    {
        Row first = new("E1", "ACME", "T1", "NEW", "", "100");
        Row second = new("E2", "ACME", "T2", "NEW", "", "100");
        Row cancelsFirst = new("E3", "ACME", "T1", "CANC", "E1", "100");
        Row cancelsSecond = new("E3", "ACME", "T2", "CANC", "E2", "100");
        Row[] left = [new("L1", "ACME", "T1", "", "", "100"), new("L2", "ACME", "T2", "", "", "100")];

        var inOrder = Describe(Run(left, [first, second, cancelsFirst, cancelsSecond]));
        var swapped = Describe(Run(left, [first, second, cancelsSecond, cancelsFirst]));

        Assert.True(string.Equals(inOrder, swapped, StringComparison.Ordinal), $"cancel of E1 first: {inOrder} || cancel of E2 first: {swapped}");
    }

    // Each trade is the target of a quarantined cancel copy, so neither is matched as live. The copies keep their own
    // code, and the trades they name report ChainMemberQuarantined.
    [Fact]
    public void Every_target_of_a_conflicting_cancel_copy_is_quarantined_and_each_row_reports_one_code()
    {
        var result = Run(
            [new("L1", "ACME", "T1", "", "", "100"), new("L2", "ACME", "T2", "", "", "100")],
            [
                new("E1", "ACME", "T1", "NEW", "", "100"),
                new("E2", "ACME", "T2", "NEW", "", "100"),
                new("E3", "ACME", "T1", "CANC", "E1", "100"),
                new("E3", "ACME", "T2", "CANC", "E2", "100"),
            ]);

        Assert.Equal(
            "left:L1=LeftOnly[]; left:L2=LeftOnly[]; right:E1=Quarantined[ChainMemberQuarantined]; right:E2=Quarantined[ChainMemberQuarantined]; right:E3=Quarantined[DuplicateConflict]; right:E3=Quarantined[DuplicateConflict]",
            Describe(result));
    }

    private static string Describe(ReconciliationResult result) =>
        string.Join("; ", result.Rows
            .Select(row => $"{row.Row.Source}:{row.Row.RowKey}={row.Disposition}[{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal))}]")
            .Order(StringComparer.Ordinal));

    private static ReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Cpty, string Ref, string Action, string Target, string Qty);
}
