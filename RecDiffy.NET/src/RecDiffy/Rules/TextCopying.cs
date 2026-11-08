namespace RecDiffy.Rules;

// Used only by TextCopyRule, which InvariantTextCopyRule replaces (#158).
[Obsolete("Used only by TextCopyRule. Replaced by InvariantTextCopyRule. See #158.")]
internal static class TextCopying
{
    public static RowContext Apply(RowContext row, string from, string to, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value?.ToString()?.Trim() : null;
        return row.Write(to, CanonicalCell.FromText(raw), ruleId, raw, raw);
    }
}
