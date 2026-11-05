using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class ProfileCompiler
{
    public static CompileOutcome Compile(ProfileDocument document, RuleRegistry registry)
    {
        var formatErrors = document.FormatVersion == 1
            ? []
            : new[] { new ProfileError("UnsupportedFormat", $"formatVersion {document.FormatVersion} is not accepted.") };
        // FieldSchema throws on a repeated name, so DuplicateField below is never produced.
        var schema = new FieldSchema(document.Fields);
        var duplicateErrors = document.Fields
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new ProfileError("DuplicateField", $"Field '{group.Key}' is declared more than once."));
        var sides = document.Normalization.ToImmutableDictionary(
            pair => pair.Key,
            pair => CompileSide(pair.Key, pair.Value, schema, document.Midpoint, registry),
            StringComparer.Ordinal);
        var pathErrors = ValidatePaths(document, schema, sides);
        ProfileError[] errors = [.. formatErrors, .. duplicateErrors, .. sides.Values.SelectMany(side => side.Errors), .. pathErrors];
        // A side whose name is not "left" or "right" contributes errors only. Its rules are dropped.
        return errors.Length == 0
            ? new CompileOutcome(new CompiledProfile(document.ProfileId, document.Version, document.FormatVersion, Hash(document), document, SideRules(sides, "left"), SideRules(sides, "right")), [])
            : new CompileOutcome(null, errors);
    }

    private static SideCompilation CompileSide(string side, NormalizationSide normalization, FieldSchema schema, MidpointMode midpoint, RuleRegistry registry) =>
        Phases(normalization).Aggregate(SideCompilation.Empty, (state, phase) =>
            phase.Calls.Aggregate(state, (current, call) => current.Accept(side, phase.Name, phase.Phase, call, schema, midpoint, registry)));

    // Identify, then parse, then derive, then validate. A later phase reads what an earlier phase wrote.
    private static IEnumerable<(string Name, RulePhase Phase, IReadOnlyList<RuleCall> Calls)> Phases(NormalizationSide normalization) =>
    [
        ("identify", RulePhase.Identify, normalization.Identify),
        ("parse", RulePhase.Parse, normalization.Parse),
        ("derive", RulePhase.Derive, normalization.Derive),
        ("validate", RulePhase.Validate, normalization.Validate),
    ];

    private static IReadOnlyList<ProfileError> ValidatePaths(ProfileDocument document, FieldSchema schema, IReadOnlyDictionary<string, SideCompilation> sides)
    {
        var overlap = document.Paths.SelectMany((path, index) => document.Paths.Take(index).Where(earlier => RoutesOverlap(earlier.Route, path.Route)).Select(_ =>
            new ProfileError("OverlappingRoute", $"Path '{path.Id}' overlaps another route.")));
        var passErrors = document.Paths.SelectMany(path => path.Passes.SelectMany(pass => ValidatePass(path, pass, document, schema, sides)));
        var diffErrors = document.Paths.SelectMany(path => (path.Diff?.Fields ?? ImmutableDictionary<string, FieldDiffDeclaration>.Empty).SelectMany(pair =>
            ValidateDiff(pair.Key, pair.Value, schema)));
        var limit = document.Limits.MaxGroupSize > 16
            ? new[] { new ProfileError("LimitExceeded", "limits.maxGroupSize exceeds the hard cap of 16.") }
            : [];
        return [.. overlap, .. passErrors, .. diffErrors, .. limit];
    }

    private static IEnumerable<ProfileError> ValidatePass(PathDeclaration path, PassDeclaration pass, ProfileDocument document, FieldSchema schema, IReadOnlyDictionary<string, SideCompilation> sides)
    {
        var unknown = KnownPass(pass.Pass) ? [] : new[] { new ProfileError("UnknownRule", $"Pass '{pass.Pass}' is not registered.") };
        var group = pass.Pass == "match.groupSum" && (pass.Block.Count == 0 || pass.Conserve.Count == 0 || pass.Many is not ("left" or "right"))
            ? new[] { new ProfileError("MissingParameter", $"Pass '{pass.Id}' requires block, conserve, and many.") }
            : [];
        var cap = (pass.MaxGroupSize ?? document.Limits.MaxGroupSize) > 16
            ? new[] { new ProfileError("LimitExceeded", $"Pass '{pass.Id}' maxGroupSize exceeds the hard cap of 16.") }
            : [];
        // Key, block, and conserve all take the key checks. A text block fails TextKey.
        var fields = ((string[])[.. pass.Key, .. pass.Block, .. pass.Conserve]).SelectMany(field => ValidateKeyField(path.Id, field, schema, sides));
        return [.. unknown, .. group, .. cap, .. fields];
    }

    private static bool KnownPass(string pass) => pass switch
    {
        "match.sharedRef" or "match.naturalKey" or "match.groupSum" or "match.reducedKey" => true,
        _ => false,
    };

    // Every side listed in normalization must write the field. A side that is absent is not checked.
    private static IEnumerable<ProfileError> ValidateKeyField(string pathId, string field, FieldSchema schema, IReadOnlyDictionary<string, SideCompilation> sides) =>
        schema.TryGet(field, out var declaration) switch
        {
            false => [new ProfileError("UnknownField", $"Path '{pathId}' names unknown field '{field}'.")],
            true when declaration is { Type: FieldType.Text } or { Role: FieldRole.Text } => [new ProfileError("TextKey", $"Path '{pathId}' uses text field '{field}' as a key.")],
            true when sides.Values.Any(side => !side.Written.Contains(field)) => [new ProfileError("FieldNotProduced", $"Path '{pathId}' keys '{field}', which a side does not write.")],
            _ => [],
        };

    private static IEnumerable<ProfileError> ValidateDiff(string field, FieldDiffDeclaration diff, FieldSchema schema) =>
        schema.TryGet(field, out var declaration) switch
        {
            false => [new ProfileError("UnknownField", $"Diff field '{field}' is not declared.")],
            true when !ToleranceFits(declaration.Type, diff.Tolerance.Kind) => [new ProfileError("ToleranceMismatch", $"Tolerance '{diff.Tolerance.Kind}' does not fit field '{field}'.")],
            _ => [],
        };

    // Exact fits every type. Absolute, relative, and attributed fit decimal only. Days fits date only.
    private static bool ToleranceFits(FieldType type, string kind) => (type, kind) switch
    {
        (FieldType.Decimal, "Exact" or "Absolute" or "Relative" or "AbsoluteOrRelative" or "Attributed") => true,
        (FieldType.Date, "Exact" or "Days") => true,
        (_, "Exact") => true,
        _ => false,
    };

    // route.all meets every route. Two byField routes meet only when the field and the value are equal.
    // Any other rule name never overlaps, including a repeated name.
    private static bool RoutesOverlap(RouteDeclaration left, RouteDeclaration right) => (left.Rule, right.Rule) switch
    {
        ("route.all", _) or (_, "route.all") => true,
        ("route.byField", "route.byField") => left.Field == right.Field && left.EqualsValue == right.EqualsValue,
        _ => false,
    };

    private static IReadOnlyList<CompiledRule> SideRules(IReadOnlyDictionary<string, SideCompilation> sides, string name) =>
        sides.TryGetValue(name, out var side) ? side.Rules : [];

    private static string Hash(ProfileDocument document)
    {
        // Sorted by name, so declaration order does not change Hash.
        // Paths, rules, tolerances, and limits are omitted. Those can change while Hash stays put.
        var text = string.Join(
            ",",
            document.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => $"{field.Name}:{field.Type}:{field.Role}:{field.Scale}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{document.FormatVersion}|{document.ProfileId}|{document.Version}|{document.Midpoint}|{text}"))).ToLowerInvariant();
    }
}

