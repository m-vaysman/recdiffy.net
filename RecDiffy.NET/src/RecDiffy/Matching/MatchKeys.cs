using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class MatchKeys
{
    public static string? Compose(ActiveItem item, IReadOnlyList<string> fields, IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        var parts = fields.Select(field => Text(item.Row.Context, schema[field])).ToArray();
        return parts.Any(part => part is null) ? null : Delimited.Join('\u001f', parts.Select(part => part!));
    }

    public static string? Text(RowContext row, FieldDeclaration field) =>
        row.IsNull(field.Name)
            ? null
            : field.Type switch
            {
                FieldType.Decimal => row.Values[field.Name].Number.ToString("F" + field.Scale, CultureInfo.InvariantCulture),
                FieldType.Date => row.Values[field.Name].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FieldType.Symbol => row.Pool.Symbol(row.Values[field.Name].Interned),
                FieldType.Identifier => IdentifierText(row, field),
                FieldType.Text => row.Values[field.Name].Text,
                _ => null,
            };

    public static bool Vetoed(ActiveItem left, ActiveItem right, string? sharedField, IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        sharedField is not null
        && schema.TryGetValue(sharedField, out var field)
        && Identity(left.Row.Context, field) is ({ } leftScheme, { } leftValue)
        && Identity(right.Row.Context, field) is ({ } rightScheme, { } rightValue)
        && IsShared(leftScheme)
        && IsShared(rightScheme)
        && leftValue != rightValue;

    // Pair keys keep the identifier scheme. A broker reference must not share a bucket with a shared-scheme reference.
    private static string IdentifierText(RowContext row, FieldDeclaration field)
    {
        var identifier = row.Pool.Identifier(row.Values[field.Name].Interned);
        return identifier.Scheme + "\u001e" + identifier.Value;
    }

    private static (string? Scheme, string? Value) Identity(RowContext row, FieldDeclaration field) =>
        row.IsNull(field.Name)
            ? (null, null)
            : field.Type switch
            {
                FieldType.Identifier => (row.Pool.Identifier(row.Values[field.Name].Interned).Scheme, row.Pool.Identifier(row.Values[field.Name].Interned).Value),
                FieldType.Symbol => ("shared", row.Pool.Symbol(row.Values[field.Name].Interned)),
                _ => ("shared", Text(row, field)),
            };

    private static bool IsShared(string scheme) => scheme is not "broker";

    public readonly record struct SharedClaim(bool Wildcard, string? Value);

    // The bucket is the value alone. Vetoed compares values across every non-broker scheme, so ISIN "X" and CUSIP "X" are one component.
    public static SharedClaim Claim(RowContext row, string? sharedField, IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        if (sharedField is null || schema.TryGetValue(sharedField, out var field) is false)
            return new(true, null);
        var (scheme, value) = Identity(row, field);
        return scheme is null || value is null || IsShared(scheme) is false ? new(true, null) : new(false, value);
    }
}
