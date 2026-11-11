using System.Globalization;

namespace RecDiffy.Rules;

internal sealed class PartyFormatDateRule(string from, string to, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture) : INormalizationRule
{
    private const string StandardFormats = "dDfFgGmMoOrRsStTuUyY";
    private readonly IReadOnlyList<string> _formats = RequireYear(formats, culture);

    public string RuleId => "date.parse";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = formatBy is null ? [] : [new FieldId(formatBy)];

    public IReadOnlyList<FieldId> Writes => [new FieldId(to)];

    public void Apply(ref RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = InvariantText.Of(raw);
        row = raw switch
        {
            null or "" => row.Write(to, CanonicalCell.Null(FieldType.Date), RuleId, null, null),
            string text when string.IsNullOrWhiteSpace(text) => row.Write(to, CanonicalCell.Null(FieldType.Date), RuleId, null, null),
            DateOnly date => WriteDate(row, date, rawText),
            DateTime date => WriteDate(row, DateOnly.FromDateTime(date), rawText),
            double serial when FromOADate(serial) is { } date => WriteDate(row, date, rawText),
            string text when Formats(row) is { } active && ParseText(text, active, culture) is { } date => WriteDate(row, date, rawText),
            _ => Reject(row, rawText),
        };
    }

    private static IReadOnlyList<string> RequireYear(IReadOnlyList<string> formats, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(culture);
        ProfileError[] errors = [.. formats.Where(format => WritesYear(format, culture.DateTimeFormat) is false).Select(format =>
            new ProfileError("InvalidParameter", $"date.parse format '{format}' has no year, so the year would come from the clock."))];
        return errors.Length == 0 ? [.. formats] : throw new ProfileCompileException(errors);
    }

    private static bool WritesYear(string format, DateTimeFormatInfo info) => format switch
    {
        [var letter] when StandardFormats.Contains(letter, StringComparison.Ordinal) => info.GetAllDateTimePatterns(letter).All(CustomWritesYear),
        [_] => false,
        _ => CustomWritesYear(format),
    };

    private static bool CustomWritesYear(string pattern) =>
        pattern.Aggregate(new PatternScan(null, false, false), (scan, character) => scan switch
        {
            { Year: true } => scan,
            { Escaped: true } => scan with { Escaped = false },
            _ when character == '\\' => scan with { Escaped = true },
            { Quote: { } quote } => character == quote ? scan with { Quote = null } : scan,
            _ => character switch
            {
                '\'' or '"' => scan with { Quote = character },
                'y' => scan with { Year = true },
                _ => scan,
            },
        }).Year;

    private static DateOnly? FromOADate(double serial) =>
        double.IsFinite(serial) && serial is > -657435.0d and < 2958466.0d
            ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
            : null;

    private static DateOnly? ParseText(string text, IReadOnlyList<string> formats, CultureInfo culture) =>
        formats
            .Select(format => DateOnly.TryParseExact(text, format, culture, DateTimeStyles.None, out var exact) ? exact : (DateOnly?)null)
            .FirstOrDefault(date => date is not null);

    private IReadOnlyList<string>? Formats(RowContext row) =>
        formatBy switch
        {
            null => _formats,
            var field when row.Display(field) is { } party && row.Reference.DateFormatByCode.TryGetValue(party, out var format) && WritesYear(format, culture.DateTimeFormat) => [format],
            _ => null,
        };

    private RowContext WriteDate(RowContext row, DateOnly date, string? rawText) =>
        row.Write(to, CanonicalCell.FromDate(date), RuleId, rawText, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private RowContext Reject(RowContext row, string? rawText) =>
        row.Write(to, CanonicalCell.Null(FieldType.Date), RuleId, rawText, null)
            .Diagnose("DateParse", DiagnosticSeverity.Invalid, RuleId, to, rawText);

    private sealed record PatternScan(char? Quote, bool Escaped, bool Year);
}
