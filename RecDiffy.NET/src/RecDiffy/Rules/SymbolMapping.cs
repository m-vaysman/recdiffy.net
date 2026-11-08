namespace RecDiffy.Rules;

internal static class SymbolMapping
{
    // Blank text is a null symbol. An unknown value with passthrough off is an invalid null.
    public static RowContext Apply(RowContext row, string from, string to, IReadOnlyDictionary<string, string> map, bool passthrough, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value?.ToString()?.Trim() : null;
        return raw switch
        {
            null or "" => row.Write(to, CanonicalCell.Null(FieldType.Symbol), ruleId, raw, null),
            var text when map.TryGetValue(text, out var mapped) => Intern(row, to, mapped, ruleId, text),
            var text when passthrough => Intern(row, to, text, ruleId, text),
            var text => row.Write(to, CanonicalCell.Null(FieldType.Symbol), ruleId, text, null)
                .Diagnose("UnknownSymbol", DiagnosticSeverity.Invalid, ruleId, to, text),
        };
    }

    private static RowContext Intern(RowContext row, string field, string symbol, string ruleId, string raw)
    {
        var (pool, id) = row.Pool.InternSymbol(symbol);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), ruleId, raw, symbol);
    }
}
