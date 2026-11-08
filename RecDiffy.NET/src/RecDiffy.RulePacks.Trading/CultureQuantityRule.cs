using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

// Reads the raw quantity exactly as decimal.parse reads a number: the side's culture, and no float or double.
// Replaced by ScaledQuantityRule (#170). It writes the absolute quantity unrounded, so the match key and the diff see different numbers.
[Obsolete("Replaced by ScaledQuantityRule: writes the absolute quantity unrounded, so a pair the diff calls Exact never matches. See #170.")]
internal sealed class CultureQuantityRule(string from, string to, NumberFormatInfo format) : INormalizationRule
{
    public string RuleId => "quantity.absolute";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads { get; } = [];

    public IReadOnlyList<FieldId> Writes => [new(to)];

    public void Apply(ref RowContext row) => row = Absolute(row);

    private RowContext Absolute(RowContext row)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = InvariantText.Of(raw);
        var earlier = row.Values.TryGetValue(from, out var cell) && cell is { IsNull: false, Type: FieldType.Decimal } ? cell.Number : (decimal?)null;
        return raw switch
        {
            double or float or Half => Unread(row, earlier, "FloatValue", rawText),
            _ => raw.ReadNumber(format).Convert(
                value: quantity => WriteAbsolute(row, quantity, rawText),
                missing: () => earlier is { } quantity ? WriteAbsolute(row, quantity, rawText) : WriteNull(row, rawText),
                failed: () => Unread(row, earlier, "DecimalParse", rawText)),
        };
    }

    private RowContext Unread(RowContext row, decimal? earlier, string code, string? rawText) =>
        earlier is { } quantity
            ? WriteAbsolute(row, quantity, rawText)
            : WriteNull(row, rawText).Diagnose(code, DiagnosticSeverity.Invalid, RuleId, to, rawText);

    private RowContext WriteAbsolute(RowContext row, decimal quantity, string? rawText)
    {
        var absolute = decimal.Abs(quantity);
        return row.Write(to, CanonicalCell.FromDecimal(absolute), RuleId, rawText, absolute.ToString(CultureInfo.InvariantCulture));
    }

    private RowContext WriteNull(RowContext row, string? rawText) =>
        row.Write(to, CanonicalCell.Null(FieldType.Decimal), RuleId, rawText, null);
}
