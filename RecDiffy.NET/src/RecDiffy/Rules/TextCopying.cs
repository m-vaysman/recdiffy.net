namespace RecDiffy.Rules;

internal static class TextCopying
{
    public static RowContext Apply(RowContext row, string from, string to, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value?.ToString()?.Trim() : null;
        return row.Write(to, CanonicalCell.FromText(raw), ruleId, raw, raw);
    }
}
