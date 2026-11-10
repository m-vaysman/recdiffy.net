using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class DiffTests
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
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
            _ = FieldDiffer.Compare(left, right, plan);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Keeps_an_accrued_delta_inside_an_inclusive_absolute_tolerance()
    {
        var diffs = FieldDiffer.Compare(
            Trade("L", Side.Left, 1m, 1m, 123_252.60m, 0m, 1m, "BUY", "Bond"),
            Trade("R", Side.Right, 1m, 1m, 123_251.58m, 0m, 1m, "BUY", "Bond"),
            Plan());

        var accrued = Assert.Single(diffs, diff => diff.Field.Name == "Accrued");
        Assert.Equal(FieldOutcome.WithinTolerance, accrued.Outcome);
        Assert.Equal(-1.02m, accrued.Delta.Number);
    }

    [Fact]
    public void Attributes_a_partial_fill_to_quantity()
    {
        var diffs = FieldDiffer.Compare(
            Trade("L", Side.Left, 100m, 2m, 50m, 10m, 240m, "SELL", "Equity"),
            Trade("R", Side.Right, 80m, 2m, 40m, 8m, 192m, "SELL", "Equity"),
            Plan());

        Assert.Equal(FieldOutcome.Break, diffs.Single(diff => diff.Field.Name == "Quantity").Outcome);
        Assert.Equal("Quantity", Assert.Single(diffs.Single(diff => diff.Field.Name == "Fees").AttributedTo));
        Assert.Contains("Quantity", diffs.Single(diff => diff.Field.Name == "NetCash").AttributedTo, StringComparer.Ordinal);
    }

    [Fact]
    public void Attributes_an_fx_cash_delta_to_the_rate()
    {
        var diffs = FieldDiffer.Compare(
            Trade("L", Side.Left, 1_000_000m, 1.2705m, 0m, 0m, 1_270_500m, "BUY", "Forward"),
            Trade("R", Side.Right, 1_000_000m, 1.2706m, 0m, 0m, 1_270_600m, "BUY", "Forward"),
            Plan());

        Assert.Equal(FieldOutcome.WithinTolerance, diffs.Single(diff => diff.Field.Name == "Price").Outcome);
        Assert.Equal(0.0001m, diffs.Single(diff => diff.Field.Name == "Price").Delta.Number);
        var cash = diffs.Single(diff => diff.Field.Name == "NetCash");
        Assert.Equal(FieldOutcome.WithinTolerance, cash.Outcome);
        Assert.Equal(100m, cash.Delta.Number);
        Assert.Equal("Price", Assert.Single(cash.AttributedTo));
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

public class RunTests
{
    [Fact]
    public void Matches_an_exact_pair_and_partitions_the_rows()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(profile, Source("L1"), Source("R1"), ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(Composition.OneToOne, entry.Composition);
        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.Equal(entry.Id, result.Rows[0].EntryId);
    }

    [Fact]
    public void Keeps_the_group_id_when_the_rows_are_swapped()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var forward = Reconciler.Run(profile, Source("L1", "R1"), Source("R1", "L1"), ReferenceDataSet.Empty);
        var backward = Reconciler.Run(profile, Source("R1", "L1"), Source("L1", "R1"), ReferenceDataSet.Empty);

        Assert.Equal(forward.Entries[0].Id, backward.Entries[0].Id);
    }

    private static RecordSource Source(params string[] ids) =>
        RecordSources.FromObjects("book", ids.Select(id => Row(id)), Map());

    private static Dictionary<string, string> Row(string id) => new(StringComparer.Ordinal)
    {
        ["Id"] = id,
        ["Counterparty"] = "CITI",
        ["Instrument"] = "US1",
        ["TradeDate"] = "2025-06-12",
        ["Side"] = "BUY",
        ["Quantity"] = "100.00",
        ["Price"] = "2",
        ["NetCash"] = "200.00",
    };

    private static SourceMap<Dictionary<string, string>> Map() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Counterparty", row => row["Counterparty"])
            .Text("Instrument", row => row["Instrument"])
            .Text("TradeDate", row => row["TradeDate"])
            .Text("Side", row => row["Side"])
            .Text("Quantity", row => row["Quantity"])
            .Text("Price", row => row["Price"])
            .Text("NetCash", row => row["NetCash"]);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "sample",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [
              { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
              { "rule": "symbol.map", "from": "Side", "to": "Side" }
            ], "parse": [
              { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
              { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
              { "rule": "decimal.parse", "from": "Price", "to": "Price" },
              { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
            ] },
            "right": { "identify": [
              { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
              { "rule": "symbol.map", "from": "Side", "to": "Side" }
            ], "parse": [
              { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
              { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
              { "rule": "decimal.parse", "from": "Price", "to": "Price" },
              { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
            ] }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] }
          ] } ]
        }
        """;
}
