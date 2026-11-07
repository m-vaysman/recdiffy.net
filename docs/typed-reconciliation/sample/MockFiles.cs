using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

// Writes the two mock files the finance example reads.
public static class MockFiles
{
    public static void Write(string folder)
    {
        File.WriteAllText(Path.Combine(folder, "broker-confirms.csv"), """
            ConfirmId,TradeRef,TradeDate,ISIN,Side,Quantity,Price,NetAmount
            C-1001,T-5001,2026-11-05,US0378331005,B,1000,189.25,189250.00
            C-1002,T-5002,2026-11-05,US5949181045,S,500,412.10,206050.00
            C-1003,T-5003,2026-11-05,US88160R1014,B,250,245.80,61450.00
            C-1004,,2026-11-05,US02079K3059,B,300,171.55,51465.00
            C-1005,T-5007,2026-11-05,US0231351067,S,120,201.40,24168.00

            """.ReplaceLineEndings("\n"));

        var day = new DateOnly(2026, 11, 5);
        object[][] rows =
        [
            ["Trade ID", "Exec Date", "ISIN", "B/S", "Qty", "Avg Px", "Net Consideration"],
            ["T-5001", day, "US0378331005", "B", 1000, 189.25m, 189250.00m],
            ["T-5002", day, "US5949181045", "S", 500, 412.10m, 206049.99m],
            ["T-5003", day, "US88160R1014", "B", 200, 245.80m, 49160.00m],
            ["T-5004", day, "US02079K3059", "B", 300, 171.55m, 51465.00m],
            ["T-5006", day, "US67066G1040", "B", 80, 145.30m, 11624.00m],
        ];
        Xlsx(Path.Combine(folder, "blotter.xlsx"), "Trades", rows);
    }

    private static void Xlsx(string path, string sheetName, object[][] rows)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = new Stylesheet(
            new Fonts(new Font()),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetData(rows.Select((cells, index) =>
            new Row(cells.Select(Cell)) { RowIndex = (uint)(index + 1) })));
        workbook.Workbook.AppendChild(new Sheets()).Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = sheetName });
        workbook.Workbook.Save();
    }

    private static Cell Cell(object value) => value switch
    {
        string text => new Cell(new InlineString(new Text(text))) { DataType = CellValues.InlineString },
        DateOnly date => new Cell(new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))) { StyleIndex = 1 },
        decimal number => new Cell(new CellValue(number.ToString(CultureInfo.InvariantCulture))) { DataType = CellValues.Number },
        int number => new Cell(new CellValue(number.ToString(CultureInfo.InvariantCulture))) { DataType = CellValues.Number },
        _ => throw new ArgumentException($"Unsupported {value.GetType().Name}", nameof(value)),
    };
}
