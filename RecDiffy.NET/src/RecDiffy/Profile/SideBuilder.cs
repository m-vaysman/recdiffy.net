using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy;

/// <summary>
/// One side's normalization rules. Each rule lands in its phase (identify, parse, derive, validate) whatever order the
/// calls are made in; within a phase, rules keep the order they were added. Every method returns a new builder.
/// </summary>
public sealed class SideBuilder
{
    private readonly ImmutableDictionary<RulePhase, ImmutableList<RuleCall>> _phases;

    private SideBuilder(ImmutableDictionary<RulePhase, ImmutableList<RuleCall>> phases) => _phases = phases;

    public static SideBuilder Empty { get; } = new(ImmutableDictionary<RulePhase, ImmutableList<RuleCall>>.Empty);

    /// <summary>Parses a date column with one exact format.</summary>
    public SideBuilder ParseDate(string from, string to, string format, WhenClause? when = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        return ParseDate(from, to, [format], when);
    }

    /// <summary>Parses a date column with the first format that fits. With no formats, any invariant-culture date is accepted.</summary>
    public SideBuilder ParseDate(string from, string to, IReadOnlyList<string> formats, WhenClause? when = null)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return Add(RulePhase.Parse, "date.parse", Columns(from, to), formats.Count == 0 ? null : new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["formats"] = [.. formats] }, null, when);
    }

    /// <summary>Parses a date column with the format reference data gives for the value of <paramref name="formatBy"/>, a declared field.</summary>
    public SideBuilder ParseDateBy(string from, string to, string formatBy, WhenClause? when = null)
    {
        ArgumentNullException.ThrowIfNull(formatBy);
        return Add(RulePhase.Parse, "date.parse", Columns(from, to, ("formatBy", formatBy)), null, null, when);
    }

    public SideBuilder ParseDecimal(string from, string to, WhenClause? when = null) =>
        Add(RulePhase.Parse, "decimal.parse", Columns(from, to), null, null, when);

    /// <summary>Maps a column to a symbol. Values missing from <paramref name="map"/> pass through unless <paramref name="passthrough"/> is false.</summary>
    public SideBuilder MapSymbol(string from, string to, IReadOnlyDictionary<string, string>? map = null, bool passthrough = true, WhenClause? when = null)
    {
        var strings = passthrough ? Columns(from, to) : Columns(from, to, ("passthrough", "false"));
        var maps = map is { Count: > 0 }
            ? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal) { ["map"] = map.ToImmutableDictionary(StringComparer.Ordinal) }
            : null;
        return Add(RulePhase.Identify, "symbol.map", strings, null, maps, when);
    }

    public SideBuilder MapIdentifier(string from, string to, string scheme, string? scope = null, WhenClause? when = null)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var strings = scope is null ? Columns(from, to, ("scheme", scheme)) : Columns(from, to, ("scheme", scheme), ("scope", scope));
        return Add(RulePhase.Identify, "identifier.map", strings, null, null, when);
    }

    public SideBuilder CopyText(string from, string to, WhenClause? when = null) =>
        Add(RulePhase.Identify, "text.copy", Columns(from, to), null, null, when);

    /// <summary>Marks a row invalid when any of these canonical fields is null.</summary>
    public SideBuilder Required(params string[] fields) => Required(fields, null);

    public SideBuilder Required(IReadOnlyList<string> fields, WhenClause? when)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Add(RulePhase.Validate, "required.present", null, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["fields"] = [.. fields] }, null, when);
    }

    /// <summary>Adds any registered rule, including a custom one, by name and parameters.</summary>
    public SideBuilder Rule(
        RulePhase phase,
        string name,
        IReadOnlyDictionary<string, string>? strings = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? lists = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? maps = null,
        WhenClause? when = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Add(phase, name, strings, lists, maps, when);
    }

    public SideBuilder Rule(RulePhase phase, RuleCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return Add(phase, call.Rule, call.Strings, call.Lists, call.Maps, call.When);
    }

    internal NormalizationSide ToSide() =>
        new([.. Calls(RulePhase.Identify)], [.. Calls(RulePhase.Parse)], [.. Calls(RulePhase.Derive)], [.. Calls(RulePhase.Validate)]);

    internal static SideBuilder From(NormalizationSide side) => new(ImmutableDictionary.CreateRange(
    [
        KeyValuePair.Create(RulePhase.Identify, side.Identify.ToImmutableList()),
        KeyValuePair.Create(RulePhase.Parse, side.Parse.ToImmutableList()),
        KeyValuePair.Create(RulePhase.Derive, side.Derive.ToImmutableList()),
        KeyValuePair.Create(RulePhase.Validate, side.Validate.ToImmutableList()),
    ]));

    private SideBuilder Add(
        RulePhase phase,
        string name,
        IReadOnlyDictionary<string, string>? strings,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? lists,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? maps,
        WhenClause? when)
    {
        // Copies every collection, so a caller who keeps and changes a dictionary it passed cannot change this builder.
        var call = new RuleCall(
            name,
            Condition.Frozen(when),
            (strings ?? ImmutableDictionary<string, string>.Empty).ToImmutableDictionary(StringComparer.Ordinal),
            (lists ?? ImmutableDictionary<string, IReadOnlyList<string>>.Empty).ToImmutableDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)[.. pair.Value], StringComparer.Ordinal),
            (maps ?? ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty).ToImmutableDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)pair.Value.ToImmutableDictionary(StringComparer.Ordinal), StringComparer.Ordinal));
        return new(_phases.SetItem(phase, Calls(phase).Add(call)));
    }

    private ImmutableList<RuleCall> Calls(RulePhase phase) => _phases.GetValueOrDefault(phase) ?? [];

    private static ImmutableDictionary<string, string> Columns(string from, string to, params (string Name, string Value)[] more)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return ImmutableDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("from", from), KeyValuePair.Create("to", to), .. more.Select(pair => KeyValuePair.Create(pair.Name, pair.Value))]);
    }
}
