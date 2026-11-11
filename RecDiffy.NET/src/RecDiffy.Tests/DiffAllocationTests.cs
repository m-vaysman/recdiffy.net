using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class DiffAllocationTests
{
    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Instrument", FieldType.Symbol, FieldRole.Key, 0),
        new("TradeDate", FieldType.Date, FieldRole.Key, 0),
        new("Side", FieldType.Symbol, FieldRole.Key, 0),
        new("AssetClass", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("Price", FieldType.Decimal, FieldRole.Economic, 6),
        new("Accrued", FieldType.Decimal, FieldRole.Economic, 2),
        new("Fees", FieldType.Decimal, FieldRole.Economic, 2),
        new("NetCash", FieldType.Decimal, FieldRole.Economic, 2),
    ];

    [Fact]
    public void Allocates_nothing_for_an_exact_pair()
    {
        var plan = Plan();
        var left = Trade("L", Side.Left, 100m, 2m, 50m, 10m, 240m, "SELL", "Equity");
        var right = Trade("R", Side.Right, 100m, 2m, 50m, 10m, 240m, "SELL", "Equity");
        _ = FieldDiffer.Compare(left, right, plan);

        // A background GC that ends its mark phase inside the window discards the unused part of this thread's
        // allocation context, and the counter reports that part as allocated (#104). After a gen0 collection the
        // thread has no allocation context, so there is nothing to discard.
        GC.Collect(0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
            _ = FieldDiffer.Compare(left, right, plan);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static DiffPlan Plan() => DiffPlan.From(
        new PathDeclaration("trade", new RouteDeclaration("route.all", null, null), [], new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
        {
            ["Accrued"] = new(new ToleranceDeclaration("Absolute", 1.25m, null, "trading.proRataToQuantity", null), "trading.proRataToQuantity", new WhenClause("AssetClass", null, null, ["Bond", "Swap"])),
            ["Fees"] = new(new ToleranceDeclaration("Absolute", 0.05m, null, "trading.proRataToQuantity", null), "trading.proRataToQuantity", null),
            ["Price"] = new(new ToleranceDeclaration("Absolute", 0.0001m, null, null, new WhenClause("AssetClass", "Forward", null, null)), null, null),
            ["NetCash"] = new(new ToleranceDeclaration("Attributed", null, null, "trading.netCash", null), "trading.netCash", null),
        })),
        Fields,
        MidpointMode.ToEven,
        RulePacks.Trading.TradingRulePack.AddTo(RuleRegistry.CreateCore()).Attributions);

    private static RowContext Trade(string id, Side side, decimal quantity, decimal price, decimal accrued, decimal fees, decimal cash, string direction, string asset)
    {
        var row = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "Counterparty", "CITI");
        row = Symbol(row, "Instrument", "US1");
        row = Symbol(row, "Side", direction);
        row = Symbol(row, "AssetClass", asset);
        row = row.Write("TradeDate", CanonicalCell.FromDate(new DateOnly(2025, 6, 12)), "test", null, null);
        row = row.Write("Quantity", CanonicalCell.FromDecimal(quantity), "test", null, null);
        row = row.Write("Price", CanonicalCell.FromDecimal(price), "test", null, null);
        row = row.Write("Accrued", CanonicalCell.FromDecimal(accrued), "test", null, null);
        row = row.Write("Fees", CanonicalCell.FromDecimal(fees), "test", null, null);
        return row.Write("NetCash", CanonicalCell.FromDecimal(cash), "test", null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, interned) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, interned), "test", null, value);
    }
}
