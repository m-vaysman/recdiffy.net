using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

public class ArgonAllocationsTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 4);

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
}
