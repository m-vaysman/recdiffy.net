using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class LifecycleTests
{
    private static readonly LifecyclePlan Life = new("EventAction", "EventTarget", "Counterparty", "CANC");

    private static readonly PackagePlan Legs = new(
        "PackageRef",
        "LegRole",
        ["LEG_1_PAY", "LEG_2_RECEIVE"],
        [
            new FieldAggregate("Quantity", AggregateKind.Same, null, null),
            new FieldAggregate("Price", AggregateKind.FromRole, "LEG_1_PAY", null),
            new FieldAggregate("Side", AggregateKind.FromRole, "LEG_1_PAY", null),
            new FieldAggregate("NetCash", AggregateKind.DifferenceByRole, "LEG_1_PAY", "LEG_2_RECEIVE"),
        ]);

    [Fact]
    public void Collapses_a_cancel_correct_chain_to_the_correction()
    {
        var rows = new[]
        {
            Event("CS-36468090", "CORR", "CS-74239776", 105.149m),
            Event("CS-39633954", "NEW", null, 102.604m),
            Event("CS-74239776", "CANC", "CS-39633954", 102.604m),
        };
        var result = Consolidation.Run(rows, Life, null).ToDictionary(row => row.Context.RowKey);

        Assert.Null(result["CS-36468090"].Suppression);
        Assert.False(result["CS-36468090"].Quarantined);
        Assert.Equal(SuppressionReason.Superseded, result["CS-39633954"].Suppression);
        Assert.Equal(SuppressionReason.CancelEvent, result["CS-74239776"].Suppression);
    }

    [Fact]
    public void Links_a_chain_when_the_scope_and_id_contain_the_separator()
    {
        const string separator = "\u001f";
        var rows = new[]
        {
            Event("CORR" + separator + "1", "CORR", "CANC" + separator + "1", 2m, "ACME" + separator + "X"),
            Event("NEW" + separator + "1", "NEW", null, 1m, "ACME" + separator + "X"),
            Event("CANC" + separator + "1", "CANC", "NEW" + separator + "1", 1m, "ACME" + separator + "X"),
        };
        var result = Consolidation.Run(rows, Life, null).ToDictionary(row => row.Context.RowKey);

        Assert.Null(result["CORR" + separator + "1"].Suppression);
        Assert.Equal(SuppressionReason.Superseded, result["NEW" + separator + "1"].Suppression);
        Assert.Equal(SuppressionReason.CancelEvent, result["CANC" + separator + "1"].Suppression);
    }

    [Fact]
    public void Suppresses_a_fully_cancelled_chain()
    {
        var rows = new[]
        {
            Event("NEW-1", "NEW", null, 10m),
            Event("CANC-1", "CANC", "NEW-1", 10m),
        };
        var result = Consolidation.Run(rows, Life, null);

        Assert.All(result, row => Assert.Equal(SuppressionReason.FullyCancelled, row.Suppression));
    }

    [Fact]
    public void Quarantines_a_cancel_with_no_target()
    {
        var rows = new[] { Event("CANC-1", "CANC", "MISSING", 10m) };
        var result = Assert.Single(Consolidation.Run(rows, Life, null));

        Assert.True(result.Quarantined);
        Assert.Equal("MissingLifecycleTarget", result.Code);
    }

    [Fact]
    public void Nets_two_linked_legs_into_one_package()
    {
        var rows = new[]
        {
            Leg("OMS9369884", "PLK-158918", "LEG_1_PAY", "PAY_FIXED", 13_000_000m, 4.268m, 278_961.22m),
            Leg("OMS9929303", "PLK-158918", "LEG_2_RECEIVE", "REC_FIXED", 13_000_000m, 3.9645m, 259_124.12m),
        };
        var result = Assert.Single(Consolidation.Run(rows, null, Legs));

        Assert.Equal(2, result.Members.Count);
        Assert.Equal(13_000_000m, result.Context.Values["Quantity"].Number);
        Assert.Equal(4.268m, result.Context.Values["Price"].Number);
        Assert.Equal("PAY_FIXED", result.Context.Display("Side"));
        Assert.Equal(19_837.10m, result.Context.Values["NetCash"].Number);
    }

    [Fact]
    public void Quarantines_a_package_missing_a_role()
    {
        var rows = new[] { Leg("OMS9369884", "PLK-158918", "LEG_1_PAY", "PAY_FIXED", 13_000_000m, 4.268m, 278_961.22m) };
        var result = Assert.Single(Consolidation.Run(rows, null, Legs));

        Assert.True(result.Quarantined);
        Assert.Equal("IncompletePackage", result.Code);
    }

    private static IntakeRow Event(string id, string action, string? target, decimal price, string scope = "CITADEL") =>
        new([new SourceRef("right", id, 0)], With(id, action, target, price, scope), false);

    private static IntakeRow Leg(string id, string link, string role, string side, decimal quantity, decimal price, decimal cash) =>
        new([new SourceRef("left", id, 0)], WithLeg(id, link, role, side, quantity, price, cash), false);

    private static RowContext With(string id, string action, string? target, decimal price, string scope)
    {
        var row = RowContext.Create(id, Side.Right, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "EventAction", action);
        row = Symbol(row, "Counterparty", scope);
        row = target is null ? row : Text(row, "EventTarget", target);
        return row.Write("Price", CanonicalCell.FromDecimal(price), "test", null, price.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static RowContext WithLeg(string id, string link, string role, string side, decimal quantity, decimal price, decimal cash)
    {
        var row = RowContext.Create(id, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Text(row, "PackageRef", link);
        row = Symbol(row, "LegRole", role);
        row = Symbol(row, "Side", side);
        row = row.Write("Quantity", CanonicalCell.FromDecimal(quantity), "test", null, null);
        row = row.Write("Price", CanonicalCell.FromDecimal(price), "test", null, null);
        return row.Write("NetCash", CanonicalCell.FromDecimal(cash), "test", null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, interned) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, interned), "test", null, value);
    }

    private static RowContext Text(RowContext row, string field, string value) =>
        row.Write(field, CanonicalCell.FromText(value), "test", null, value);
}
