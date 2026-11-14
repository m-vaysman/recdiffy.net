using System.Diagnostics;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #222. Spec v3 §3.1: only an unreadable source or a missing mapped column fails the run, so a long chain of corrections
// returns a result. §5.1 rule 3: each CORR supersedes its predecessor, so the chain leaves one survivor, its last
// correction. §12: runs go to 100k rows per side and beyond, so marking rows as events must not make lifecycle grow with
// the square of the event count.
public class LifecycleScaleTests
{
    private const int ChainLength = 10_000;
    private const int StandaloneEvents = 100_000;
    private static readonly TimeSpan GrowthLimit = TimeSpan.FromSeconds(30);
    private static readonly LifecyclePlan Life = new("EventAction", "EventTarget", "Counterparty", "CANC");

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "lifecycle-scale",
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

    // E0 is the NEW trade, and each of E1 to E10000 corrects the one before it. Our side holds the same trade.
    [Fact]
    public void A_chain_of_ten_thousand_corrections_returns_with_its_last_correction_matched()
    {
        Row[] chain =
        [
            new("E0", "T1", "NEW", "", "100"),
            .. Enumerable.Range(1, ChainLength).Select(index => new Row(Id(index), "T1", "CORR", Id(index - 1), "100")),
        ];

        var result = Run([new Row("L1", "T1", "", "", "100")], chain);

        Assert.Equal(Disposition.Matched, Assert.Single(result.Rows, row => row.Row.RowKey == "L1").Disposition);
        Assert.Equal(Disposition.Matched, Assert.Single(result.Rows, row => row.Row.RowKey == Id(ChainLength)).Disposition);
        Assert.Equal(ChainLength, result.Rows.Count(row => row is { Disposition: Disposition.Suppressed, Suppression: SuppressionReason.Superseded }));
    }

    // Corrections whose targets close a loop: on itself, between two events, and a loop with a tail leading into it.
    // Every row of the chain is quarantined as a cycle (spec v3 §5.1 rule 1).
    [Theory]
    [InlineData("E1>E1")]
    [InlineData("E1>E2 E2>E1")]
    [InlineData("E1>E2 E2>E3 E3>E2")]
    public void A_chain_whose_links_close_a_loop_is_quarantined_as_a_cycle(string links)
    {
        Row[] right = [.. links.Split(' ').Select(link => link.Split('>')).Select(pair => new Row(pair[0], "T1", "CORR", pair[1], "100"))];

        var result = Run([], right);

        Assert.Equal(right.Length, result.Rows.Length);
        Assert.All(result.Rows, row =>
        {
            Assert.Equal(Disposition.Quarantined, row.Disposition);
            Assert.Equal(["LifecycleCycle"], row.Diagnostics.Select(diagnostic => diagnostic.Code), StringComparer.Ordinal);
        });
    }

    // A trade that one event corrects and another cancels has links that branch but never loop, so it is not a cycle.
    [Fact]
    public void A_chain_whose_links_branch_without_a_loop_is_not_a_cycle()
    {
        var result = Run(
            [new Row("L1", "T1", "", "", "100")],
            [new Row("E0", "T1", "NEW", "", "100"), new Row("E1", "T1", "CORR", "E0", "100"), new Row("E2", "T1", "CANC", "E0", "100")]);

        Assert.DoesNotContain(result.Rows, row => row.Disposition == Disposition.Quarantined);
        Assert.Equal(Disposition.Matched, Assert.Single(result.Rows, row => row.Row.RowKey == "E1").Disposition);
    }

    // Each of 100,000 NEW events with no target is its own chain of one, so there is nothing to consolidate. Linear work
    // takes well under a second here; the square of the event count takes minutes. The limit sits far from both.
    [Fact]
    public void Consolidating_a_hundred_thousand_standalone_events_stays_far_inside_the_limit()
    {
        IntakeRow[] events = [.. Enumerable.Range(0, StandaloneEvents).Select(Event)];

        var watch = Stopwatch.StartNew();
        var consolidated = Consolidation.Run(events, Life, null);
        var elapsed = watch.Elapsed;

        Assert.Equal(StandaloneEvents, consolidated.Count(row => row is { Quarantined: false, Suppression: null, LifecycleCollapsed: true }));
        Assert.True(
            elapsed < GrowthLimit,
            string.Create(CultureInfo.InvariantCulture, $"{StandaloneEvents} events took {elapsed.TotalSeconds:F1} s; the limit is {GrowthLimit.TotalSeconds} s."));
    }

    private static IReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static string Id(int index) => "E" + index.ToString(CultureInfo.InvariantCulture);

    private static IntakeRow Event(int index)
    {
        var row = RowContext.Create(Id(index), Side.Right, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (actionPool, action) = row.Pool.InternSymbol("NEW");
        var (scopePool, scope) = actionPool.InternSymbol("ACME");
        var context = row.WithPool(scopePool)
            .Write("EventAction", CanonicalCell.FromInterned(FieldType.Symbol, action), "test", null, "NEW")
            .Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, scope), "test", null, "ACME");
        return new IntakeRow([new SourceRef("right", Id(index), index)], context, false);
    }

    public sealed record Row(string Id, string Ref, string Action, string Target, string Qty);
}
