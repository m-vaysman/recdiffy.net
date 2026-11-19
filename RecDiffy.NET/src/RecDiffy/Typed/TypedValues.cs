using System.Collections.Immutable;

namespace RecDiffy;

// The spellings one side's text member can have, and the value each one means. Spellings are listed by value, or a
// function decides for each spelling found in the data. A spelling is compared as written, after trimming.
internal sealed record TypedSpellings(ImmutableDictionary<string, string> Listed, Func<string, string?>? Decide)
{
    public static TypedSpellings Empty { get; } = new(ImmutableDictionary.Create<string, string>(StringComparer.Ordinal), null);

    public bool IsDeclared => Decide is not null || !Listed.IsEmpty;

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
        ArgumentNullException.ThrowIfNull(decide, parameter);
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

// What a key member's text means on each side, and which of its values are opposites. A pair is (a value of the left
// items, the value of the right items that matches it). A function can add the pairs it finds true.
internal sealed record TypedMeaning(TypedSpellings Ours, TypedSpellings Theirs, ImmutableList<(string Ours, string Theirs)> Pairs, Func<string, string, bool>? Test)
{
    public static TypedMeaning Empty { get; } = new(TypedSpellings.Empty, TypedSpellings.Empty, [], null);

    public TypedMeaning Value(string value, IReadOnlyList<string> ours, IReadOnlyList<string> theirs) =>
        this with { Ours = Ours.Accept(value, ours, nameof(ours)), Theirs = Theirs.Accept(value, theirs, nameof(theirs)) };

    public TypedMeaning Decided(Func<string, string?> ours, Func<string, string?> theirs) =>
        this with { Ours = Ours.Decided(ours, nameof(ours)), Theirs = Theirs.Decided(theirs, nameof(theirs)) };

    public TypedMeaning Opposite(IReadOnlyList<(string Ours, string Theirs)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var named = pairs.Select(pair => (Ours: pair.Ours?.Trim(), Theirs: pair.Theirs?.Trim())).ToArray();
        return named switch
        {
            [] => throw new ArgumentException("List at least one pair.", nameof(pairs)),
            _ when named.Any(pair => string.IsNullOrEmpty(pair.Ours) || string.IsNullOrEmpty(pair.Theirs)) =>
                throw new ArgumentException("A pair needs two values.", nameof(pairs)),
            _ => this with { Pairs = Pairs.AddRange(named.Select(pair => (pair.Ours ?? "", pair.Theirs ?? ""))) },
        };
    }

    public TypedMeaning Opposite(Func<string, string, bool> test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return Test is null
            ? this with { Test = test }
            : throw new InvalidOperationException("The opposites are decided by a function already.");
    }

    // The tables the profile holds. Without opposites each side's spellings are read as the values they mean. With
    // them, the left side is read the same way, and each right spelling is read as the left value that matches the
    // value it means, so a plain key match on the result matches opposites. A right value with no opposite is not
    // accepted: its spellings are left out, and their rows are quarantined.
    public TypedMaps Resolve(IEnumerable<string> oursFound, IEnumerable<string> theirsFound) =>
        Pairs.IsEmpty && Test is null
            ? new(Ours.Table(oursFound), Theirs.Table(theirsFound))
            : Opposed(oursFound, theirsFound);

    private TypedMaps Opposed(IEnumerable<string> oursFound, IEnumerable<string> theirsFound)
    {
        var ours = Ours.Table(oursFound);
        var theirs = Theirs.Table(theirsFound);
        NameKnown(Ours, ours, Pairs.Select(pair => pair.Ours), "left");
        NameKnown(Theirs, theirs, Pairs.Select(pair => pair.Theirs), "right");
        var oursDomain = Ours.IsDeclared ? ours.Values.Distinct(StringComparer.Ordinal) : oursFound.Distinct(StringComparer.Ordinal);
        var theirsDomain = Theirs.IsDeclared ? theirs.Values.Distinct(StringComparer.Ordinal) : theirsFound.Distinct(StringComparer.Ordinal);
        var tested = Test is { } test
            ? oursDomain.SelectMany(left => theirsDomain.Where(right => test(left, right)).Select(right => (Ours: left, Theirs: right)))
            : [];
        var pairs = Pairs.Concat(tested).Distinct().ToArray();
        var left = Ours.IsDeclared ? ours : Identity(pairs.Select(pair => pair.Ours));
        var meant = Theirs.IsDeclared ? theirs : Identity(pairs.Select(pair => pair.Theirs));
        return new(left, meant.SelectMany(entry => Partner(pairs, entry)).ToImmutableDictionary(StringComparer.Ordinal));
    }

    // A value that was listed with Value has to be one the pairs can name. A function's values come from the data.
    private static void NameKnown(TypedSpellings spellings, ImmutableDictionary<string, string> table, IEnumerable<string> named, string side)
    {
        if (spellings.Listed.IsEmpty)
            return;
        var values = table.Values.ToHashSet(StringComparer.Ordinal);
        if (named.FirstOrDefault(value => !values.Contains(value)) is { } unknown)
            throw new InvalidOperationException($"'{unknown}' is not a value named for the {side} items. Name it with Value, or correct the pair.");
    }

    private static ImmutableDictionary<string, string> Identity(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).ToImmutableDictionary(value => value, value => value, StringComparer.Ordinal);

    private static IEnumerable<KeyValuePair<string, string>> Partner((string Ours, string Theirs)[] pairs, KeyValuePair<string, string> spelling) =>
        pairs.Where(pair => pair.Theirs == spelling.Value).Select(pair => pair.Ours).Distinct(StringComparer.Ordinal).ToArray() switch
        {
            [] => [],
            [var only] => [KeyValuePair.Create(spelling.Key, only)],
            [var first, var second, ..] => throw new InvalidOperationException($"'{spelling.Value}' is the opposite of '{first}' and '{second}'; a value can have one opposite."),
        };
}

// The two tables a profile holds for one member: what each side's spelling is read as.
internal sealed record TypedMaps(ImmutableDictionary<string, string> Left, ImmutableDictionary<string, string> Right);
