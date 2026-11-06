using System.Data;
using System.Globalization;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// 🔴 quantity.absolute reads its raw value only as decimal or string. A data reader passes int, long and double cells
// through unchanged (DataReaderSource.CellOf keeps int and long), so a whole-number quantity from a DataTable, a
// database or any reader that types its columns becomes a null Quantity, with no diagnostic. decimal.parse and
// side.resolve read the same values correctly. Found while wiring RecDiffy.IO; fixing it is a CHANGE to
// QuantityAbsolute, which this work does not name.
public class QuantityAbsoluteRawTypeTests
{
    [Theory(Skip = "🔴 Known defect: quantity.absolute ignores int and long raw values. Remove Skip with the fix.")]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    public void A_whole_number_quantity_from_a_data_reader_is_read(Type type)
    {
        using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Qty", type);
#pragma warning restore IDISP004
        table.Rows.Add("1", Convert.ChangeType(-100, type, CultureInfo.InvariantCulture));

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()),
            RecordSources.FromObjects("right", Array.Empty<string>(), new SourceMap<string>()),
            ReferenceDataSet.Empty);

        Assert.Equal(100m, result.Canonical(Assert.Single(result.Rows).Row)["Quantity"].Number);
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "quantity",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": { "left": { "derive": [ { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" } ] } },
          "paths": [ { "id": "all", "route": { "rule": "route.all" } } ]
        }
        """;
}