internal sealed record CompileOutcome(CompiledProfile? Profile, IReadOnlyList<ProfileError> Errors);

internal sealed record SideCompilation(ImmutableDictionary<string, WriteSite> WrittenSites, ImmutableList<ProfileError> Errors, ImmutableList<CompiledRule> Rules)
{
    public IReadOnlySet<string> Written => WrittenSites.Keys.ToHashSet(StringComparer.Ordinal);

    public static SideCompilation Empty { get; } = new(
        ImmutableDictionary.Create<string, WriteSite>(StringComparer.Ordinal),
        [],
        []);

    public SideCompilation Accept(string side, string phaseName, RulePhase phase, RuleCall call, FieldSchema schema, MidpointMode midpoint, RuleRegistry registry) =>
        registry.TryGet(call.Rule, out var registration) switch
        {
            false => Fail("UnknownRule", $"Rule '{call.Rule}' is not registered."),
            true when registration.Phase != phase => Fail("WrongPhase", $"Rule '{call.Rule}' is listed under {phaseName} but belongs to {registration.Phase}."),
            true when Missing(registration, call) is { } missing => Fail("MissingParameter", $"{side}.{call.Rule} is missing '{missing}'."),
            true when call.Strings.TryGetValue("to", out var target) && !schema.TryGet(target, out _) => Fail("UnknownField", $"{side}.{call.Rule} writes undeclared field '{target}'."),
            _ => Bind(side, call, registration, schema, midpoint),
        };

