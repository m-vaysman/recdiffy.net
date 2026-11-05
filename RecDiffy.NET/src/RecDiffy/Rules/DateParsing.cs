using System.Globalization;

namespace RecDiffy.Rules;

internal static class DateParsing
{
    public static RowContext Apply(RowContext row, string from, string to, IReadOnlyList<string> formats, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        // Missing comes first. Blank has Ok set too, so the success arm would write 0001-01-01 for an empty cell.
        return Read(raw, formats) switch
        {
            { Missing: true } => row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, null, null),
            { Ok: true, Value: var date } => row.Write(to, CanonicalCell.FromDate(date), ruleId, raw?.ToString(), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            _ => row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, raw?.ToString(), null)
                .Diagnose("DateParse", DiagnosticSeverity.Invalid, ruleId, to, raw?.ToString()),
        };
    }

    // Null or empty is blank. A whitespace-only string fails the parse.
    private static Parsed Read(object? raw, IReadOnlyList<string> formats) => raw switch
    {
        null or "" => Parsed.Blank,
        DateOnly date => Parsed.Of(date),
        DateTime date => Parsed.Of(DateOnly.FromDateTime(date)),
        double serial => Parsed.Of(DateOnly.FromDateTime(DateTime.FromOADate(serial))),
        string text => ParseText(text, formats) is { } date ? Parsed.Of(date) : Parsed.Failed,
        _ => Parsed.Failed,
    };

    // No formats means a loose invariant parse, and that parse is month-first. "13/01/2024" fails.
    // Listed formats use an exact match, and the first hit wins.
    private static DateOnly? ParseText(string text, IReadOnlyList<string> formats) =>
        formats.Count == 0
            ? DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null
            : formats
                .Select(format => DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact) ? exact : (DateOnly?)null)
                .FirstOrDefault(date => date is not null);

    private readonly record struct Parsed(bool Ok, bool Missing, DateOnly Value)
    {
        public static Parsed Blank { get; } = new(true, true, default);

        public static Parsed Failed { get; } = new(false, false, default);

        public static Parsed Of(DateOnly value) => new(true, false, value);
    }
}
