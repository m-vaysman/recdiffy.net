namespace RecDiffy.Tests;

public class SourceMapCapacityTests
{
    private sealed record Row(
        string Id,
        string Counterparty,
        decimal Quantity,
        decimal Price,
        DateOnly TradeDate,
        string Side,
        decimal Fees,
        DateOnly SettleDate);

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Counterparty", row => row.Counterparty)
        .Decimal("Quantity", row => row.Quantity)
        .Decimal("Price", row => row.Price)
        .Date("TradeDate", row => row.TradeDate)
        .Text("Side", row => row.Side)
        .Decimal("Fees", row => row.Fees)
        .Date("SettleDate", row => row.SettleDate);

    // The allocation test isolates the dictionary resizing this change removes, so it binds only text columns;
    // a decimal or date cell boxes its value on every projection and would mask the dictionary's own cost.
    private static readonly SourceMap<Row> TextOnly = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Counterparty", row => row.Counterparty)
        .Text("Side", row => row.Side)
        .Text("C4", row => row.Id)
        .Text("C5", row => row.Counterparty)
        .Text("C6", row => row.Side)
        .Text("C7", row => row.Id)
        .Text("C8", row => row.Counterparty);

    private static readonly Row Trade = new("T1", "JPM", 100m, 49.5m, new DateOnly(2025, 6, 11), "SELL", 1.25m, new DateOnly(2025, 6, 13));

    private IReadOnlyDictionary<string, object?>[] _projected = [];

    [Fact]
    public void Projecting_a_row_returns_the_eight_cells_each_binder_wrote()
    {
        var cells = Map.Project(Trade);

        Assert.Equal(8, cells.Count);
        Assert.Equal("T1", cells["Id"]);
        Assert.Equal("JPM", cells["Counterparty"]);
        Assert.Equal(100m, cells["Quantity"]);
        Assert.Equal(49.5m, cells["Price"]);
        Assert.Equal(new DateOnly(2025, 6, 11), cells["TradeDate"]);
        Assert.Equal("SELL", cells["Side"]);
        Assert.Equal(1.25m, cells["Fees"]);
        Assert.Equal(new DateOnly(2025, 6, 13), cells["SettleDate"]);
    }

    [Fact]
    public void A_column_bound_twice_keeps_the_last_binders_value()
    {
        var cells = Map.Text("Id", _ => "replaced").Project(Trade);

        Assert.Equal(8, cells.Count);
        Assert.Equal("replaced", cells["Id"]);
    }

    [Fact]
    public void Projecting_eight_text_cells_allocates_under_eight_hundred_bytes_per_row()
    {
        for (var index = 0; index < 200; index++)
            _ = TextOnly.Project(Trade);

        _projected = new IReadOnlyDictionary<string, object?>[1_000];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < _projected.Length; index++)
            _projected[index] = TextOnly.Project(Trade);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Before the capacity was given this was about 1,110 bytes; with it, about 560. The bound sits between the two
        // with room to spare, so a small change in the runtime's dictionary layout does not fail the test.
        var perCall = (double)allocated / _projected.Length;
        Assert.True(
            perCall < 800,
            $"Projecting eight text cells allocated {perCall.ToInvariant("F1")} bytes per row, above the 800-byte budget.");
    }
}