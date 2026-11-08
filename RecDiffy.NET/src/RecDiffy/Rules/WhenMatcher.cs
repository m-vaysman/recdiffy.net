namespace RecDiffy.Rules;

internal static class WhenMatcher
{
    // A clause with no equals, not-equals, or in list holds for every row.
    // A missing field fails equals and in, and it passes not-equals.
    public static bool Holds(WhenClause? when, RowContext row) => when switch
    {
        null => true,
        { EqualsValue: { } expected } => Text(row, when.Field) == expected,
        { NotEquals: { } excluded } => Text(row, when.Field) != excluded,
        { In: { } values } => Text(row, when.Field) is { } actual && Contains(values, actual),
        _ => true,
    };

    // Enumerable.Contains(comparer) boxes the list enumerator. The indexer loop stays ordinal and allocation-free.
    private static bool Contains(IReadOnlyList<string> values, string actual)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], actual, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // A canonical display wins. Otherwise the raw column is trimmed, and a blank raw cell is null.
    public static string? Text(RowContext row, string field) =>
        row.Display(field) ?? (row.Raw.TryGetValue(field, out var raw) ? raw?.ToString()?.Trim() : null);
}
