using System.Text;

namespace RecDiffy.IO;

/// <summary>How to read a delimited text file. <see cref="HasHeader"/> and <see cref="Delimiter"/> must be stated; nothing is detected unless asked for.</summary>
public sealed record CsvOptions
{
    /// <summary>True: the first row holds column names and every later row is data. False: columns are named <c>Column1</c> to <c>ColumnN</c> and every row is data.</summary>
    public required bool HasHeader { get; init; }

    public required CsvDelimiter Delimiter { get; init; }

    public char Quote { get; init; } = '"';

    /// <summary>The character that escapes a quote inside a quoted field. The default doubles the quote, as RFC 4180 does.</summary>
    public char Escape { get; init; } = '"';

    /// <summary>
    /// The file's text encoding; UTF-8 unless stated. A byte-order mark for this encoding is skipped; no other encoding is
    /// detected. Bytes that are invalid in this encoding fail the read rather than being replaced.
    /// </summary>
    public Encoding Encoding { get; init; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);
}

/// <summary>A field delimiter: one character, or automatic detection when the caller chooses it.</summary>
public sealed record CsvDelimiter
{
    private CsvDelimiter(char? character) => Character = character;

    public static CsvDelimiter Comma { get; } = new(',');

    public static CsvDelimiter Tab { get; } = new('\t');

    public static CsvDelimiter Pipe { get; } = new('|');

    public static CsvDelimiter Semicolon { get; } = new(';');

    /// <summary>Lets the CSV reader detect the delimiter from the header row. Only used when chosen; it is never a default.</summary>
    public static CsvDelimiter DetectAutomatically { get; } = new((char?)null);

    /// <summary>The character, or null when the delimiter is detected.</summary>
    public char? Character { get; }

    public static CsvDelimiter Of(char character) => character switch
    {
        '\r' or '\n' => throw new ArgumentException("A line break cannot be a field delimiter.", nameof(character)),
        _ => new(character),
    };
}

/// <summary>How to read one sheet of a workbook. <see cref="HasHeader"/> and <see cref="Sheet"/> must be stated. Data starts at cell A1.</summary>
public sealed record ExcelOptions
{
    /// <summary>True: row 1 holds column names and every later row is data. False: columns are named <c>Column1</c> to <c>ColumnN</c> and every row, row 1 included, is data.</summary>
    public required bool HasHeader { get; init; }

    public required ExcelSheet Sheet { get; init; }
}

/// <summary>Which sheet to read: by its tab name, or by its tab number counting from 1.</summary>
public sealed record ExcelSheet
{
    private ExcelSheet(string? name, int number) => (Name, Number) = (name, number);

    /// <summary>The tab name, or null when the sheet is chosen by number.</summary>
    public string? Name { get; }

    /// <summary>The 1-based tab number, or 0 when the sheet is chosen by name.</summary>
    public int Number { get; }

    public static ExcelSheet Named(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new(name, 0);
    }

    /// <summary>Chooses a sheet by its tab number.</summary>
    /// <param name="number">
    /// The tab number, 1-based: 1 is the first tab, 2 the second. This is intentional: it matches how users count the tabs
    /// they see in Excel. It is not an off-by-one error; do not change it to 0-based. Hidden tabs count, in workbook order.
    /// </param>
    public static ExcelSheet Numbered(int number) => new(null, number);
}

/// <summary>The workbook format of an Excel stream. A file path's extension gives its format.</summary>
public enum ExcelFormat
{
    Xlsx,
    Xlsb,
    Xls,
}
