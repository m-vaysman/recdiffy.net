using System.Collections.Immutable;

namespace RecDiffy;

// The spellings one side's text member can have, and the value each one means. Spellings are listed by value, or a
// function decides for each spelling found in the data. A spelling is compared as written, after trimming.
internal sealed record TypedSpellings(ImmutableDictionary<string, string> Listed, Func<string, string?>? Decide)
{
    public static TypedSpellings Empty { get; } = new(ImmutableDictionary.Create<string, string>(StringComparer.Ordinal), null);

    public TypedSpellings Accept(string value, IReadOnlyList<string> spellings, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(spellings);
        var name = value.Trim();
        var written = spellings.Select(spelling => spelling?.Trim()).ToArray();
        return this switch
        {
            _ when name.Length == 0 => throw new ArgumentException("A value needs a name.", nameof(value)),
            { Decide: not null } => throw new InvalidOperationException("The values are decided by a function already; list them or use a function, not both."),
            _ when written.Any(string.IsNullOrEmpty) => throw new ArgumentException("A spelling cannot be empty.", parameter),
            _ when written.FirstOrDefault(spelling => Listed.TryGetValue(spelling ?? "", out var meant) && meant != name) is { } clash =>
                throw new ArgumentException($"'{clash}' already means '{Listed[clash]}', so it cannot also mean '{name}'.", parameter),
            _ => this with { Listed = Listed.SetItems(written.OfType<string>().Select(spelling => KeyValuePair.Create(spelling, name))) },
        };
    }

    public TypedSpellings Decided(Func<string, string?> decide, string parameter)
    {
        ArgumentNullException.ThrowIfNull(decide);
        return this switch
        {
            { Decide: not null } => throw new InvalidOperationException("The values are decided by a function already."),
            { Listed.IsEmpty: false } => throw new InvalidOperationException("The values are listed already; list them or use a function, not both."),
            _ => this with { Decide = decide },
        };
    }

    // The spelling-to-value table the profile holds. A function is asked about each spelling found in the data, once.
    public ImmutableDictionary<string, string> Table(IEnumerable<string> found) =>
        Decide is { } decide
            ? found.Distinct(StringComparer.Ordinal).SelectMany(spelling => Meant(decide, spelling)).ToImmutableDictionary(StringComparer.Ordinal)
            : Listed;

    private static IEnumerable<KeyValuePair<string, string>> Meant(Func<string, string?> decide, string spelling) =>
        decide(spelling) is { } value ? [KeyValuePair.Create(spelling, value.Trim())] : [];
}

// What a key member's text means on each side.
internal sealed record TypedMeaning(TypedSpellings Ours, TypedSpellings Theirs)
{
    public static TypedMeaning Empty { get; } = new(TypedSpellings.Empty, TypedSpellings.Empty);

    public TypedMeaning Value(string value, IReadOnlyList<string> ours, IReadOnlyList<string> theirs) =>
        new(Ours.Accept(value, ours, nameof(ours)), Theirs.Accept(value, theirs, nameof(theirs)));

    public TypedMeaning Decided(Func<string, string?> ours, Func<string, string?> theirs) =>
        new(Ours.Decided(ours, nameof(ours)), Theirs.Decided(theirs, nameof(theirs)));

    public TypedMaps Resolve(IEnumerable<string> oursFound, IEnumerable<string> theirsFound) =>
        new(Ours.Table(oursFound), Theirs.Table(theirsFound));
}

// The two tables a profile holds for one member: what each side's spelling is read as.
internal sealed record TypedMaps(ImmutableDictionary<string, string> Left, ImmutableDictionary<string, string> Right);
