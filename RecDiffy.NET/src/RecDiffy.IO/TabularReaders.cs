using System.Data;
using System.Globalization;
using Sylvan.Data.Csv;
using Sylvan.Data.Excel;

namespace RecDiffy.IO;

// Opens Sylvan readers and hands them to the engine with their columns named. No parsing logic of our own.
// IDISP001 cannot follow ownership here: every reader created is returned inside a NamedColumns, which disposes it
// (the CSV reader also owns its StreamReader), or is disposed by Owned when the wrapping fails.
#pragma warning disable IDISP001
internal static class TabularReaders
{
    public static IDataReader Csv(Stream stream, CsvOptions options, bool ownsStream)
    {
        var text = new StreamReader(stream, options.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: -1, leaveOpen: !ownsStream);
        var reader = CsvDataReader.Create(text, new CsvDataReaderOptions
        {
            HasHeaders = options.HasHeader,
            Delimiter = options.Delimiter.Character,
            Quote = options.Quote,
            Escape = options.Escape,
            OwnsReader = true,
        });
        return Owned(reader, () => new NamedColumns(reader, Names(reader, options.HasHeader, "the CSV file")));
    }

    public static IDataReader Excel(string path, ExcelOptions options)
    {
        var workbook = ExcelDataReader.Create(path, Options(options, ownsStream: true));
        return Owned(workbook, () => Sheet(workbook, options, $"'{path}'"));
    }

    public static IDataReader Excel(Stream stream, ExcelFormat format, ExcelOptions options)
    {
        var workbook = ExcelDataReader.Create(stream, Workbook(format), Options(options, ownsStream: false));
        return Owned(workbook, () => Sheet(workbook, options, "the stream"));
    }

    public static IReadOnlyList<string> SheetNames(string path)
    {
        using var workbook = ExcelDataReader.Create(path, new ExcelDataReaderOptions { ReadHiddenWorksheets = true });
        return [.. workbook.WorksheetNames];
    }

    // The dynamic schemas keep each cell as the workbook stores it: a number, a date or text. Sylvan's default schema
    // reads every cell as a string, which would turn a date into a serial number or locale-formatted text.
    private static ExcelDataReaderOptions Options(ExcelOptions options, bool ownsStream) => new()
    {
        Schema = options.HasHeader ? ExcelSchema.Dynamic : ExcelSchema.DynamicNoHeaders,
        ReadHiddenWorksheets = true,
        OwnsStream = ownsStream,
    };

    private static ExcelWorkbookType Workbook(ExcelFormat format) => format switch
    {
        ExcelFormat.Xlsx => ExcelWorkbookType.ExcelXml,
        ExcelFormat.Xlsb => ExcelWorkbookType.ExcelBinary,
        ExcelFormat.Xls => ExcelWorkbookType.Excel,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "An Excel format is Xlsx, Xlsb or Xls."),
    };

    private static NamedColumns Sheet(ExcelDataReader workbook, ExcelOptions options, string source)
    {
        var names = workbook.WorksheetNames.ToArray();
        var sheet = options.Sheet switch
        {
            { Name: { } name } when names.Contains(name, StringComparer.Ordinal) => name,
            { Name: { } name } => throw new ArgumentException($"Sheet '{name}' was not found in {source}. {List(names)}", nameof(options)),
            // Intentional 1-based to 0-based conversion: sheet 1 is the first tab, as users count tabs in Excel. Do not remove.
            { Number: var number } when number >= 1 && number <= names.Length => names[number - 1],
            { Number: var number } => throw new ArgumentOutOfRangeException(nameof(options), number, $"Sheet number {number.ToString(CultureInfo.InvariantCulture)} is out of range in {source}: sheet numbers start at 1, the first tab. {List(names)}"),
        };
        return workbook.TryOpenWorksheet(sheet)
            ? new NamedColumns(workbook, Names(workbook, options.HasHeader, $"sheet '{sheet}' of {source}"))
            : throw new InvalidOperationException($"Sheet '{sheet}' of {source} could not be opened.");
    }

    // Returns what wrap builds around a reader this class just created; if wrap fails, the reader is disposed so a
    // rejected file is not left open.
    private static NamedColumns Owned(IDisposable created, Func<NamedColumns> wrap)
    {
        try
        {
            return wrap();
        }
        catch
        {
#pragma warning disable IDISP007 // The caller created this reader and passes ownership; nothing else holds it.
            created.Dispose();
#pragma warning restore IDISP007
            throw;
        }
    }

    private static string List(IReadOnlyList<string> names) =>
        names.Count == 0
            ? "The workbook has no sheets."
            : $"The workbook has {names.Count.ToString(CultureInfo.InvariantCulture)} sheet(s): {string.Join(", ", names.Select((name, index) => $"{(index + 1).ToString(CultureInfo.InvariantCulture)} '{name}'"))}.";

    // A header row must name every column once: an empty or repeated name cannot be a column a profile reads by name.
    // Without a header, columns are Column1 to ColumnN, so profiles map them by position.
    private static string[] Names(IDataReader reader, bool hasHeader, string source)
    {
        if (!hasHeader)
            return [.. Enumerable.Range(1, reader.FieldCount).Select(position => "Column" + position.ToString(CultureInfo.InvariantCulture))];
        if (reader.FieldCount == 0)
            throw new InvalidDataException($"The header row of {source} is empty. With a header, row 1 must name the columns, starting at cell A1.");
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var empty = names.Select((name, index) => (name, index)).Where(pair => string.IsNullOrWhiteSpace(pair.name)).Select(pair => (pair.index + 1).ToString(CultureInfo.InvariantCulture)).ToArray();
        var repeated = names.Where(name => !string.IsNullOrWhiteSpace(name)).GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => $"'{group.Key}'").ToArray();
        string[] problems =
        [
            .. empty.Length > 0 ? [$"column(s) {string.Join(", ", empty)} have no name"] : Array.Empty<string>(),
            .. repeated.Length > 0 ? [$"{string.Join(", ", repeated)} appear more than once"] : Array.Empty<string>(),
        ];
        return problems.Length == 0
            ? names
            : throw new InvalidDataException($"The header row of {source} cannot name the columns: {string.Join("; ", problems)}.");
    }
}

