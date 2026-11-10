using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Benchmarks;

// The schema and the four passes of CascadeTests: shared reference, natural key, group sum, reduced key.
internal static class BenchCascade
{
    private const string Rule = "bench";
    private const string Counterparty = "CP";
    private static readonly DateOnly TradeDate = new(2025, 6, 11);

    public static IReadOnlyList<FieldDeclaration> Fields { get; } =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Instrument", FieldType.Symbol, FieldRole.Key, 0),
        new("TradeDate", FieldType.Date, FieldRole.Key, 0),
        new("Side", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("Price", FieldType.Decimal, FieldRole.Economic, 6),
        new("NetCash", FieldType.Decimal, FieldRole.Economic, 2),
        new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
    ];

    // Declared before Passes, because static initializers run in text order.
    public static PassDeclaration Group { get; } =
        new("P3", "match.groupSum", [], ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], ["Quantity", "NetCash"], "left", null);

    public static IReadOnlyList<PassDeclaration> Passes { get; } =
    [
        new("P1", "match.sharedRef", [], [], [], null, null),
        new("P2", "match.naturalKey", ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"], [], [], null, null),
        Group,
        new("P4", "match.reducedKey", ["Counterparty", "Instrument", "TradeDate", "Side"], [], [], null, null),
    ];

    public static ConsolidatedRow[] Rows(IReadOnlyList<BenchTrade> trades) => [.. trades.Select(Row)];

    public static MatchReport Run(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right, IReadOnlyList<PassDeclaration> passes) =>
        CascadeMatcher.Run(left, right, passes, Fields, SearchLimits.Default);

    public static BenchOutcome Outcome(MatchReport report) => new(
        report.Committed.Count,
        report.Committed.Sum(match => match.Left.Count + match.Right.Count),
        Count(report, Alignment.Ambiguous, null),
        Count(report, Alignment.SearchIncomplete, "BlockTooLarge"),
        Count(report, Alignment.SearchIncomplete, "BudgetExhausted"),
        Count(report, Alignment.LeftOnly, null),
        Count(report, Alignment.RightOnly, null));

    // The benchmark setup calls this once, so a dataset that stops giving its outcome fails instead of timing something else.
    public static void Verify(BenchDataset dataset, MatchReport report)
    {
        var actual = Outcome(report);
        if (actual != dataset.Expected)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{dataset.Shape} at {dataset.Rows} rows and width {dataset.Width}: expected {dataset.Expected}, the matcher gave {actual}."));
        }
    }

    private static int Count(MatchReport report, Alignment alignment, string? code) =>
        report.Residue.Where(cluster => cluster.Alignment == alignment && cluster.Code == code).Sum(cluster => cluster.Rows.Count);

    private static ConsolidatedRow Row(BenchTrade trade, int ordinal)
    {
        var created = RowContext.Create(trade.Id, trade.Side, new Dictionary<string, object?>(StringComparer.Ordinal));
        var keyed = Symbol(Symbol(Symbol(created, "Counterparty", Counterparty), "Instrument", trade.Instrument), "Side", "BUY");
        var valued = keyed
            .Write("TradeDate", CanonicalCell.FromDate(TradeDate), Rule, null, TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Write("Quantity", CanonicalCell.FromDecimal(trade.Quantity), Rule, null, null)
            .Write("Price", CanonicalCell.FromDecimal(trade.Price), Rule, null, null)
            .Write("NetCash", CanonicalCell.FromDecimal(trade.NetCash), Rule, null, null);
        var referenced = trade is { Scheme: { } scheme, Reference: { } reference } ? Identifier(valued, scheme, reference) : valued;
        return new ConsolidatedRow([new SourceRef(trade.Side is Side.Left ? "left" : "right", trade.Id, ordinal)], referenced, false, null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), Rule, null, value);
    }

    private static RowContext Identifier(RowContext row, string scheme, string value)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", value);
        return row.WithPool(pool).Write("TradeRef", CanonicalCell.FromInterned(FieldType.Identifier, id), Rule, null, value);
    }
}
