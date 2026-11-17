using System.Globalization;
using RecDiffy.IO;

namespace RecDiffy.BrowserDemo.Demo;

/// <summary>One side's file, read in the browser: its bytes, its columns and a sample of its rows.</summary>
/// <param name="Name">The file name.</param>
/// <param name="Bytes">The file's contents. They stay in this page's memory.</param>
/// <param name="Excel">The workbook format, or <see langword="null"/> for a CSV file.</param>
/// <param name="Peek">The column names, a sample of rows and the row count.</param>
public sealed record LoadedFile(string Name, byte[] Bytes, ExcelFormat? Excel, TabularPeek Peek)
{
    private const int SampleSize = 200;
    private const int Seed = 7;

    private static readonly CsvOptions Csv = new() { HasHeader = true, Delimiter = CsvDelimiter.DetectAutomatically };
    private static readonly ExcelOptions FirstSheet = new() { HasHeader = true, Sheet = ExcelSheet.Numbered(1) };

    public IReadOnlyList<string> Headers => Peek.Headers;

    public static LoadedFile Read(string name, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(bytes);

        var excel = Path.GetExtension(name).ToUpperInvariant() switch
        {
            ".XLSX" => ExcelFormat.Xlsx,
            ".XLSB" => ExcelFormat.Xlsb,
            ".XLS" => ExcelFormat.Xls,
            _ => (ExcelFormat?)null,
        };

        using var stream = new MemoryStream(bytes, writable: false);
        var peek = excel switch
        {
            { } format => FileSources.PeekExcel(stream, format, FirstSheet, SampleSize, Seed),
            null => FileSources.PeekCsv(stream, Csv, SampleSize, Seed),
        };

        return new LoadedFile(name, bytes, excel, peek);
    }

    /// <summary>A read-only stream over the file's bytes. The caller disposes it.</summary>
    public MemoryStream Open() => new(Bytes, writable: false);

    /// <summary>A record source over <paramref name="stream"/>, for the run. The stream stays open until the run is done.</summary>
    public RecordSource Source(string sourceName, Stream stream) => Excel switch
    {
        { } format => FileSources.FromExcel(sourceName, stream, format, FirstSheet),
        null => FileSources.FromCsv(sourceName, stream, Csv),
    };

    /// <summary>True when every non-blank sampled value in the column reads as a number.</summary>
    public bool IsNumeric(string column) => Values(column) is { Count: > 0 } values && values.TrueForAll(value => Number(value) is not null);

    /// <summary>The most decimal places any sampled value in the column has.</summary>
    public int Decimals(string column) => Values(column).Select(Number).Max(number => number?.Scale ?? 0);

    /// <summary>The first sampled rows, as text, for a preview.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Preview(int count) =>
        [.. Peek.Rows.Take(count).Select(row => (IReadOnlyList<string>)[.. row.Values.Select(Text)])];

    private List<object> Values(string column) => Headers.ToList().IndexOf(column) switch
    {
        < 0 => [],
        var index => [.. Peek.Rows.Select(row => row.Values[index]).Where(value => value is not null && Text(value).Length > 0).Select(value => value!)],
    };

    private static decimal? Number(object value) => value switch
    {
        decimal number => number,
        double number => (decimal)number,
        int number => number,
        long number => number,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("en-US"), out var number) => number,
        _ => null,
    };

    private static string Text(object? value) => value switch
    {
        null => "",
        DateTime { TimeOfDay.Ticks: 0 } date => date.ToInvariant("yyyy-MM-dd"),
        DateTime time => time.ToInvariant("yyyy-MM-dd HH:mm:ss"),
        DateOnly date => date.ToInvariant("yyyy-MM-dd"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? "",
    };
}
