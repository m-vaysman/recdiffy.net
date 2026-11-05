using System.Globalization;

namespace RecDiffy.Rules;

internal static class DateParsing
{
    public static RowContext Apply(RowContext row, string from, string to, IReadOnlyList<string> formats, string? formatBy, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var chosen = Formats(row, formats, formatBy);
        return chosen switch
        {
            null => row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, raw?.ToString(), null)
                .Diagnose("DateParse", DiagnosticSeverity.Invalid, ruleId, to, raw?.ToString()),
            var active => Read(raw, active) switch
            {
                { Missing: true } => row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, null, null),
                { Ok: true, Value: var date } => row.Write(to, CanonicalCell.FromDate(date), ruleId, raw?.ToString(), date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                _ => row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, raw?.ToString(), null)
                    .Diagnose("DateParse", DiagnosticSeverity.Invalid, ruleId, to, raw?.ToString()),
            },
        };
    }

    // A formatBy value with no reference format invalidates the date. The formats list is not a fallback.
    private static IReadOnlyList<string>? Formats(RowContext row, IReadOnlyList<string> formats, string? formatBy) =>
        formatBy switch
        {
            null => formats,
            var field when row.Display(field) is { } party && row.Reference.DateFormatByCode.TryGetValue(party, out var format) => [format],
            _ => null,
        };

    private static Parsed Read(object? raw, IReadOnlyList<string> formats) => raw switch
    {
        null or "" => Parsed.Blank,
        DateOnly date => Parsed.Of(date),
        DateTime date => Parsed.Of(DateOnly.FromDateTime(date)),
        double serial => Parsed.Of(DateOnly.FromDateTime(DateTime.FromOADate(serial))),
        string text => ParseText(text, formats) is { } date ? Parsed.Of(date) : Parsed.Failed,
        _ => Parsed.Failed,
    };

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
