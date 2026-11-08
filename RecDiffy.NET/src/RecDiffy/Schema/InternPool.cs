using System.Collections.Immutable;

namespace RecDiffy;

internal readonly record struct IdentifierKey(string Scheme, string Scope, string Value);

internal sealed record InternPool(
    ImmutableDictionary<string, int> Symbols,
    ImmutableArray<string> SymbolText,
    ImmutableDictionary<IdentifierKey, int> Identifiers,
    ImmutableArray<IdentifierKey> IdentifierText)
{
    public static InternPool Empty { get; } = new(
        ImmutableDictionary.Create<string, int>(StringComparer.Ordinal),
        [],
        ImmutableDictionary.Create<IdentifierKey, int>(),
        []);

    // The id is the array index, so the first value is 0, the same Interned a null cell stores.
    public (InternPool Pool, int Id) InternSymbol(string value) =>
        Symbols.TryGetValue(value, out var existing)
            ? (this, existing)
            : (this with
            {
                Symbols = Symbols.Add(value, SymbolText.Length),
                SymbolText = SymbolText.Add(value),
            }, SymbolText.Length);

    // The id is the array index, so the first value is 0, the same Interned a null cell stores.
    public (InternPool Pool, int Id) InternIdentifier(string scheme, string scope, string value)
    {
        var key = new IdentifierKey(scheme, scope, value);
        return Identifiers.TryGetValue(key, out var existing)
            ? (this, existing)
            : (this with
            {
                Identifiers = Identifiers.Add(key, IdentifierText.Length),
                IdentifierText = IdentifierText.Add(key),
            }, IdentifierText.Length);
    }

    public string Symbol(int id) => SymbolText[id];

    public IdentifierKey Identifier(int id) => IdentifierText[id];
}
