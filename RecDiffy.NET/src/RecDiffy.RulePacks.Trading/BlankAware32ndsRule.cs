using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal sealed class BlankAware32ndsRule(string from, string to, int scale, MidpointMode midpoint) : INormalizationRule
{
    public string RuleId => "price.from32nds";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row)
    {
        var raw = ReadText(row, from);
        row = Parse(raw) switch
        {
            null when raw is null => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, null, null),
            { } price => Write(row, Rounding.Round(price, scale, midpoint), raw),
            _ => row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, raw, null)
                .Diagnose("PriceParse", DiagnosticSeverity.Invalid, RuleId, to, raw),
        };
    }

    private static string? ReadText(RowContext row, string field) =>
        row.Display(field) ?? (row.Raw.TryGetValue(field, out var raw) && InvariantText.Of(raw)?.Trim() is { Length: > 0 } text ? text : null);

    private static decimal? Parse(string? text)
    {
        if (text is not { Length: > 2 })
            return null;
        var plus = text[^1] is '+';
        var body = plus ? text[..^1] : text;
        var dash = body.IndexOf('-');
        return dash is > 0 && body[(dash + 1)..] is { Length: 2 } ticksText
            && int.TryParse(body[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var whole)
            && int.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks is >= 0 and <= 31
                ? whole + ticks / 32m + (plus ? 1m / 64m : 0m)
                : null;
    }

    private RowContext Write(RowContext row, decimal price, string? raw) =>
        row.Write(to, CanonicalCell.FromDecimal(price), RuleId, raw, price.ToString(CultureInfo.InvariantCulture));
}
