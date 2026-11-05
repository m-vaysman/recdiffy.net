namespace RecDiffy.Rules;

internal static class WhenMatcher
{
    public static bool Holds(WhenClause? when, RowContext row) => when switch
    {
        null => true,
        { EqualsValue: { } expected } => Text(row, when.Field) == expected,
        { NotEquals: { } excluded } => Text(row, when.Field) != excluded,
        { In: { } values } => Text(row, when.Field) is { } actual && values.Contains(actual),
        _ => true,
    };

    public static string? Text(RowContext row, string field) =>
        row.Display(field) ?? (row.Raw.TryGetValue(field, out var raw) ? raw?.ToString()?.Trim() : null);
}
