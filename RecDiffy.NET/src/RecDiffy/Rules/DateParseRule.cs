using System.Globalization;

namespace RecDiffy.Rules;

internal sealed class DateParseFactory : Engine.IRuleFactory
{
    public string RuleId => "date.parse";
    public RulePhase Phase => RulePhase.Parse;
    public INormalizationRule Bind(RuleArguments args) => new DateParseRule(args.Required("from"), args.Required("to"), args.Optional("format"), args.Optional("formatBy"));
}

internal sealed class DateParseRule : INormalizationRule
{
    private readonly string _from;
    private readonly string _to;
    private readonly string? _format;
    private readonly string? _formatBy;
    public DateParseRule(string from, string to, string? format, string? formatBy)
    {
        _from = from;
        _to = to;
        _format = format;
        _formatBy = formatBy;
    }

    public string RuleId => "date.parse";
    public RulePhase Phase => RulePhase.Parse;
    public IReadOnlyList<FieldId> Reads => _formatBy is null ? [] : [new FieldId(_formatBy)];
    public IReadOnlyList<FieldId> Writes => [new FieldId(_to)];

    public void Apply(ref RowContext row)
    {
        if (!row.TryRaw(_from, out var raw) || raw.IsNull)
        {
            row.SetNull(_to);
            return;
        }
        if (raw.Date is DateOnly date)
        {
            row.SetDate(_to, date, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return;
        }
        if (raw.Number is decimal serial && _format is null && _formatBy is null)
        {
            var converted = DateOnly.FromDateTime(DateTime.FromOADate((double)serial));
            row.SetDate(_to, converted, converted.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return;
        }
        var text = raw.Text ?? "";
        var format = _format;
        if (_formatBy is not null && row.TryCanonical(_formatBy, out var party))
        {
            var code = row.State.Pool.Id(party.Interned).Value;
            if (!row.Reference.DateFormatByCode.TryGetValue(code, out format))
            {
                row.Diagnose("DateParse", DiagnosticSeverity.Invalid, _to, text);
                return;
            }
        }
        if (string.IsNullOrEmpty(format) || !DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            row.Diagnose("DateParse", DiagnosticSeverity.Invalid, _to, text);
            return;
        }
        row.SetDate(_to, parsed, text);
    }
}
