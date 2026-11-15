using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class MatchKeys
{
    // "F0" to "F28", one for each scale a profile can declare, so a decimal key does not build its format text (#403).
    private static readonly string[] FixedPoint = [.. Enumerable.Range(0, 29).Select(scale => "F" + scale.ToInvariant())];

    public static MatchKey? Compose(ActiveItem item, IReadOnlyList<string> fields, IReadOnlyDictionary<string, FieldDeclaration> schema, bool sharedOnly = false)
    {
        var parts = fields.Select(field => KeyText(item.Row.Context, schema[field], sharedOnly)).ToArray();
        return parts.Any(part => part is null) ? null : MatchKey.FromEncoded(Delimited.Join('\u001f', parts.Select(part => part!)));
    }

    // Decimal key text is fixed-point at the field scale, so 1.5 and 1.50 at scale 2 share a key.
    private static string? KeyText(RowContext row, FieldDeclaration field, bool sharedOnly = false) =>
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
                FieldType.Decimal => row.Values[field.Name].Number.ToString(Format(field.Scale), CultureInfo.InvariantCulture),
                FieldType.Date => row.Values[field.Name].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FieldType.Symbol => row.Pool.Symbol(row.Values[field.Name].Interned),
                FieldType.Identifier => row.Pool.Identifier(row.Values[field.Name].Interned).Value,
                FieldType.Text => row.Values[field.Name].Text,
                _ => null,
            };

    // A profile never compiles with a scale outside 0..28. A declaration built by hand still gets the text it got before.
    private static string Format(int scale) => scale is >= 0 and <= 28 ? FixedPoint[scale] : "F" + scale;

    // The form a person reads (#338, R2): each key field by its value, joined with a middle dot, and no scheme. The join is
    // display only, so a value that holds the separator itself is shown as written. A missing field has no value form.
    public static string? ValueForm(RowContext row, IReadOnlyList<string> fields, IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        var parts = fields.Select(field => schema.TryGetValue(field, out var declaration) ? ValueText(row, declaration) : null).ToArray();
        return parts.Any(part => part is null) ? null : string.Join(ValueSeparator, parts);
    }

    // A committed group keys on its target row's RowKey, which already reads as a value. Every other commit keys on the
    // pass's key fields, whose key form carries a scheme, so the trail shows their values instead.
    public static string? CommittedTrailKey(PassDeclaration? pass, string key, RowContext row, IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        pass switch
        {
            { Pass: "match.groupSum" } => key,
            { Pass: "match.sharedRef", Key.Count: 0 } => ValueForm(row, SharedRefKey, schema),
            { Key.Count: > 0 } pair => ValueForm(row, pair.Key, schema),
            _ => null,
        };

    // A contested pair keys on its key fields. A contested group has no key of its own, so it keys on its block fields.
    // Any other verdict carries no key. An unknown pass yields null rather than the encoded key.
    public static string? OpenTrailKey(PassDeclaration? pass, string? key, RowContext? row, IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        (pass, key, row) switch
        {
            (_, null, _) or (null, _, _) or (_, _, null) => null,
            ({ Pass: "match.groupSum" } group, _, { } context) => ValueForm(context, group.Block, schema),
            ({ Pass: "match.sharedRef", Key.Count: 0 }, _, { } context) => ValueForm(context, SharedRefKey, schema),
            ({ Key.Count: > 0 } pair, _, { } context) => ValueForm(context, pair.Key, schema),
            _ => null,
        };

    private const string ValueSeparator = " · ";

    private static readonly string[] SharedRefKey = ["Counterparty", "TradeRef"];

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

    // The fields one pass keys on, and whether an identifier must be in a shared scheme to have a key.
    public sealed record Spec(IReadOnlyList<string> Fields, bool SharedOnly);

    // Every item's key for the key fields of each pass, composed once for a run (#396). A field's key text is read and
    // formatted once per item, however many passes name the field. The table is built from the items and the schema
    // alone, and nothing changes it afterwards.
    public sealed class Table
    {
        private readonly IReadOnlyList<ActiveItem>? _items;
        private readonly IReadOnlyDictionary<string, FieldDeclaration>? _schema;
        private readonly (Spec Spec, string?[] Keys)[] _keys;

        private Table(IReadOnlyList<ActiveItem>? items, IReadOnlyDictionary<string, FieldDeclaration>? schema, (Spec Spec, string?[] Keys)[] keys)
        {
            _items = items;
            _schema = schema;
            _keys = keys;
        }

        public static Table Empty { get; } = new(null, null, []);

        // The columns are working state of this method and do not leave it. With no item there is no key to compose,
        // and the schema is not asked for a field, as it is not when Compose is never called.
        public static Table Build(IReadOnlyList<ActiveItem> items, IReadOnlyDictionary<string, FieldDeclaration> schema, IEnumerable<Spec> specs)
        {
            if (items.Count is 0)
                return Empty;
            var columns = new Dictionary<(string Field, bool SharedOnly), string?[]>();
            var keys = new List<(Spec Spec, string?[] Keys)>();
            foreach (var spec in specs)
            {
                if (keys.Exists(known => Same(known.Spec, spec.Fields, spec.SharedOnly)) is false)
                    keys.Add((spec, Composed(items.Count, [.. spec.Fields.Select(field => Column(items, schema[field], spec.SharedOnly, columns))])));
            }

            return new(items, schema, [.. keys]);
        }

        // The key of one item under these fields. A workspace with other items or another schema, or fields the table
        // was not built for, gets its keys composed afresh: the same text, without the reuse.
        public Func<int, MatchKey?> Reader(MatchWorkspace workspace, IReadOnlyList<string> fields, bool sharedOnly)
        {
            var known = ReferenceEquals(_items, workspace.Items) && ReferenceEquals(_schema, workspace.Schema)
                ? Array.Find(_keys, entry => Same(entry.Spec, fields, sharedOnly)).Keys
                : null;
            return known is null
                ? index => Compose(workspace.Items[index], fields, workspace.Schema, sharedOnly)
                : index => known[index] is { } encoded ? MatchKey.FromEncoded(encoded) : null;
        }

        private static bool Same(Spec spec, IReadOnlyList<string> fields, bool sharedOnly) =>
            spec.SharedOnly == sharedOnly && spec.Fields.SequenceEqual(fields, StringComparer.Ordinal);

        // One field's key text for every item. Only an identifier reads the shared-only flag, so every other field has
        // one column whatever the pass.
        private static string?[] Column(
            IReadOnlyList<ActiveItem> items,
            FieldDeclaration field,
            bool sharedOnly,
            Dictionary<(string Field, bool SharedOnly), string?[]> columns)
        {
            var slot = (field.Name, sharedOnly && field.Type is FieldType.Identifier);
            if (columns.TryGetValue(slot, out var known))
                return known;
            var column = new string?[items.Count];
            for (var item = 0; item < column.Length; item++)
                column[item] = KeyText(items[item].Row.Context, field, sharedOnly);
            columns[slot] = column;
            return column;
        }

        // The same join as Compose, over the same parts. A null part leaves the item without a key, as in Compose. The
        // part buffer is reused for every item: the join has read it by the time the next item fills it.
        private static string?[] Composed(int count, string?[][] columns)
        {
            var keys = new string?[count];
            var parts = new string[columns.Length];
            for (var item = 0; item < count; item++)
                keys[item] = Filled(parts, columns, item) ? Delimited.Join('\u001f', parts) : null;
            return keys;
        }

        private static bool Filled(string[] parts, string?[][] columns, int item)
        {
            for (var field = 0; field < columns.Length; field++)
            {
                if (columns[field][item] is not { } text)
                    return false;
                parts[field] = text;
            }

            return true;
        }
    }
}
