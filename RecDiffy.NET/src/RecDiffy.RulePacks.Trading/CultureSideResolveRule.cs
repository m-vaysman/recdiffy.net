using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal static class CultureSideResolution
{
    public static RowContext Apply(RowContext row, string codeColumn, string quantityColumn, string? invertWhen, string perspectiveColumn, string assetField, NumberFormatInfo format, string ruleId)
    {
        var code = WhenMatcher.Text(row, codeColumn);
        var quantity = Quantity(row, quantityColumn, format);
        var asset = row.Display(assetField);
        var invert = invertWhen is not null && WhenMatcher.Text(row, perspectiveColumn) == invertWhen;
        return Decide(Map(code), quantity, asset) switch
        {
            { Ok: false, Code: { } diagnostic } => row.Diagnose(diagnostic, DiagnosticSeverity.Invalid, ruleId, "Side", code),
            { Side: { } side } => Write(row, invert ? Invert(side) : side, ruleId, code),
            _ => row.Diagnose("UndeterminedSide", DiagnosticSeverity.Invalid, ruleId, "Side", code),
        };
    }

    private static Decision Decide(string? side, decimal quantity, string? asset) => (side, quantity, asset) switch
    {
        (null, 0, _) => Decision.Invalid("UndeterminedSide"),
        (null, < 0, "Swap") => Decision.Valid("PAY_FIXED"),
        (null, < 0, _) => Decision.Valid("SELL"),
        (null, > 0, "Swap") => Decision.Valid("REC_FIXED"),
        (null, _, _) => Decision.Valid("BUY"),
        ("BUY" or "REC_FIXED", < 0, _) => Decision.Invalid("SideSignConflict"),
        ("SELL" or "PAY_FIXED" or "BUY" or "REC_FIXED", _, _) => Decision.Valid(side!),
        _ => Decision.Invalid("UndeterminedSide"),
    };

    private static string? Map(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "B" or "BOT" or "BUY" => "BUY",
        "S" or "SLD" or "SELL" => "SELL",
        "P" or "PAY" or "PAYFIXED" or "PAY_FIXED" => "PAY_FIXED",
        "R" or "REC" or "RECEIVEFIXED" or "REC_FIXED" => "REC_FIXED",
        _ => "",
    };

    private static string Invert(string side) => side switch
    {
        "BUY" => "SELL",
        "SELL" => "BUY",
        "PAY_FIXED" => "REC_FIXED",
        "REC_FIXED" => "PAY_FIXED",
        var other => other,
    };

    // The raw quantity is read as decimal.parse reads a number, in the side's culture, and floating point is not read.
    // Then an earlier canonical value. An unreadable quantity is zero, so a blank quantity and no code is UndeterminedSide.
    private static decimal Quantity(RowContext row, string column, NumberFormatInfo format) =>
        (Raw(row, column, format), row.Values.TryGetValue(column, out var cell) && !cell.IsNull ? cell.Number : (decimal?)null) switch
        {
            ({ } raw, _) => raw,
            (null, { } canonical) => canonical,
            _ => 0m,
        };

    private static decimal? Raw(RowContext row, string column, NumberFormatInfo format) =>
        row.Raw.TryGetValue(column, out var raw) && raw is not (double or float or Half)
            ? raw.ReadNumber(format).Convert<decimal?>(value => value, () => null, () => null)
            : null;

    private static RowContext Write(RowContext row, string side, string ruleId, string? raw)
    {
        var (pool, id) = row.Pool.InternSymbol(side);
        return row.WithPool(pool).Write("Side", CanonicalCell.FromInterned(FieldType.Symbol, id), ruleId, raw, side);
    }

    private readonly record struct Decision(bool Ok, string? Side, string? Code)
    {
        public static Decision Valid(string side) => new(true, side, null);

        public static Decision Invalid(string code) => new(false, null, code);
    }
}

internal sealed class CultureSideResolveRule(string code, string quantity, string? invertWhen, string perspective, string asset, NumberFormatInfo format) : INormalizationRule
{
    public string RuleId => "side.resolve";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads => [new(asset)];

    public IReadOnlyList<FieldId> Writes => [new("Side")];

    public void Apply(ref RowContext row) =>
        row = CultureSideResolution.Apply(row, code, quantity, invertWhen, perspective, asset, format, RuleId);
}
