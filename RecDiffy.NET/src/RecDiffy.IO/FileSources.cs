using System.Data;

namespace RecDiffy.IO;

/// <summary>
/// Record sources for CSV and Excel files, read by Sylvan.Data.Csv and Sylvan.Data.Excel and passed to
/// <see cref="RecordSources.FromDataReader"/>. Nothing is parsed, detected or repaired here: CSV cells reach the profile
/// as text, Excel cells as the number, date or text the workbook stores, and the profile's rules do the rest.
/// </summary>
/// <remarks>
/// A path is reopened for every run. A stream can be read once, and stays open: the caller owns it. Left and right are
/// independent sources, so any combination of formats works.
/// </remarks>
public static class FileSources
{
    public static RecordSource FromCsv(string name, string path, CsvOptions options)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        return RecordSources.FromDataReader(name, () => TabularReaders.Csv(File.OpenRead(path), options, ownsStream: true), new TabularOptions());
    }

    public static RecordSource FromCsv(string name, Stream stream, CsvOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        return RecordSources.FromDataReader(name, Once(() => TabularReaders.Csv(stream, options, ownsStream: false)), new TabularOptions());
    }

    public static RecordSource FromExcel(string name, string path, ExcelOptions options)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        return RecordSources.FromDataReader(name, () => TabularReaders.Excel(path, options), new TabularOptions());
    }

    public static RecordSource FromExcel(string name, Stream stream, ExcelFormat format, ExcelOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        return RecordSources.FromDataReader(name, Once(() => TabularReaders.Excel(stream, format, options)), new TabularOptions());
    }

    /// <summary>The workbook's sheet names in tab order, hidden sheets included. Sheet number n is item n − 1.</summary>
    public static IReadOnlyList<string> ExcelSheetNames(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return TabularReaders.SheetNames(path);
    }

    /// <summary>Reads a CSV file's headers and a seeded random sample of rows from across the whole file.</summary>
    public static TabularPeek PeekCsv(string path, CsvOptions options, int sampleSize, int seed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        using var reader = TabularReaders.Csv(File.OpenRead(path), options, ownsStream: true);
        return TabularPeek.Read(reader, sampleSize, seed, cancellationToken);
    }

    public static TabularPeek PeekCsv(Stream stream, CsvOptions options, int sampleSize, int seed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        using var reader = TabularReaders.Csv(stream, options, ownsStream: false);
        return TabularPeek.Read(reader, sampleSize, seed, cancellationToken);
    }

    /// <summary>Reads one sheet's headers and a seeded random sample of rows from across the whole sheet.</summary>
    public static TabularPeek PeekExcel(string path, ExcelOptions options, int sampleSize, int seed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);
        using var reader = TabularReaders.Excel(path, options);
        return TabularPeek.Read(reader, sampleSize, seed, cancellationToken);
    }

    public static TabularPeek PeekExcel(Stream stream, ExcelFormat format, ExcelOptions options, int sampleSize, int seed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        using var reader = TabularReaders.Excel(stream, format, options);
        return TabularPeek.Read(reader, sampleSize, seed, cancellationToken);
    }

    // A caller's stream cannot be rewound reliably, so a stream source refuses a second run instead of reading nothing.
    private static Func<IDataReader> Once(Func<IDataReader> open)
    {
        var opened = 0;
        return () => Interlocked.Exchange(ref opened, 1) == 0
            ? open()
            : throw new InvalidOperationException("A source made from a stream can be read once. Make a new source, or use a file path, to read the file again.");
    }
}
