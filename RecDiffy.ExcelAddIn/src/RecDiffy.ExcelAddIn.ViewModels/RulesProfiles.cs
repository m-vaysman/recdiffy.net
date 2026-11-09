using System.Collections.Immutable;

namespace RecDiffy.ExcelAddIn.ViewModels;

public static class RulesProfiles
{
    public const string PathId = "all";

    private const string RouteAll = "route.all";
    private const string KeyPass = "match.naturalKey";
    private const string Left = "left";
    private const string Right = "right";

    private static readonly RuleRegistry Registry = RuleRegistry.CreateCore();

    public static ProfileDocument Document(RulesState rules, ProfileDocument baseProfile)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(baseProfile);
        return baseProfile with
        {
            Fields = [.. rules.Fields.Select(field => new FieldDeclaration(field.Name, field.Type, field.Role, field.Scale))],
            Normalization = ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            [
                KeyValuePair.Create(Left, Side(rules.Fields, field => field.LeftColumn)),
                KeyValuePair.Create(Right, Side(rules.Fields, field => field.RightColumn)),
            ]),
            Paths = rules.Passes.Count == 0 ? [] : [Path(rules)],
        };
    }

    public static IReadOnlyList<ProfileProblem> Check(ProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ProfileBuilder.From(profile).Build(Registry).Problems;
    }

    public static RulesState? Read(ProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!IsEditable(profile))
            return null;

        var path = profile.Paths.FirstOrDefault();
        var diff = path?.Diff?.Fields ?? ImmutableDictionary<string, FieldDiffDeclaration>.Empty;
        return new RulesState(
            [.. profile.Fields.Select(field => ReadField(profile, field))],
            [.. (path?.Passes ?? []).Select(pass => new PassRule([.. pass.Key]))],
            [.. profile.Fields.Where(field => field.Role is FieldRole.Economic).Select(field => ReadComparison(field.Name, diff.GetValueOrDefault(field.Name)))]);
    }

    public static IReadOnlyList<ComparisonKind> KindsFor(FieldType type) => type switch
    {
        FieldType.Decimal => [ComparisonKind.Exact, ComparisonKind.Absolute, ComparisonKind.Relative, ComparisonKind.AbsoluteOrRelative],
        FieldType.Date => [ComparisonKind.Exact, ComparisonKind.Days],
        _ => [ComparisonKind.Exact],
    };

    private static NormalizationSide Side(IReadOnlyList<FieldRule> fields, Func<FieldRule, string?> column)
    {
        var read = fields.SelectMany(field => column(field) is { } name ? [(Field: field, Column: name)] : Array.Empty<(FieldRule Field, string Column)>()).ToArray();
        return new NormalizationSide(
            [.. read.Where(pair => IsIdentified(pair.Field.Type)).Select(pair => Call(pair.Field, pair.Column))],
            [.. read.Where(pair => !IsIdentified(pair.Field.Type)).Select(pair => Call(pair.Field, pair.Column))],
            [],
            []);
    }

    private static bool IsIdentified(FieldType type) => type is FieldType.Text or FieldType.Identifier or FieldType.Symbol;

    private static RuleCall Call(FieldRule field, string column) => field.Type switch
    {
        FieldType.Text => Rule("text.copy", column, field.Name),
        FieldType.Identifier => Rule("identifier.map", column, field.Name, ("scheme", field.Name)),
        FieldType.Symbol => Rule("symbol.map", column, field.Name),
        FieldType.Decimal => Rule("decimal.parse", column, field.Name),
        FieldType.Date => Rule("date.parse", column, field.Name) with
        {
            Lists = field.DateFormats.Count == 0
                ? ImmutableDictionary<string, IReadOnlyList<string>>.Empty
                : ImmutableDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("formats", (IReadOnlyList<string>)[.. field.DateFormats])]),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field.Type, "Unknown field type."),
    };

    private static RuleCall Rule(string rule, string from, string to, params (string Name, string Value)[] more) => new(
        rule,
        null,
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("from", from), KeyValuePair.Create("to", to), .. more.Select(pair => KeyValuePair.Create(pair.Name, pair.Value))]),
        ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
        ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);

    private static PathDeclaration Path(RulesState rules)
    {
        var tolerances = rules.Comparisons
            .Where(comparison => comparison.Kind is not ComparisonKind.Exact)
            .Select(comparison => KeyValuePair.Create(comparison.Field, new FieldDiffDeclaration(Tolerance(comparison), null, null)))
            .ToArray();
        return new PathDeclaration(
            PathId,
            new RouteDeclaration(RouteAll, null, null),
            [.. rules.Passes.Select((pass, index) => new PassDeclaration($"P{index + 1}", KeyPass, [.. pass.Key], [], [], null, null))],
            tolerances.Length == 0 ? null : new DiffDeclaration(ImmutableDictionary.CreateRange(StringComparer.Ordinal, tolerances)));
    }

    private static ToleranceDeclaration Tolerance(ComparisonRule comparison) => comparison.Kind switch
    {
        ComparisonKind.Absolute => new ToleranceDeclaration("Absolute", comparison.Absolute, null, null, null),
        ComparisonKind.Relative => new ToleranceDeclaration("Relative", null, comparison.Relative, null, null),
        ComparisonKind.AbsoluteOrRelative => new ToleranceDeclaration("AbsoluteOrRelative", comparison.Absolute, comparison.Relative, null, null),
        ComparisonKind.Days => new ToleranceDeclaration("Days", null, null, null, null, comparison.Days),
        _ => new ToleranceDeclaration("Exact", null, null, null, null),
    };

    private static FieldRule ReadField(ProfileDocument profile, FieldDeclaration field)
    {
        var left = ReadCall(profile, Left, field.Name);
        var right = ReadCall(profile, Right, field.Name);
        var formats = (left ?? right)?.Lists.GetValueOrDefault("formats") ?? [];
        return new FieldRule(field.Name, left?.Strings["from"], right?.Strings["from"], field.Type, field.Role, field.Scale, [.. formats]);
    }

    private static RuleCall? ReadCall(ProfileDocument profile, string side, string field) =>
        profile.Normalization.GetValueOrDefault(side) is { } rules
            ? rules.Identify.Concat(rules.Parse).FirstOrDefault(call => string.Equals(call.Strings.GetValueOrDefault("to"), field, StringComparison.Ordinal))
            : null;

    private static ComparisonRule ReadComparison(string field, FieldDiffDeclaration? diff) => diff?.Tolerance switch
    {
        { Kind: "Absolute" } tolerance => new ComparisonRule(field, ComparisonKind.Absolute, tolerance.Absolute, null, null),
        { Kind: "Relative" } tolerance => new ComparisonRule(field, ComparisonKind.Relative, null, tolerance.Relative, null),
        { Kind: "AbsoluteOrRelative" } tolerance => new ComparisonRule(field, ComparisonKind.AbsoluteOrRelative, tolerance.Absolute, tolerance.Relative, null),
        { Kind: "Days" } tolerance => new ComparisonRule(field, ComparisonKind.Days, null, null, tolerance.Days),
        _ => new ComparisonRule(field, ComparisonKind.Exact, null, null, null),
    };

    private static bool IsEditable(ProfileDocument profile) =>
        profile.Paths.Count <= 1
        && profile.Paths.All(path => path is { Route.Rule: RouteAll }
            && path.Passes.All(pass => pass is { Pass: KeyPass, Block.Count: 0, Conserve.Count: 0, Many: null, MaxGroupSize: null })
            && (path.Diff?.Fields ?? ImmutableDictionary<string, FieldDiffDeclaration>.Empty).All(pair =>
                pair.Value is { Attributed: null, ApplicableWhen: null, Tolerance: { When: null, Attributed: null, Kind: "Exact" or "Absolute" or "Relative" or "AbsoluteOrRelative" or "Days" } }
                && profile.Fields.Any(field => string.Equals(field.Name, pair.Key, StringComparison.Ordinal) && field.Role is FieldRole.Economic)))
        && profile.Normalization.Values.All(side => side is { Derive.Count: 0, Validate.Count: 0 }
            && side.Identify.Concat(side.Parse).All(call => call.When is null && call.Maps.Count == 0
                && call.Rule is "text.copy" or "identifier.map" or "symbol.map" or "decimal.parse" or "date.parse"
                && call.Strings.ContainsKey("from") && call.Strings.ContainsKey("to")));
}
