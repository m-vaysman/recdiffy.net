using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RecDiffy.Results;
using Xunit.Abstractions;

namespace RecDiffy.IO.Tests;

// Adversarial pass on main. README: "Excel numbers arrive as the decimal the workbook stores"; spec §7.3: a number no
// decimal holds exactly stays a double and is reported (FloatValue). Generates large workbooks with many sheets, noisy
// doubles, huge and tiny values, dates and blanks, reads every sheet through FileSources.FromExcel, and checks each cell.
// Scale with RECDIFFY_XLSX_SHEETS (default 6) and RECDIFFY_XLSX_ROWS (rows per sheet, default 5000).
public class AdversarialWorkbookStressTests(ITestOutputHelper output)
{
    private const string Fields = """
        { "name": "Id", "type": "Identifier", "role": "Key" },
        { "name": "Amount", "type": "Decimal", "scale": 28, "role": "Economic" },
        { "name": "Day", "type": "Date", "role": "Economic" },
        { "name": "Note", "type": "Text", "role": "Text" }
        """;

    private const string Rules = """
        "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "ID" }, { "rule": "text.copy", "from": "Note", "to": "Note" } ],
        "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "date.parse", "from": "Day", "to": "Day" } ]
        """;

    private static int Env(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    [Fact]
    public void Every_cell_of_a_large_noisy_workbook_reads_as_the_decimal_it_stores()
    {
        var sheets = Env("RECDIFFY_XLSX_SHEETS", 6);
        var rows = Env("RECDIFFY_XLSX_ROWS", 5000);
        var clock = Stopwatch.StartNew();
        var expected = Enumerable.Range(0, sheets).Select(sheet => Generate(sheet, rows)).ToArray();
        var path = Write(TestFiles.Folder(), expected);
        var written = clock.Elapsed;
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var failures = new ConcurrentBag<string>();
        var largeWhole = 0;
        var cells = 0;
        Parallel.For(0, sheets, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, sheet =>
        {
            var source = FileSources.FromExcel("left", path, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(sheet + 1) });
            var result = Probe.Run(source, Probe.Empty(), Fields, Rules);
            var byOrdinal = result.Rows.Where(row => row.Row.Source == "left").OrderBy(row => row.Row.Ordinal).ToArray();
            if (byOrdinal.Length != expected[sheet].Length)
                failures.Add($"sheet {sheet + 1}: {byOrdinal.Length} rows read, {expected[sheet].Length} written");
            foreach (var (row, cell) in byOrdinal.Zip(expected[sheet]))
            {
                var canonical = result.Canonical(row.Row);
                var codes = string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code).Where(code => code is not "DuplicateConflict"));
                var amount = canonical.IsNull("Amount") ? $"null[{codes}]" : canonical["Amount"].Number.ToString(CultureInfo.InvariantCulture);
                var want = cell.Amount switch
                {
                    null => "null[]",
                    var stored when cell.Exact is { } exact => exact.ToString(CultureInfo.InvariantCulture),
                    _ => "null[FloatValue]",
                };
                // A whole number from 2^53 up to the long range is the Int64 defect (AdversarialWorkbookIntegerTests); count it apart.
                if (string.Equals(amount, want, StringComparison.Ordinal) is false && cell.Exact is { } whole && decimal.Truncate(whole) == whole && Math.Abs(whole) >= 9007199254740992m && Math.Abs(whole) <= long.MaxValue)
                    Interlocked.Increment(ref largeWhole);
                else if (string.Equals(amount, want, StringComparison.Ordinal) is false)
                    failures.Add($"sheet {sheet + 1} {cell.Id}: stored '{cell.Amount}' read {amount}, expected {want}");
                Interlocked.Increment(ref cells);
                var day = canonical.IsNull("Day") ? null : (DateOnly?)canonical["Day"].Date;
                if (day != cell.Day)
                    failures.Add($"sheet {sheet + 1} {cell.Id}: day {cell.Day} read {day}");
            }
        });
        var used = Process.GetCurrentProcess().TotalProcessorTime - cpu;
        output.WriteLine($"sheets={sheets} rows/sheet={rows} file={new FileInfo(path).Length / 1024} KB write={written.TotalSeconds:F1}s read+check={(clock.Elapsed - written).TotalSeconds:F1}s cpu={used.TotalSeconds:F1}s rows checked={cells} large-whole Int64 mismatches={largeWhole} other failures={failures.Count}");
        foreach (var failure in failures.OrderBy(text => text, StringComparer.Ordinal).Take(20))
            output.WriteLine(failure);
        Assert.Empty(failures);
    }

    // Stored is the text written in the cell: the double's shortest round-trip text, as the README says programs write.
    // Exact is the decimal that text holds, or null when no decimal holds it exactly.
    private sealed record Written(string Id, string? Amount, decimal? Exact, DateOnly? Day, string? Note);

    private static Written[] Generate(int sheet, int rows)
    {
        var random = new Random(sheet * 104_729 + 1);
        return [.. Enumerable.Range(0, rows).Select(index =>
        {
            var id = $"S{sheet}-{index.ToString(CultureInfo.InvariantCulture)}";
            double? number = random.Next(12) switch
            {
                0 => null,
                1 => Math.Round(random.NextDouble() * 1e6, 2) + Math.Round(random.NextDouble() * 1e3, 2),
                2 => 0.1 * random.Next(1, 100) + 0.2 * random.Next(1, 100),
                3 => random.NextDouble() * Math.Pow(10, random.Next(15, 32)),
                4 => random.NextDouble() * Math.Pow(10, -random.Next(5, 32)),
                5 => -Math.Round(random.NextDouble() * 1e9, 4),
                6 => random.Next(2) == 0 ? -0.0 : 0.0,
                7 => (double)(1L << random.Next(53, 63)) + random.Next(0, 4096),
                8 => random.NextDouble(),
                9 => random.Next(-1_000_000, 1_000_000),
                10 => 1 / 3.0 * random.Next(1, 1000),
                _ => Math.Round(random.NextDouble() * 100, 6),
            };
            var stored = number?.ToString("R", CultureInfo.InvariantCulture);
            var day = random.Next(8) == 0 ? (DateOnly?)null : new DateOnly(1900, 3, 1).AddDays(random.Next(0, 73_000));
            var note = random.Next(3) switch { 0 => null, 1 => "", _ => "n" + index.ToString(CultureInfo.InvariantCulture) };
            return new Written(id, stored, stored is null ? null : ExactDecimal(stored), day, note);
        })];
    }

    // The decimal a text holds, only when the decimal holds every digit of it.
    private static decimal? ExactDecimal(string text)
    {
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) is false)
            return null;
        var mantissa = text.Split('E', 'e')[0].TrimStart('-').Replace(".", "", StringComparison.Ordinal).Trim('0');
        var exponent = text.Contains('E', StringComparison.OrdinalIgnoreCase) ? int.Parse(text.Split('E', 'e')[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
        var fraction = (text.Split('E', 'e')[0].Split('.') is [_, var digits] ? digits.TrimEnd('0').Length : 0) - exponent;
        return fraction <= 28 && mantissa.Length <= 29 ? value : null;
    }

    private static string Write(string folder, Written[][] sheets)
    {
        var path = Path.Combine(folder, "stress.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(new Font()),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
        var list = new Sheets();
        foreach (var (cells, index) in sheets.Select((cells, index) => (cells, index)))
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            using (var writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, 1, [Text("A1", "Id"), Text("B1", "Amount"), Text("C1", "Day"), Text("D1", "Note")]);
                foreach (var (cell, offset) in cells.Select((cell, offset) => (cell, offset)))
                {
                    var number = (uint)(offset + 2);
                    var n = number.ToString(CultureInfo.InvariantCulture);
                    WriteRow(writer, number, [
                        Text("A" + n, cell.Id),
                        cell.Amount is { } amount ? new Cell(new CellValue(amount)) { CellReference = "B" + n, DataType = CellValues.Number } : null,
                        cell.Day is { } day ? new Cell(new CellValue(day.ToDateTime(TimeOnly.MinValue).ToOADate().ToString("R", CultureInfo.InvariantCulture))) { CellReference = "C" + n, StyleIndex = 1 } : null,
                        cell.Note is { } note ? Text("D" + n, note) : null,
                    ]);
                }

                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            list.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)(index + 1), Name = "S" + (index + 1).ToString(CultureInfo.InvariantCulture) });
        }

        workbook.Workbook.AppendChild(list);
        workbook.Workbook.Save();
        return path;
    }

    private static void WriteRow(OpenXmlWriter writer, uint number, Cell?[] cells)
    {
        writer.WriteStartElement(new Row { RowIndex = number });
        foreach (var cell in cells.OfType<Cell>())
            writer.WriteElement(cell);
        writer.WriteEndElement();
    }

    private static Cell Text(string reference, string text) =>
        new(new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text) { Space = SpaceProcessingModeValues.Preserve })) { CellReference = reference, DataType = CellValues.InlineString };
}
