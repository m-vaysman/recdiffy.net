namespace RecDiffy.Rules;

internal static class IdentifierMapping
{
    public static RowContext Apply(RowContext row, string from, string to, string scheme, string scope, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value?.ToString()?.Trim() : null;
        return string.IsNullOrEmpty(raw)
            ? row.Write(to, CanonicalCell.Null(FieldType.Identifier), ruleId, raw, null)
            : Intern(row, to, scheme, scope, raw, ruleId);
    }

    private static RowContext Intern(RowContext row, string field, string scheme, string scope, string value, string ruleId)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, scope, value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Identifier, id), ruleId, value, value);
    }
}
