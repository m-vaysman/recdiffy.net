using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

// What consolidation needs from the profile to combine members (spec v3 §5.2, §5.3): each field's declaration, the
// profile midpoint, and the fields the run uses (keys, blocks, conserve lists, diffed fields and route fields).
internal sealed record FieldUse(IReadOnlyDictionary<string, FieldDeclaration> Fields, MidpointMode Midpoint, Func<string, bool> IsUsed)
{
    // For a caller without a profile: every field is used, and decimals agree only when exactly equal.
    public static FieldUse Everything { get; } = new(ImmutableDictionary<string, FieldDeclaration>.Empty, MidpointMode.ToEven, _ => true);

    public static FieldUse Of(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compared = document.Fields.Where(field => field.Role is FieldRole.Key or FieldRole.Economic).Select(field => field.Name);
        var matched = document.Paths.SelectMany(path => path.Passes).SelectMany(pass => (IEnumerable<string>)[.. pass.Key, .. pass.Block, .. pass.Conserve]);
        var routed = document.Paths.Select(path => path.Route.Field).OfType<string>();
        var used = compared.Concat(matched).Concat(routed).ToImmutableHashSet(StringComparer.Ordinal);
        return new(document.Fields.ToImmutableDictionary(field => field.Name, StringComparer.Ordinal), document.Midpoint, used.Contains);
    }

    public int Scale(string field) => Fields.TryGetValue(field, out var declaration) ? declaration.Scale : 28;
}

internal static class MemberAgreement
{
    public const string Disagree = "MembersDisagree";

    // Members agree on a field when its identity is the same for all of them; unknown agrees only with unknown.
    public static bool Agree(IReadOnlyList<RowContext> members, string field, FieldUse use) =>
        members.Select(member => Identity(member, field, use)).Distinct(StringComparer.Ordinal).Skip(1).Any() is false;

    // Decimals by value at the field's scale with the profile midpoint, identifiers by scheme, scope and value.
    public static string? Identity(RowContext row, string field, FieldUse use) =>
        row.Values.TryGetValue(field, out var cell) && cell.IsNull is false
            ? cell.Type switch
            {
                FieldType.Decimal => "D" + (Rounding.Round(cell.Number, use.Scale(field), use.Midpoint) / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
                FieldType.Date => "T" + cell.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FieldType.Symbol => "S" + row.Pool.Symbol(cell.Interned),
                FieldType.Identifier => "I" + Delimited.Join('\u001f', [row.Pool.Identifier(cell.Interned).Scheme, row.Pool.Identifier(cell.Interned).Scope, row.Pool.Identifier(cell.Interned).Value]),
                FieldType.Text => "X" + cell.Text,
                _ => null,
            }
            : null;

    // Copies one member's value as it is. A symbol or identifier is interned again in the target's pool with its own
    // scheme and scope, so the copy keeps its identity.
    public static RowContext Copy(RowContext target, string field, RowContext source, string ruleId) =>
        source.Values.TryGetValue(field, out var cell) && cell.IsNull is false
            ? cell.Type switch
            {
                FieldType.Symbol => InternSymbol(target, field, source.Pool.Symbol(cell.Interned), ruleId),
                FieldType.Identifier => InternIdentifier(target, field, source.Pool.Identifier(cell.Interned), ruleId),
                _ => target.Write(field, cell, ruleId, null, source.Display(field)),
            }
            : Unknown(target, field, source, ruleId);

    public static RowContext Unknown(RowContext target, string field, RowContext typedBy, string ruleId) =>
        target.Write(field, CanonicalCell.Null(typedBy.Values.TryGetValue(field, out var cell) ? cell.Type : FieldType.Text), ruleId, null, null);

    private static RowContext InternSymbol(RowContext target, string field, string text, string ruleId)
    {
        var (pool, id) = target.Pool.InternSymbol(text);
        return target.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), ruleId, null, text);
    }

    private static RowContext InternIdentifier(RowContext target, string field, IdentifierKey key, string ruleId)
    {
        var (pool, id) = target.Pool.InternIdentifier(key.Scheme, key.Scope, key.Value);
        return target.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Identifier, id), ruleId, null, key.Value);
    }
}
