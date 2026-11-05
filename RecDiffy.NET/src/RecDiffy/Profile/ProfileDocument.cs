namespace RecDiffy;

public sealed record WhenClause(string Field, string? EqualsValue, string? NotEquals, IReadOnlyList<string>? In)
{
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

public sealed record ToleranceDeclaration(string Kind, decimal? Absolute, decimal? Relative, string? Attributed, WhenClause? When);

public sealed record FieldDiffDeclaration(ToleranceDeclaration Tolerance, string? Attributed, WhenClause? ApplicableWhen);

public sealed record DiffDeclaration(IReadOnlyDictionary<string, FieldDiffDeclaration> Fields);

public sealed record PathDeclaration(string Id, RouteDeclaration Route, IReadOnlyList<PassDeclaration> Passes, DiffDeclaration? Diff);

public sealed record SearchLimits(int MaxGroupSize, int MaxBlockItems, int MaxTargetsPerComponent, int MaxNodesPerTarget)
{
    public static SearchLimits Default { get; } = new(8, 24, 8, 250_000);
}

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

    public static ProfileDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return ProfileParser.Parse(json);
    }
}
