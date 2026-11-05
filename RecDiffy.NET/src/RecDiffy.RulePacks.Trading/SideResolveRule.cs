using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

internal static class SideResolution
{
    public static RowContext Apply(RowContext row, string codeColumn, string quantityColumn, string? invertWhen, string perspectiveColumn, string assetField, string ruleId)
    {
        var code = WhenMatcher.Text(row, codeColumn);
        var quantity = Quantity(row, quantityColumn);
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
        "P" or "PAY" or "PAY_FIXED" => "PAY_FIXED",
        "R" or "REC" or "REC_FIXED" => "REC_FIXED",
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

    private static decimal Quantity(RowContext row, string column) =>
        row.Raw.TryGetValue(column, out var raw) && raw is decimal number ? number
        : decimal.TryParse(raw?.ToString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed
        : row.Values.TryGetValue(column, out var cell) && !cell.IsNull ? cell.Number
        : 0m;

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

internal sealed class SideResolveRule(string code, string quantity, string? invertWhen, string perspective, string asset) : INormalizationRule
{
    public string RuleId => "side.resolve";

    public RulePhase Phase => RulePhase.Derive;

    public IReadOnlyList<FieldId> Reads => [new(asset)];

    public IReadOnlyList<FieldId> Writes => [new("Side")];

    public void Apply(ref RowContext row) =>
        row = SideResolution.Apply(row, code, quantity, invertWhen, perspective, asset, RuleId);
}
