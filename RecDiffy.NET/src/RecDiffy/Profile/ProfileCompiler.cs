using System.Collections.Immutable;
using System.Globalization;
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
        var duplicateErrors = document.Fields
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new ProfileError("DuplicateField", $"Field '{group.Key}' is declared more than once."))
            .ToArray();
        var scaleErrors = document.Fields
            .Where(field => field.Scale is < 0 or > 28)
            .Select(field => new ProfileError("InvalidParameter", $"Field '{field.Name}' scale {field.Scale} is outside 0..28."))
            .ToArray();
        // ToImmutableDictionary throws on a repeated name, so DuplicateField has to be returned before FieldSchema is built.
        if (duplicateErrors.Length > 0)
            return new CompileOutcome(null, [.. formatErrors, .. duplicateErrors, .. scaleErrors]);

        var schema = new FieldSchema(document.Fields);
        var sides = document.Normalization.ToImmutableDictionary(
            pair => pair.Key,
            pair => CompileSide(pair.Key, pair.Value, schema, document.Midpoint, registry),
            StringComparer.Ordinal);
        var pathErrors = ValidatePaths(document, schema, sides);
        ProfileError[] errors = [.. formatErrors, .. duplicateErrors, .. scaleErrors, .. sides.Values.SelectMany(side => side.Errors), .. pathErrors];
        return errors.Length == 0
            ? new CompileOutcome(new CompiledProfile(document.ProfileId, document.Version, document.FormatVersion, Hash(document), document, SideRules(sides, "left"), SideRules(sides, "right")), [])
            : new CompileOutcome(null, errors);
    }

    private static SideCompilation CompileSide(string side, NormalizationSide normalization, FieldSchema schema, MidpointMode midpoint, RuleRegistry registry) =>
        Phases(normalization).Aggregate(SideCompilation.Empty, (state, phase) =>
            phase.Calls.Aggregate(state, (current, call) => current.Accept(side, phase.Name, phase.Phase, call, schema, midpoint, registry)));

    private static IEnumerable<(string Name, RulePhase Phase, IReadOnlyList<RuleCall> Calls)> Phases(NormalizationSide normalization) =>
    [
        ("identify", RulePhase.Identify, normalization.Identify),
        ("parse", RulePhase.Parse, normalization.Parse),
        ("derive", RulePhase.Derive, normalization.Derive),
        ("validate", RulePhase.Validate, normalization.Validate),
    ];

    private static IReadOnlyList<ProfileError> ValidatePaths(ProfileDocument document, FieldSchema schema, IReadOnlyDictionary<string, SideCompilation> sides)
    {
        var missingPaths = document.Paths.Count == 0
            ? new[] { new ProfileError("MissingParameter", "paths must contain a route.") }
            : [];
        var routeErrors = document.Paths.SelectMany(path => ValidateRoute(path, schema));
        var overlap = document.Paths.SelectMany((path, index) => document.Paths.Take(index).Where(earlier => RoutesOverlap(earlier.Route, path.Route)).Select(_ =>
            new ProfileError("OverlappingRoute", $"Path '{path.Id}' overlaps another route.")));
        var passErrors = document.Paths.SelectMany(path => path.Passes.SelectMany(pass => ValidatePass(path, pass, document, schema, sides)));
        var diffErrors = document.Paths.SelectMany(path => (path.Diff?.Fields ?? ImmutableDictionary<string, FieldDiffDeclaration>.Empty).SelectMany(pair =>
            ValidateDiff(pair.Key, pair.Value, schema)));
        var limit = document.Limits.MaxGroupSize > 16
            ? new[] { new ProfileError("LimitExceeded", "limits.maxGroupSize exceeds the hard cap of 16.") }
            : [];
        return [.. missingPaths, .. routeErrors, .. overlap, .. passErrors, .. diffErrors, .. limit];
    }

    private static IEnumerable<ProfileError> ValidateRoute(PathDeclaration path, FieldSchema schema) => path.Route.Rule switch
    {
        "route.all" => [],
        "route.byField" when path.Route.Field is null || path.Route.EqualsValue is null =>
            [new ProfileError("MissingParameter", $"Path '{path.Id}' route.byField requires field and equals.")],
        "route.byField" when schema.TryGet(path.Route.Field!, out _) is false =>
            [new ProfileError("UnknownField", $"Path '{path.Id}' route names unknown field '{path.Route.Field}'.")],
        "route.byField" => [],
        _ => [new ProfileError("UnknownRule", $"Route '{path.Route.Rule}' is not registered.")],
    };

    private static IEnumerable<ProfileError> ValidatePass(PathDeclaration path, PassDeclaration pass, ProfileDocument document, FieldSchema schema, IReadOnlyDictionary<string, SideCompilation> sides)
    {
        var unknown = KnownPass(pass.Pass) ? [] : new[] { new ProfileError("UnknownRule", $"Pass '{pass.Pass}' is not registered.") };
        var group = pass.Pass == "match.groupSum" && (pass.Block.Count == 0 || pass.Conserve.Count == 0 || pass.Many is not ("left" or "right"))
            ? new[] { new ProfileError("MissingParameter", $"Pass '{pass.Id}' requires block, conserve, and many.") }
            : [];
        var cap = (pass.MaxGroupSize ?? document.Limits.MaxGroupSize) > 16
            ? new[] { new ProfileError("LimitExceeded", $"Pass '{pass.Id}' maxGroupSize exceeds the hard cap of 16.") }
            : [];
        var fields = ((string[])[.. pass.Key, .. pass.Block, .. pass.Conserve]).SelectMany(field => ValidateKeyField(path.Id, field, schema, sides));
        return [.. unknown, .. group, .. cap, .. fields];
    }

    private static bool KnownPass(string pass) => pass switch
    {
        "match.sharedRef" or "match.naturalKey" or "match.groupSum" or "match.reducedKey" => true,
        _ => false,
    };

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

    private static bool ToleranceFits(FieldType type, string kind) => (type, kind) switch
    {
        (FieldType.Decimal, "Exact" or "Absolute" or "Relative" or "AbsoluteOrRelative" or "Attributed") => true,
        (FieldType.Date, "Exact" or "Days") => true,
        (_, "Exact") => true,
        _ => false,
    };

    private static bool RoutesOverlap(RouteDeclaration left, RouteDeclaration right) => (left.Rule, right.Rule) switch
    {
        ("route.all", _) or (_, "route.all") => true,
        ("route.byField", "route.byField") => left.Field == right.Field && left.EqualsValue == right.EqualsValue,
        _ => false,
    };

    private static IReadOnlyList<CompiledRule> SideRules(IReadOnlyDictionary<string, SideCompilation> sides, string name) =>
        sides.TryGetValue(name, out var side) ? side.Rules : [];

    private static string Hash(ProfileDocument document) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(document)))).ToLowerInvariant();

    private static string Canonical(ProfileDocument document) =>
        Delimited.Concat('\n', Lines(document));

    // Field names are sorted. Rule calls and paths stay in profile order, so reordering those changes the hash.
    private static IEnumerable<string> Lines(ProfileDocument document)
    {
        yield return Pipe(Text(document.FormatVersion), document.ProfileId, document.Version, document.Midpoint.ToString());
        foreach (var field in document.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
            yield return Pipe("field", field.Name, field.Type.ToString(), field.Role.ToString(), Text(field.Scale));
        foreach (var source in document.SourceKeys.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            yield return Pipe("source", source.Key, source.Value);
        yield return Pipe(
            "limits",
            Text(document.Limits.MaxGroupSize),
            Text(document.Limits.MaxBlockItems),
            Text(document.Limits.MaxTargetsPerComponent),
            Text(document.Limits.MaxNodesPerTarget));
        foreach (var side in document.Normalization.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var line in Calls(side.Key, "identify", side.Value.Identify))
                yield return line;
            foreach (var line in Calls(side.Key, "parse", side.Value.Parse))
                yield return line;
            foreach (var line in Calls(side.Key, "derive", side.Value.Derive))
                yield return line;
            foreach (var line in Calls(side.Key, "validate", side.Value.Validate))
                yield return line;
        }

        foreach (var path in document.Paths)
            foreach (var line in PathLines(path))
                yield return line;
        foreach (var side in document.Consolidation.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            foreach (var line in ConsolidationLines(side.Key, side.Value))
                yield return line;
    }

    private static IEnumerable<string> Calls(string side, string phase, IReadOnlyList<RuleCall> calls)
    {
        foreach (var call in calls)
        {
            yield return Pipe("rule", side, phase, call.Rule);
            if (When("when", call.When) is { } when)
                yield return when;
            foreach (var pair in call.Strings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                yield return Pipe("arg", pair.Key, pair.Value);
            foreach (var pair in call.Lists.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                yield return Delimited.Concat('|', [Delimited.Escape("list"), Delimited.Escape(pair.Key), Delimited.Join(',', pair.Value)]);
            foreach (var pair in call.Maps.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                yield return MapLine(pair.Key, pair.Value);
        }
    }

    private static IEnumerable<string> PathLines(PathDeclaration path)
    {
        yield return Pipe("path", path.Id, path.Route.Rule, path.Route.Field, path.Route.EqualsValue);
        foreach (var pass in path.Passes)
        {
            yield return Pipe("pass", pass.Id, pass.Pass, pass.Many, Text(pass.MaxGroupSize));
            yield return Head("key", Delimited.Join(',', pass.Key));
            yield return Head("block", Delimited.Join(',', pass.Block));
            yield return Head("conserve", Delimited.Join(',', pass.Conserve));
        }

        if (path.Diff is not { } diff)
            yield break;
        foreach (var pair in diff.Fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var tolerance = pair.Value.Tolerance;
            yield return tolerance.Days is { } days
                ? Pipe("diff", pair.Key, tolerance.Kind, Money(tolerance.Absolute), Money(tolerance.Relative), tolerance.Attributed, pair.Value.Attributed, Text(days))
                : Pipe("diff", pair.Key, tolerance.Kind, Money(tolerance.Absolute), Money(tolerance.Relative), tolerance.Attributed, pair.Value.Attributed);
            if (When("tolerance-when", tolerance.When) is { } toleranceWhen)
                yield return toleranceWhen;
            if (When("applicable", pair.Value.ApplicableWhen) is { } applicable)
                yield return applicable;
        }
    }

    private static IEnumerable<string> ConsolidationLines(string side, ConsolidationSide plan)
    {
        yield return Pipe("consolidation", side);
        if (plan.Lifecycle is { } lifecycle)
            yield return Pipe("life", lifecycle.ActionField, lifecycle.TargetField, lifecycle.ScopeField, lifecycle.CorrPointsAt);
        foreach (var package in plan.Packages)
        {
            yield return Delimited.Concat('|', [Delimited.Escape("package"), Delimited.Escape(package.LinkField), Delimited.Escape(package.RoleField), Delimited.Join(',', package.RequiredRoles)]);
            foreach (var aggregate in package.Aggregates.OrderBy(aggregate => aggregate.Field, StringComparer.Ordinal))
                yield return Pipe("agg", aggregate.Field, aggregate.Kind.ToString(), aggregate.Role, aggregate.OtherRole);
        }
    }

    private static string? When(string label, WhenClause? when) =>
        when is null
            ? null
            : Delimited.Concat('|', [
                Delimited.Escape(label),
                Delimited.Escape(when.Field),
                Delimited.Escape(when.EqualsValue ?? ""),
                Delimited.Escape(when.NotEquals ?? ""),
                when.In is { } values ? Delimited.Join(',', values.OrderBy(value => value, StringComparer.Ordinal)) : "",
            ]);

    private static string MapLine(string name, IReadOnlyDictionary<string, string> map) =>
        Delimited.Concat('|', [Delimited.Escape("map"), Delimited.Escape(name), .. map.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => Delimited.Join('=', [entry.Key, entry.Value]))]);

    private static string Pipe(params string?[] parts) => Delimited.Join('|', parts.Select(part => part ?? ""));

    private static string Head(string label, string joined) =>
        Delimited.Concat('|', [Delimited.Escape(label), joined]);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Money(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
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
        // Rewriting a field this rule also reads is allowed. Two writers that do not read the field conflict unless their when clauses exclude each other.
        var writeErrors = rule.Writes.Where(write =>
            WrittenSites.TryGetValue(write.Name, out var previous)
            && !rule.Reads.Any(read => read.Name == write.Name)
            && !WhenClause.MutuallyExclusive(previous.When, call.When)).Select(write =>
            new ProfileError("AmbiguousWrite", $"{side} field '{write.Name}' is written by '{WrittenSites[write.Name].Rule}' and '{call.Rule}'."));
        var unknown = ((FieldId[])[.. rule.Reads, .. rule.Writes]).Where(field => schema.TryGet(field.Name, out _) is false).Select(field =>
            new ProfileError("UnknownField", $"{side}.{call.Rule} uses undeclared field '{field.Name}'."));
        ProfileError[] errors = [.. readErrors, .. writeErrors, .. unknown];
        var written = rule.Writes.Aggregate(WrittenSites, (sites, write) => sites.SetItem(write.Name, new WriteSite(call.Rule, call.When)));
        return errors.Length == 0
            ? this with { WrittenSites = written, Rules = Rules.Add(new CompiledRule(rule, call.When)) }
            : this with { Errors = Errors.AddRange(errors) };
    }

    private SideCompilation Fail(string code, string message) => this with { Errors = Errors.Add(new ProfileError(code, message)) };

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
