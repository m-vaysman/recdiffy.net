using RecDiffy.Rules;

namespace RecDiffy;

internal static class RouteText
{
    public static string? Of(RowContext row, FieldDeclaration field) =>
        field.Type is FieldType.Identifier && row.IsNull(field.Name) is false
            ? row.Pool.Identifier(row.Values[field.Name].Interned).Value
            : MatchKeys.Text(row, field);
}
