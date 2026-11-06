using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonLifecycleCollapseTests
{
    [Fact]
    public void Lifecycle_collapse_does_not_throw_when_conflicting_duplicate_rows_are_lifecycle_events()
    {
        var map = new SourceMap<(string Id, string Action, string? Target, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target)
            .Text("Quantity", r => r.Qty);

        var rightRows = new[]
        {
            ("E1", "NEW", (string?)null, "100"),
            ("E2", "CANC", "E1", "100"),
            ("E2", "CANC", "E1", "200"),
        };

        var rawRight = RecordSources.FromObjects("right", rightRows, map).Materialize("right", "Id", CancellationToken.None);
        var duplicateWithAction = rawRight.Select((r, idx) =>
        {
            var ctx = RowContext.Create(r.Ref.RowKey, Side.Right, r.Cells);
            var (p1, s1) = ctx.Pool.InternSymbol("ACME");
            var (p2, s2) = p1.InternSymbol(idx == 0 ? "NEW" : "CANC");
            ctx = ctx.WithPool(p2)
                .Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, s1), "test", null, "ACME")
                .Write("EventAction", CanonicalCell.FromInterned(FieldType.Symbol, s2), "test", null, idx == 0 ? "NEW" : "CANC");
            if (idx > 0)
                ctx = ctx.Write("EventTarget", CanonicalCell.FromText("E1"), "test", null, "E1");
            return new IntakeRow([r.Ref], ctx, idx > 0);
        }).ToArray();

        var plan = new LifecyclePlan("EventAction", "EventTarget", "Counterparty", "CANC");
        var collapsed = Consolidation.Run(duplicateWithAction, plan, null);

        Assert.All(collapsed, c => Assert.True(c.Quarantined));
    }
}