// Gives the engine the column names this adapter settled on and passes everything else through to the reader it owns.
internal sealed class NamedColumns(IDataReader inner, string[] names) : IDataReader
{
    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    public int Depth => inner.Depth;

    public bool IsClosed => inner.IsClosed;

    public int RecordsAffected => inner.RecordsAffected;

    public int FieldCount => inner.FieldCount;

    public string GetName(int i) => i >= 0 && i < names.Length
        ? names[i]
        : throw new ArgumentOutOfRangeException(nameof(i), i, "Column ordinal is out of range.");

    public int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var ordinal = Array.FindIndex(names, candidate => string.Equals(candidate, name, StringComparison.Ordinal));
        return ordinal >= 0 ? ordinal : throw new ArgumentException($"No column is named '{name}'.", nameof(name));
    }

    public bool Read() => inner.Read();

    public bool NextResult() => inner.NextResult();

    public void Close() => inner.Close();

#pragma warning disable IDISP007 // This wrapper owns the reader it wraps: TabularReaders creates both and returns only the wrapper.
    public void Dispose() => inner.Dispose();
#pragma warning restore IDISP007

    public DataTable? GetSchemaTable() => inner.GetSchemaTable();

    public bool GetBoolean(int i) => inner.GetBoolean(i);

    public byte GetByte(int i) => inner.GetByte(i);

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => inner.GetBytes(i, fieldOffset, buffer, bufferoffset, length);

    public char GetChar(int i) => inner.GetChar(i);

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => inner.GetChars(i, fieldoffset, buffer, bufferoffset, length);

    public IDataReader GetData(int i) => inner.GetData(i);

    public string GetDataTypeName(int i) => inner.GetDataTypeName(i);

    public DateTime GetDateTime(int i) => inner.GetDateTime(i);

    public decimal GetDecimal(int i) => inner.GetDecimal(i);

    public double GetDouble(int i) => inner.GetDouble(i);

    public Type GetFieldType(int i) => inner.GetFieldType(i);

    public float GetFloat(int i) => inner.GetFloat(i);

    public Guid GetGuid(int i) => inner.GetGuid(i);

    public short GetInt16(int i) => inner.GetInt16(i);

    public int GetInt32(int i) => inner.GetInt32(i);

    public long GetInt64(int i) => inner.GetInt64(i);

    public string GetString(int i) => inner.GetString(i);

    // The engine never interprets a double (#158). A workbook number reaches it as the decimal the workbook stores,
    // and Sylvan's narrowing of whole numbers to Int32 is widened to that decimal too. A number no decimal holds
    // exactly stays a double, and decimal.parse reports it.
    public object GetValue(int i) => inner.GetValue(i) switch
    {
        int number => (decimal)number,
        double number when WorkbookNumbers.Exact(number) is { } exact => exact,
        var value => value,
    };

    public int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var count = Math.Min(values.Length, FieldCount);
        for (var ordinal = 0; ordinal < count; ordinal++)
            values[ordinal] = GetValue(ordinal);
        return count;
    }

    public bool IsDBNull(int i) => inner.IsDBNull(i);
}
