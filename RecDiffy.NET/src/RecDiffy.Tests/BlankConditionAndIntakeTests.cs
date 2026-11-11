using System.Data;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class BlankConditionAndIntakeTests
{
    private const string BlankRawConditionProfile = """
        {
          "formatVersion": 1,
          "profileId": "blank-raw-condition",
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

    private const string CanonicalApplicableWhenProfile = """
        {
          "formatVersion": 1,
          "profileId": "blank-canonical-applicable-when",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Identity" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Ref", "scheme": "id" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Ref", "scheme": "id" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" } ]
            }
          },
          "paths": [ {
            "id": "all",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "Accrued": { "applicableWhen": { "field": "AssetClass", "notEquals": "" } } } }
          } ]
        }
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Source_map_text_normalizes_blank_and_whitespace_to_null_so_when_matcher_reads_null(string? input)
    {
        var map = new SourceMap<string?>().Text("Col", value => value);
        var cells = map.Project(input);
        var row = RowContext.Create("1", Side.Left, cells);

        Assert.Null(cells["Col"]);
        Assert.Null(WhenMatcher.Text(row, "Col"));
    }

    [Theory]
    [InlineData(null, true, false, true)]
    [InlineData("", true, false, true)]
    [InlineData("   ", true, false, true)]
    [InlineData("FX", false, true, true)]
    [InlineData("EQ", false, true, false)]
    public void Empty_operands_check_for_blank_across_equals_not_equals_and_in(string? raw, bool equalsBlank, bool notEqualsBlank, bool inBlankOrFx)
    {
        var row = RuleBench.Row(("Flag", raw));

        Assert.Equal(equalsBlank, WhenMatcher.Holds(new WhenClause("Flag", "", null, null), row));
        Assert.Equal(notEqualsBlank, WhenMatcher.Holds(new WhenClause("Flag", null, "", null), row));
        Assert.Equal(inBlankOrFx, WhenMatcher.Holds(new WhenClause("Flag", null, null, ["", "FX"]), row));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_equals_empty_condition_holds_on_a_blank_cell_from_a_data_reader_and_from_objects(string? flag)
    {
        var compiled = Reconciler.Compile(ProfileDocument.Parse(BlankRawConditionProfile), RuleRegistry.CreateCore());
        var map = new SourceMap<(string Id, string? Flag, string? Note)>()
            .Text("Id", row => row.Id)
            .Text("Flag", row => row.Flag)
            .Text("Note", row => row.Note);
        using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Flag", typeof(string));
        table.Columns.Add("Note", typeof(string));
#pragma warning restore IDISP004
        table.Rows.Add("A", (object?)flag ?? DBNull.Value, DBNull.Value);

        var fromReader = Reconciler.Run(
            compiled,
            RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()),
            RecordSources.FromObjects<(string Id, string? Flag, string? Note)>("right", [], map),
            ReferenceDataSet.Empty);
        var fromObjects = Reconciler.Run(
            compiled,
            RecordSources.FromObjects("left", [("A", flag, (string?)null)], map),
            RecordSources.FromObjects<(string Id, string? Flag, string? Note)>("right", [], map),
            ReferenceDataSet.Empty);

        Assert.Equal("RequiredMissing", Assert.Single(Assert.Single(fromReader.Rows).Diagnostics).Code);
        Assert.Equal("RequiredMissing", Assert.Single(Assert.Single(fromObjects.Rows).Diagnostics).Code);
    }

    [Theory]
    [InlineData("", FieldOutcome.Exact)]
    [InlineData("Bond", FieldOutcome.Break)]
    public void Not_equals_empty_on_a_canonical_field_skips_comparison_when_the_field_is_blank(string asset, FieldOutcome expected)
    {
        var compiled = Reconciler.Compile(ProfileDocument.Parse(CanonicalApplicableWhenProfile), RuleRegistry.CreateCore());
        var map = new SourceMap<(string Id, string Asset, string Acc)>()
            .Text("Id", row => row.Id)
            .Text("Asset", row => row.Asset)
            .Text("Acc", row => row.Acc);

        var result = Reconciler.Run(
            compiled,
            RecordSources.FromObjects("left", [("T1", asset, "10.00")], map),
            RecordSources.FromObjects("right", [("T1", asset, "25.00")], map),
            ReferenceDataSet.Empty);

        Assert.Equal(expected, Assert.Single(result.Entries).Outcome);
    }

    [Fact]
    public void Whitespace_only_cells_are_blank_in_price_from32nds_and_date_parse()
    {
        var priceRow = new RuleBench("price.from32nds").With("from", "Px").With("to", "Price").Apply(("Px", "   "));
        var dateRow = new RuleBench("date.parse").With("from", "Raw").With("to", "Day").With("formatBy", "Party").Apply(DateParseInputTests.PartyRow("GAMMA", "   "));

        Assert.True(priceRow.Values["Price"].IsNull);
        Assert.Empty(priceRow.Diagnostics);
        Assert.True(dateRow.Values["Day"].IsNull);
        Assert.Empty(dateRow.Diagnostics);
    }
}
