using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #395: intake keeps the one dictionary a source built for a row; RowContext.Create still copies a caller's.
public class OwnedRawCellsTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "owned-raw-cells",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Id"] } ] } ]
        }
        """;

    private sealed record Trade(string Id, string? Party, decimal? Quantity, DateOnly? Day);

    private static readonly SourceMap<Trade> Map = new SourceMap<Trade>()
        .Text("Id", row => row.Id)
        .Text("Party", row => row.Party)
        .Decimal("Qty", row => row.Quantity)
        .Date("Day", row => row.Day);

    private static readonly Trade[] Trades =
    [
        new("T1", "JPM", 1234.5m, new DateOnly(2025, 6, 11)),
        new("T2", null, null, null),
    ];

    private IntakeRow[] _kept = [];

    [Fact]
    public void A_callers_dictionary_changed_after_Create_does_not_change_the_row()
    {
        var cells = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Id"] = "T1", ["Qty"] = 100m };

        var row = RowContext.Create("T1", Side.Left, cells);
        cells["Qty"] = 999m;
        cells["Added"] = "later";
        cells.Remove("Id");

        Assert.Equal(["Id", "Qty"], row.Raw.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("T1", row.Raw["Id"]);
        Assert.Equal(100m, row.Raw["Qty"]);
    }

    [Fact]
    public void A_row_started_by_intake_holds_every_cell_of_its_record()
    {
        var records = RecordSources.FromObjects("left", Trades, Map).Materialize("left", "Id", CancellationToken.None);

        var rows = Normalize(records);

        Assert.Equal(records.Count, rows.Count);
        foreach (var (record, row) in records.Zip(rows))
        {
            Assert.Equal(record.Cells.Count, row.Context.Raw.Count);
            Assert.Equal(
                record.Cells.OrderBy(cell => cell.Key, StringComparer.Ordinal),
                row.Context.Raw.OrderBy(cell => cell.Key, StringComparer.Ordinal));
            Assert.Equal(record.Cells.Keys.Order(StringComparer.Ordinal), row.Context.Raw.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
            Assert.Equal(record.Cells.Values.Count(), row.Context.Raw.Values.Count());
            Assert.All(record.Cells, cell => Assert.True(row.Context.Raw.ContainsKey(cell.Key)));
            Assert.All(record.Cells, cell => Assert.Equal(cell.Value, row.Context.Raw[cell.Key]));
            Assert.False(row.Context.Raw.TryGetValue("NoSuchColumn", out _));
        }
    }

    [Fact]
    public void A_rule_cannot_cast_intakes_raw_cells_back_to_a_dictionary_and_write_to_them()
    {
        var records = RecordSources.FromObjects("left", Trades, Map).Materialize("left", "Id", CancellationToken.None);

        var raw = Normalize(records)[0].Context.Raw;

        Assert.False(raw is IDictionary<string, object?>, "Raw can be cast to a writable dictionary.");
        Assert.False(raw is System.Collections.IDictionary, "Raw can be cast to a writable dictionary.");
        Assert.NotSame(records[0].Cells, raw);
    }

    [Fact]
    public void The_result_reads_raw_text_from_the_cells_the_source_built()
    {
        using var table = Table();
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", Trades, Map),
            RecordSources.FromDataReader("right", table.CreateDataReader, new TabularOptions()),
            ReferenceDataSet.Empty);
        var left = result.Rows.Where(row => row.Row.Source is "left").ToDictionary(row => row.Row.RowKey, row => row.Row, StringComparer.Ordinal);
        var right = result.Rows.Where(row => row.Row.Source is "right").ToDictionary(row => row.Row.RowKey, row => row.Row, StringComparer.Ordinal);

        Assert.Equal(("T1", "JPM", "1234.5", "06/11/2025"), Raw(result, left["T1"]));
        Assert.Equal(("T2", null, null, null), Raw(result, left["T2"]));
        Assert.Equal(("T1", "JPM", "1234.5", "06/11/2025"), Raw(result, right["T1"]));
        Assert.Equal(("T2", null, null, null), Raw(result, right["T2"]));
        Assert.Null(result.Raw(left["T1"], "NoSuchColumn"));
    }

    [Fact]
    public void Starting_a_row_from_its_record_allocates_under_1600_bytes_per_row()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var records = RecordSources.FromObjects("left", [.. Enumerable.Range(0, 1_000).Select(Wide)], WideMap).Materialize("left", "Id", CancellationToken.None);
        _ = Intake.Normalize(profile, records, [], ReferenceDataSet.Empty);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _kept = [.. Intake.Normalize(profile, records, [], ReferenceDataSet.Empty).Left];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // One identifier rule over eight cells. With the cells copied into a second, immutable dictionary this was
        // about 1,970 bytes per row; without the copy, about 1,230. The bound sits between the two with room to spare.
        var perRow = (double)allocated / _kept.Length;
        Assert.True(perRow < 1_600, $"Normalizing an eight-cell row allocated {perRow.ToInvariant("F1")} bytes per row, above the 1,600-byte budget.");
    }

    [Fact]
    public void An_object_source_keeps_no_reference_to_the_cells_it_built()
    {
        var source = RecordSources.FromObjects("left", Trades, Map);

        var cells = CellsOfEveryRow(source);
        Collect();

        Assert.All(cells, row => Assert.False(row.IsAlive, "The source still holds a row's cells."));
        GC.KeepAlive(source);
        GC.KeepAlive(Map);
    }

    [Fact]
    public void An_async_object_source_keeps_no_reference_to_the_cells_it_built()
    {
        var source = RecordSources.FromObjects("left", Stream(), Map);

        var cells = CellsOfEveryRow(source);
        Collect();

        Assert.All(cells, row => Assert.False(row.IsAlive, "The source still holds a row's cells."));
        GC.KeepAlive(source);
    }

    [Fact]
    public void A_data_reader_source_keeps_no_reference_to_the_cells_it_built()
    {
        using var table = Table();
        var source = RecordSources.FromDataReader("right", table.CreateDataReader, new TabularOptions());

        var cells = CellsOfEveryRow(source);
        Collect();

        Assert.All(cells, row => Assert.False(row.IsAlive, "The source still holds a row's cells."));
        GC.KeepAlive(source);
    }

    // The records die with this frame, so afterwards only the source could still hold a row's cells.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CellsOfEveryRow(RecordSource source) =>
        [.. source.Materialize("left", "Id", CancellationToken.None).Select(record => new WeakReference(record.Cells))];

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static IReadOnlyList<IntakeRow> Normalize(IReadOnlyList<RawRecord> records) =>
        Intake.Normalize(Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()), records, [], ReferenceDataSet.Empty).Left;

    private static (string? Id, string? Party, string? Quantity, string? Day) Raw(IReconciliationResult result, SourceRef row) =>
        (result.Raw(row, "Id"), result.Raw(row, "Party"), result.Raw(row, "Qty"), result.Raw(row, "Day"));

    private static async IAsyncEnumerable<Trade> Stream()
    {
        foreach (var trade in Trades)
        {
            await Task.Yield();
            yield return trade;
        }
    }

    private static DataTable Table()
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Party", typeof(string));
        table.Columns.Add("Qty", typeof(decimal));
        table.Columns.Add("Day", typeof(DateTime));
#pragma warning restore IDISP004
        table.Rows.Add("T1", "JPM", 1234.5m, new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Unspecified));
        table.Rows.Add("T2", DBNull.Value, DBNull.Value, DBNull.Value);
        return table;
    }

    private sealed record WideRow(string Id, string Text);

    // Text cells only: a decimal or date cell boxes its value in the source and would blur the dictionary's own cost.
    private static readonly SourceMap<WideRow> WideMap = new SourceMap<WideRow>()
        .Text("Id", row => row.Id)
        .Text("C2", row => row.Text)
        .Text("C3", row => row.Text)
        .Text("C4", row => row.Text)
        .Text("C5", row => row.Text)
        .Text("C6", row => row.Text)
        .Text("C7", row => row.Text)
        .Text("C8", row => row.Text);

    private static WideRow Wide(int index) => new("T" + index.ToInvariant(), "text");
}
