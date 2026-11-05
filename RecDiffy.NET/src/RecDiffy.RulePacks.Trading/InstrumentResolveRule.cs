using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class InstrumentResolveFactory : IRuleFactory
{
    public string RuleId => "instrument.resolve";
    public RulePhase Phase => RulePhase.Identify;
    public INormalizationRule Bind(RuleArguments args) => new InstrumentResolveRule(
        args.Required("to"),
        args.Optional("idColumn"),
        args.Optional("schemeColumn"),
        args.List("canonical"));
}

internal sealed class InstrumentResolveRule : INormalizationRule
{
    private readonly string _to;
    private readonly string? _idColumn;
    private readonly string? _schemeColumn;
    private readonly IReadOnlyList<string> _canonical;
    public InstrumentResolveRule(string to, string? idColumn, string? schemeColumn, IReadOnlyList<string> canonical)
    {
        _to = to;
        _idColumn = idColumn;
        _schemeColumn = schemeColumn;
        _canonical = canonical;
    }

    public string RuleId => "instrument.resolve";
    public RulePhase Phase => RulePhase.Identify;
    public IReadOnlyList<FieldId> Reads { get; } = [];
    public IReadOnlyList<FieldId> Writes => [new(_to)];

    public void Apply(ref RowContext row)
    {
        if (_idColumn is null)
        {
            string? chosen = null;
            foreach (var column in _canonical)
            {
                if (row.TryRaw(column, out var raw) && !raw.IsNull && !string.IsNullOrWhiteSpace(raw.Text))
                    chosen ??= raw.Text.Trim();
            }

            if (chosen is null)
            {
                row.SetNull(_to);
                return;
            }

            foreach (var column in _canonical)
            {
                if (!row.TryRaw(column, out var raw) || raw.IsNull || string.IsNullOrWhiteSpace(raw.Text))
                    continue;
                row.State.Instruments.Add(Scheme(column), raw.Text.Trim(), chosen);
            }

            row.SetIdentifier(_to, "instrument", "", chosen, chosen);
            return;
        }

        if (!row.TryRaw(_idColumn, out var id) || id.IsNull || string.IsNullOrWhiteSpace(id.Text))
        {
            row.SetNull(_to);
            return;
        }
        var scheme = "TICKER";
        if (_schemeColumn is not null && row.TryRaw(_schemeColumn, out var schemeRaw) && schemeRaw.Text is not null)
            scheme = schemeRaw.Text.Trim();
        var token = id.Text.Trim();
        if (row.State.Instruments.Conflicts.Contains((scheme, token)))
        {
            row.Diagnose("UnresolvedInstrument", DiagnosticSeverity.Warning, _to, token);
            row.SetNull(_to);
            return;
        }
        if (!row.TryLookupInstrument(scheme, token, out var resolved))
        {
            row.Diagnose("UnresolvedInstrument", DiagnosticSeverity.Warning, _to, token);
            row.SetNull(_to);
            return;
        }
        row.SetIdentifier(_to, "instrument", "", resolved, token);
    }

    private static string Scheme(string column) => column.ToUpperInvariant() switch
    {
        "TICKER" => "TICKER",
        _ => column.ToUpperInvariant(),
    };
}
