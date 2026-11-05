namespace RecDiffy.Rules;

internal static class RequiredFields
{
    public static RowContext Apply(RowContext row, IReadOnlyList<string> fields, string ruleId) =>
        fields.Where(row.IsNull).Aggregate(
            row,
            (current, field) => current.Diagnose("RequiredMissing", DiagnosticSeverity.Invalid, ruleId, field, null));
}
