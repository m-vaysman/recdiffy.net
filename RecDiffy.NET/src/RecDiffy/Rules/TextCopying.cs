namespace RecDiffy.Rules;

internal static class TextCopying
{
    public static RowContext Apply(RowContext row, string from, string to, string ruleId)
    {
        // Trim runs first. A whitespace-only value reaches FromText as empty and becomes a null cell.
        var raw = row.Raw.TryGetValue(from, out var value) ? value?.ToString()?.Trim() : null;
        return row.Write(to, CanonicalCell.FromText(raw), ruleId, raw, raw);
    }
}