    private SideCompilation Bind(string side, RuleCall call, NormalizationRegistration registration, FieldSchema schema, MidpointMode midpoint)
    {
        var rule = registration.Create(call, schema, midpoint);
        var readErrors = rule.Reads.Where(read => !WrittenSites.ContainsKey(read.Name)).Select(read =>
            new ProfileError("FieldReadBeforeWrite", $"{side}.{call.Rule} reads '{read.Name}' before any rule writes it."));
        // A rule that reads the field it writes skips the ambiguous-write check.
        var writeErrors = rule.Writes.Where(write =>
            WrittenSites.TryGetValue(write.Name, out var previous)
            && !rule.Reads.Any(read => read.Name == write.Name)
            && !WhenClause.MutuallyExclusive(previous.When, call.When)).Select(write =>
            new ProfileError("AmbiguousWrite", $"{side} field '{write.Name}' is written by '{WrittenSites[write.Name].Rule}' and '{call.Rule}'."));
        var unknown = ((FieldId[])[.. rule.Reads, .. rule.Writes]).Where(field => schema.TryGet(field.Name, out _) is false).Select(field =>
            new ProfileError("UnknownField", $"{side}.{call.Rule} uses undeclared field '{field.Name}'."));
        ProfileError[] errors = [.. readErrors, .. writeErrors, .. unknown];
        // On success the site keeps the latest clause only.
        // A newer clause can overlap an earlier write when each neighbor pair is exclusive.
        var written = rule.Writes.Aggregate(WrittenSites, (sites, write) => sites.SetItem(write.Name, new WriteSite(call.Rule, call.When)));
        // Any error drops the whole call. Writes from that call are not recorded.
        // A later read of a target that no earlier call wrote reports FieldReadBeforeWrite.
        return errors.Length == 0
            ? this with { WrittenSites = written, Rules = Rules.Add(new CompiledRule(rule, call.When)) }
            : this with { Errors = Errors.AddRange(errors) };
    }

    private SideCompilation Fail(string code, string message) => this with { Errors = Errors.Add(new ProfileError(code, message)) };

    // "fields" has to be a list. A string under that name is missing.
    // Any other required name is present as either a string or a list, and a list named "from" passes here then throws in the factory.
    private static string? Missing(NormalizationRegistration registration, RuleCall call) =>
        registration.Required.FirstOrDefault(required => required switch
        {
            "fields" => !call.Lists.ContainsKey(required),
            _ => !call.Strings.ContainsKey(required) && !call.Lists.ContainsKey(required),
        });
}

internal sealed record WriteSite(string Rule, WhenClause? When);

internal sealed record CompiledRule(INormalizationRule Rule, WhenClause? When);

public sealed class CompiledProfile
{
    internal CompiledProfile(
        string profileId,
        string version,
        int formatVersion,
        string hash,
        ProfileDocument document,
        IReadOnlyList<CompiledRule> leftRules,
        IReadOnlyList<CompiledRule> rightRules)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(leftRules);
        ArgumentNullException.ThrowIfNull(rightRules);
        ProfileId = profileId;
        Version = version;
        FormatVersion = formatVersion;
        Hash = hash;
        Document = document;
        LeftRules = leftRules;
        RightRules = rightRules;
    }

    public string ProfileId { get; }

    public string Version { get; }

    public int FormatVersion { get; }

    public string Hash { get; }

    public ProfileDocument Document { get; }

    internal IReadOnlyList<CompiledRule> LeftRules { get; }

    internal IReadOnlyList<CompiledRule> RightRules { get; }
}
