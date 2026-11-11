using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class UnexplainedAttributionTests
{
    private static readonly FieldDeclaration[] Fields =
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
    public void A_pro_rata_field_its_formula_does_not_explain_is_attributed_to_no_root()
    {
        var accrued = Diff("Accrued", Trade(1_000_000m, 4_784.72m, 1_004_834.72m), Trade(800_000m, 4_000.00m, 804_050.00m));

        Assert.Equal((FieldOutcome.Break, 0), (accrued.Outcome, accrued.AttributedTo.Length));
    }

    [Fact]
    public void A_net_cash_its_formula_does_not_explain_is_attributed_to_no_root()
    {
        var cash = Diff("NetCash", Trade(1_000_000m, 4_784.72m, 1_004_834.72m), Trade(800_000m, 3_827.78m, 900_000.00m));

        Assert.Equal((FieldOutcome.Break, 0), (cash.Outcome, cash.AttributedTo.Length));
    }

    private static FieldDiff Diff(string field, RowContext left, RowContext right) =>
        Assert.Single(FieldDiffer.Compare(left, right, Plan()), diff => diff.Field.Name == field);

    private static DiffPlan Plan() => DiffPlan.From(
        new PathDeclaration("trade", new RouteDeclaration("route.all", null, null), [], new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
        {
            ["Accrued"] = new(new ToleranceDeclaration("Exact", null, null, null, null), "trading.proRataToQuantity", null),
            ["NetCash"] = new(new ToleranceDeclaration("Attributed", null, null, "trading.netCash", null), null, null),
        })),
        Fields,
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
}
