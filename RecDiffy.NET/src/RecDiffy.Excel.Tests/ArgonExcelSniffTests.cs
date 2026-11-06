using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

public class ArgonExcelSniffTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 4);

    [Fact]
    public void WriteAll_does_not_crash_on_sheet_names_or_cell_text_over_32767_chars_and_invalid_xml_chars()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "excel-limits",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "Instrument", "type": "Symbol", "role": "Key" },
                { "name": "Asset", "type": "Symbol", "role": "Key" },
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Currency", "type": "Symbol", "role": "Economic" },
                { "name": "TradeDate", "type": "Date", "role": "Key" },
                { "name": "SettleDate", "type": "Date", "role": "Economic" },
                { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
                { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
                { "name": "Book", "type": "Text", "role": "Text" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                    { "rule": "symbol.map", "from": "Asset", "to": "Asset" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" },
                    { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                    { "rule": "text.copy", "from": "Book", "to": "Book" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                    { "rule": "symbol.map", "from": "Asset", "to": "Asset" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" },
                    { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                    { "rule": "text.copy", "from": "Book", "to": "Book" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var longText = new string('X', 40_000) + "\u0001\u0007";
        var map = new SourceMap<(string Id, string Cpty, string Inst, string Qty, string Cash, string Book)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", r => r.Cpty)
            .Text("Instrument", r => r.Inst)
            .Text("Asset", _ => "Equity")
            .Text("Side", _ => "BUY")
            .Text("Currency", _ => "USD")
            .Text("TradeDate", _ => "2026-11-02")
            .Text("SettleDate", _ => "2026-11-04")
            .Text("Quantity", r => r.Qty)
            .Text("NetCash", r => r.Cash)
            .Text("Book", r => r.Book);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", longText, "EQ-1", "100", "1000", longText)], map),
            RecordSources.FromObjects("right", [("R1", longText, "EQ-1", "90", "900", "BOOK")], map),
            ReferenceDataSet.Empty);

        var outcomes = result.WriteAll(Paths.Scratch("excel-long"), new ExcelReportOptions { AsOf = AsOf, Profile = profile });
        var written = outcomes.Where(o => o.Written).ToArray();
        Assert.NotEmpty(written);
        Assert.All(written, outcome => Assert.Empty(Workbooks.SchemaErrors(outcome.Path!)));

        var blotter = Book.Read(written.Single(o => o.Kind == ReportKind.ExceptionBlotter).Path!)["BO1_ExceptionBlotter"];
        Assert.All(blotter.Values, cell => Assert.True((cell.Value?.Length ?? 0) <= 32_767));
    }

    [Fact]
    public void Clean_does_not_split_surrogate_pair_at_32767_character_boundary()
    {
        var prefix = new string('A', 32_766);
        var emoji = "\U0001F600"; // 2 UTF-16 code units: high surrogate at index 32766, low surrogate at index 32767
        var text = prefix + emoji;

        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "surrogate-boundary",
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
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Cpty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", r => r.Cpty)
            .Text("Instrument", _ => "EQ-1")
            .Text("Side", _ => "BUY")
            .Text("Currency", _ => "USD")
            .Text("TradeDate", _ => "2026-11-02")
            .Text("SettleDate", _ => "2026-11-04");

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", text)], map),
            RecordSources.FromObjects("right", Array.Empty<(string, string)>(), map),
            ReferenceDataSet.Empty);

        using var ms = new MemoryStream();
        var outcome = result.WriteReport(ReportKind.ExceptionBlotter, ms, new ExcelReportOptions { AsOf = AsOf, Profile = profile });
        Assert.True(outcome.Written);
    }

    [Fact]
    public void Allocations_report_handles_group_where_both_sides_have_multiple_rows_without_crashing()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "alloc-dup-block",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                },
                "right": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Text("Quantity", r => r.Qty);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", "4"), ("L2", "A", "6")], map),
            RecordSources.FromObjects("right", [("R1", "A", "10"), ("R1", "A", "10")], map),
            ReferenceDataSet.Empty);

        using var ms = new MemoryStream();
        var outcome = result.WriteReport(ReportKind.Allocations, ms, new ExcelReportOptions { AsOf = AsOf, Profile = profile });
        Assert.True(outcome.Written);

        var path = Path.Combine(Paths.Scratch("alloc-dup"), outcome.FileName);
        File.WriteAllBytes(path, ms.ToArray());
        var sheet = Book.Read(path)["BO5_Allocations"];
        Assert.Contains(sheet.Values, c => c.Value == "L1");
        Assert.Contains(sheet.Values, c => c.Value == "L2");
    }

    [Fact]
    public void LifecycleChains_report_handles_both_sides_collapsed_in_one_matched_entry()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "both-sides-lifecycle",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
                { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "consolidation": {
                "left": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } },
                "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Counterparty", "Quantity"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Action, string? Target, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target)
            .Text("Quantity", r => r.Qty);

        var leftRows = new[]
        {
            ("L1n", "NEW", (string?)null, "50"),
            ("L1c", "CANC", "L1n", "50"),
            ("L1r", "CORR", "L1c", "100"),
        };
        var rightRows = new[]
        {
            ("R1n", "NEW", (string?)null, "80"),
            ("R1c", "CANC", "R1n", "80"),
            ("R1r", "CORR", "R1c", "100"),
        };

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", leftRows, map),
            RecordSources.FromObjects("right", rightRows, map),
            ReferenceDataSet.Empty);

        using var ms = new MemoryStream();
        var outcome = result.WriteReport(ReportKind.LifecycleChains, ms, new ExcelReportOptions { AsOf = AsOf, Profile = profile });
        Assert.True(outcome.Written);

        var path = Path.Combine(Paths.Scratch("both-life"), outcome.FileName);
        File.WriteAllBytes(path, ms.ToArray());
        var sheet = Book.Read(path)["BO4_LifecycleChains"];
        Assert.Contains(sheet.Values, c => c.Value == "L1n");
        Assert.Contains(sheet.Values, c => c.Value == "R1n");
    }
}
