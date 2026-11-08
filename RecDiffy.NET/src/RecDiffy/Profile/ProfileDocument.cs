namespace RecDiffy;

public sealed record WhenClause(string Field, string? EqualsValue, string? NotEquals, IReadOnlyList<string>? In)
{
    // Different fields are never exclusive, and an in list is never exclusive.
    // Equals and not-equals exclude each other only when they name the same text.
    public static bool MutuallyExclusive(WhenClause? left, WhenClause? right) => (left, right) switch
    {
        (null, _) or (_, null) => false,
        var (first, second) when first.Field != second.Field => false,
        ({ EqualsValue: { } leftEquals }, { EqualsValue: { } rightEquals }) => leftEquals != rightEquals,
        ({ EqualsValue: { } leftEquals }, { NotEquals: { } rightNot }) => leftEquals == rightNot,
        ({ NotEquals: { } leftNot }, { EqualsValue: { } rightEquals }) => leftNot == rightEquals,
        _ => false,
    };
}

public sealed record FieldDeclaration(string Name, FieldType Type, FieldRole Role, int Scale);

public sealed record RuleCall(
    string Rule,
    WhenClause? When,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Lists,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Maps);

public sealed record NormalizationSide(
    IReadOnlyList<RuleCall> Identify,
    IReadOnlyList<RuleCall> Parse,
    IReadOnlyList<RuleCall> Derive,
    IReadOnlyList<RuleCall> Validate);

public sealed record RouteDeclaration(string Rule, string? Field, string? EqualsValue);

public sealed record PassDeclaration(
    string Id,
    string Pass,
    IReadOnlyList<string> Key,
    IReadOnlyList<string> Block,
    IReadOnlyList<string> Conserve,
    string? Many,
    int? MaxGroupSize);

public sealed record ToleranceDeclaration(string Kind, decimal? Absolute, decimal? Relative, string? Attributed, WhenClause? When, int? Days = null);

public sealed record FieldDiffDeclaration(ToleranceDeclaration Tolerance, string? Attributed, WhenClause? ApplicableWhen);

public sealed record DiffDeclaration(IReadOnlyDictionary<string, FieldDiffDeclaration> Fields);

public sealed record PathDeclaration(string Id, RouteDeclaration Route, IReadOnlyList<PassDeclaration> Passes, DiffDeclaration? Diff);

public sealed record SearchLimits(int MaxGroupSize, int MaxBlockItems, int MaxTargetsPerComponent, int MaxNodesPerTarget)
{
    public static SearchLimits Default { get; } = new(8, 24, 8, 250_000);
}

internal sealed record ConsolidationSide(LifecyclePlan? Lifecycle, IReadOnlyList<PackagePlan> Packages);

/// <summary>One side's declared reading: its culture name (empty for invariant) and whether its date formats are strict. Null means not declared.</summary>
public sealed record SourceReading(string? Culture, bool? StrictDateFormats);

public sealed record ProfileDocument
{
    public required int FormatVersion { get; init; }

    public required string ProfileId { get; init; }

    public required string Version { get; init; }

    public required MidpointMode Midpoint { get; init; }

    public required IReadOnlyList<FieldDeclaration> Fields { get; init; }

    public required IReadOnlyDictionary<string, NormalizationSide> Normalization { get; init; }

    public required IReadOnlyList<PathDeclaration> Paths { get; init; }

    public required IReadOnlyDictionary<string, string> SourceKeys { get; init; }

    public required SearchLimits Limits { get; init; }

    /// <summary>The culture both sides' text is read with, unless a side declares its own. Null means the server's culture, read once at compile. Empty means invariant.</summary>
    public string? Culture { get; init; }

    /// <summary>Whether <c>/</c> and <c>:</c> in date formats mean exactly those characters. Null means strict.</summary>
    public bool? StrictDateFormats { get; init; }

    /// <summary>Each side's own reading, keyed by side name. A side's declaration overrides the run's.</summary>
    public IReadOnlyDictionary<string, SourceReading> SourceReadings { get; init; } =
        new Dictionary<string, SourceReading>(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, ConsolidationSide> Consolidation { get; init; } =
        new Dictionary<string, ConsolidationSide>(StringComparer.Ordinal);

    public static ProfileDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return ProfileParser.Parse(json);
    }
}
