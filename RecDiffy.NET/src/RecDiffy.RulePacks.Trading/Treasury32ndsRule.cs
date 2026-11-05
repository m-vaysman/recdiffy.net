using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal static class Treasury32nds
{
    public static RowContext Apply(RowContext row, string from, string to, int scale, MidpointMode midpoint, string ruleId)
    {
        var raw = WhenMatcher.Text(row, from);
        return Parse(raw) switch
        {
            null when raw is null => row.Write(to, CanonicalCell.Null(FieldType.Decimal), ruleId, null, null),
            { } price => Write(row, to, Rounding.Round(price, scale, midpoint), ruleId, raw),
            _ => row.Write(to, CanonicalCell.Null(FieldType.Decimal), ruleId, raw, null)
                .Diagnose("PriceParse", DiagnosticSeverity.Invalid, ruleId, to, raw),
        };
    }

    public static decimal? Parse(string? text)
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

    private static RowContext Write(RowContext row, string field, decimal price, string ruleId, string? raw) =>
        row.Write(field, CanonicalCell.FromDecimal(price), ruleId, raw, price.ToString(CultureInfo.InvariantCulture));
}

internal sealed class Treasury32ndsRule(string from, string to) : INormalizationRule
{
    public string RuleId => "price.from32nds";

    public RulePhase Phase => RulePhase.Parse;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) =>
        row = Treasury32nds.Apply(row, from, to, 6, MidpointMode.ToEven, RuleId);
}
