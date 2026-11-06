using System.Data;

namespace RecDiffy.Tests;

public class ArgonDataReaderTests
{
    [Fact]
    public void DataReaderSource_reads_all_rows_without_consuming_the_first_data_row_as_header()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(SimpleDecimalProfile), RuleRegistry.CreateCore());
        using var table = new DataTable();
        using var idCol = table.Columns.Add("Id", typeof(string));
        using var qtyCol = table.Columns.Add("Quantity", typeof(decimal));
        _ = table.Rows.Add("L1", 10m);
        _ = table.Rows.Add("L2", 20m);

        var left = RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions(0, "Id"));
        var right = RecordSources.FromObjects("right", Array.Empty<Dictionary<string, string>>(), SimpleDecimalMap());

        var result = Reconciler.Run(profile, left, right, ReferenceDataSet.Empty);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["L1", "L2"], result.Rows.Select(r => r.Row.RowKey).OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static SourceMap<Dictionary<string, string>> SimpleDecimalMap() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", r => r["Id"])
            .Text("Quantity", r => r["Quantity"]);

    private const string SimpleDecimalProfile = """
        {
          "formatVersion": 1,
          "profileId": "simple-decimal",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": {
            "left": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
        }
        """;
}
