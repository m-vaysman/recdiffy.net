using System.Data;
using System.Globalization;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// quantity.absolute used to read its raw value only as decimal or string. A data reader passes int and long
// through unchanged, so a whole-number quantity became a null Quantity. AbsoluteQuantityRule reads those types.
public class QuantityAbsoluteRawTypeTests
{
    [Theory]
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
