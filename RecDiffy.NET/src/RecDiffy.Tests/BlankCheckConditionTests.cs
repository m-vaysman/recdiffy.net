using System.Data;
using System.Globalization;

namespace RecDiffy.Tests;

// An empty operand is a blank check, because a blank cell is data (spec v3 §7.5; #219). A data reader, which the file
// readers use, delivers a blank cell as no value, and a condition reads no value as null, which never equals "" (#280).
public class BlankCheckConditionTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "blank-check",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Symbol", "role": "Key" },
            { "name": "Note", "type": "Text", "role": "Text" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" }, { "rule": "text.copy", "from": "Note", "to": "Note" } ],
              "validate": [ { "rule": "required.present", "fields": ["Note"], "when": { "field": "Flag", "equals": "" } } ]
            },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Id", "to": "Ref" } ] }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    [Fact(Skip = "#280: a blank check never holds on a blank cell from a data reader.")]
    public void An_equals_empty_condition_holds_on_a_blank_cell_from_a_data_reader()
    {
        using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Flag", typeof(string));
        table.Columns.Add("Note", typeof(string));
#pragma warning restore IDISP004
        table.Rows.Add("A", "", "");

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()),
            RecordSources.FromObjects<string>("right", [], new SourceMap<string>().Text("Id", id => id)),
            ReferenceDataSet.Empty);

        var row = Assert.Single(result.Rows);
        Assert.Equal("RequiredMissing", Assert.Single(row.Diagnostics).Code);
    }
}
