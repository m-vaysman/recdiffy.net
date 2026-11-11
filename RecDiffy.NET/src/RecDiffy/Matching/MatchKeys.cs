using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class MatchKeys
{
    public static string? Compose(ActiveItem item, IReadOnlyList<string> fields, IReadOnlyDictionary<string, FieldDeclaration> schema, bool sharedOnly = false)
    {
        var parts = fields.Select(field => KeyText(item.Row.Context, schema[field], sharedOnly)).ToArray();
        return parts.Any(part => part is null) ? null : Delimited.Join('\u001f', parts.Select(part => part!));
    }

    // Decimal key text is fixed-point at the field scale, so 1.5 and 1.50 at scale 2 share a key.
    public static string? KeyText(RowContext row, FieldDeclaration field, bool sharedOnly = false) =>
        row.IsNull(field.Name)
            ? null
            : field.Type switch
            {
                FieldType.Identifier => IdentifierKeyText(row, field, sharedOnly),
                _ => ValueText(row, field),
            };

    public static string? ValueText(RowContext row, FieldDeclaration field) =>
        row.IsNull(field.Name)
            ? null
            : field.Type switch
            {
                FieldType.Decimal => row.Values[field.Name].Number.ToString("F" + field.Scale, CultureInfo.InvariantCulture),
                FieldType.Date => row.Values[field.Name].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FieldType.Symbol => row.Pool.Symbol(row.Values[field.Name].Interned),
                FieldType.Identifier => row.Pool.Identifier(row.Values[field.Name].Interned).Value,
                FieldType.Text => row.Values[field.Name].Text,
                _ => null,
            };

    // A broker scheme never vetoes, and a missing value never vetoes. Two shared schemes veto when the values differ.
    public static bool Vetoed(ActiveItem left, ActiveItem right, string? sharedField, IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        sharedField is not null
        && schema.TryGetValue(sharedField, out var field)
        && Identity(left.Row.Context, field) is ({ } leftScheme, { } leftValue)
        && Identity(right.Row.Context, field) is ({ } rightScheme, { } rightValue)
        && IsShared(leftScheme)
        && IsShared(rightScheme)
        && leftValue != rightValue;

    // Pair keys keep the identifier scheme. A broker reference must not share a bucket with a shared-scheme reference.
    // Scheme and value are joined with a record separator, so a value cannot collide with the next key field.
    private static string? IdentifierKeyText(RowContext row, FieldDeclaration field, bool sharedOnly)
    {
        var identifier = row.Pool.Identifier(row.Values[field.Name].Interned);
        return sharedOnly && IsShared(identifier.Scheme) is false
            ? null
            : identifier.Scheme + "\u001e" + identifier.Value;
    }

    private static (string? Scheme, string? Value) Identity(RowContext row, FieldDeclaration field) =>
        row.IsNull(field.Name)
            ? (null, null)
            : field.Type switch
            {
                FieldType.Identifier => (row.Pool.Identifier(row.Values[field.Name].Interned).Scheme, row.Pool.Identifier(row.Values[field.Name].Interned).Value),
                FieldType.Symbol => ("shared", row.Pool.Symbol(row.Values[field.Name].Interned)),
                _ => ("shared", ValueText(row, field)),
            };

    // The comparison is ordinal. "Broker" still counts as shared.
    private static bool IsShared(string scheme) => scheme is not "broker";

    public readonly record struct SharedClaim(bool Wildcard, string? Value);

    // The bucket is the value alone. Vetoed compares values across every non-broker scheme, so ISIN "X" and CUSIP "X" are one component.
    // A broker scheme, a null value, or a missing shared field comes back as a wildcard.
    public static SharedClaim Claim(RowContext row, string? sharedField, IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        if (sharedField is null || schema.TryGetValue(sharedField, out var field) is false)
            return new(true, null);
        var (scheme, value) = Identity(row, field);
        return scheme is null || value is null || IsShared(scheme) is false ? new(true, null) : new(false, value);
    }
}
