using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

// #116 "Untested but holds", item 8: cell text that XML 1.0 cannot carry is replaced, and text longer than an Excel cell
// holds is cut to 32,767 characters, so the report is still a valid workbook.
public class ArgonExcelHoldsTests
{
    private const int MaxCellText = 32_767;

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "argon-excel-holds",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "symbol.map", "from": "Side", "to": "Side" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }
              ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
        }
        """;

    // \u0001 and \u0007 are not XML 1.0 characters. Each becomes U+FFFD, and the rest of the text is kept.
    [Fact]
    public void Control_characters_in_cell_text_are_replaced()
    {
        var path = Write("AC\u0001ME\u0007");

        Assert.Contains(Texts(path), text => text == "AC�ME�");
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    // 40,000 characters with control characters inside: the cell holds the first 32,767, cleaned.
    [Fact]
    public void Cell_text_longer_than_a_cell_holds_is_cut_to_the_limit()
    {
        var text = "\u0001" + new string('A', 20_000) + "\u0007" + new string('B', 19_998);

        var path = Write(text);

        var expected = "�" + new string('A', 20_000) + "�" + new string('B', MaxCellText - 20_002);
        Assert.Contains(Texts(path), cell => cell == expected);
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    internal static string Write(string counterparty)
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var map = new SourceMap<(string Id, string Cpty)>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", row => row.Cpty)
            .Text("Instrument", _ => "EQ-1")
            .Text("Side", _ => "BUY")
            .Text("Currency", _ => "USD")
            .Text("TradeDate", _ => "2026-11-02")
            .Text("SettleDate", _ => "2026-11-04");
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", counterparty)], map),
            RecordSources.FromObjects("right", Array.Empty<(string, string)>(), map),
            ReferenceDataSet.Empty);
        var folder = Path.Combine(Path.GetTempPath(), "recdiffy-excel-tests", "argon-holds", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "blotter.xlsx");
        using (var file = File.Create(path))
            Assert.True(result.WriteReport(ReportKind.ExceptionBlotter, file, new ExcelReportOptions { AsOf = new DateOnly(2026, 11, 4), Profile = profile }).Written);
        return path;
    }

    internal static IEnumerable<string> Texts(string path) =>
        Book.Read(path).Sheets.Values.SelectMany(sheet => sheet.Values).Select(cell => cell.Value).OfType<string>();
}
