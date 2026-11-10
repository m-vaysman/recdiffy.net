using System.Globalization;
using static RecDiffy.ProfileProblemCodes;

namespace RecDiffy;

// The structural checks a builder runs before any registry is involved: names that must be declared, and declarations
// that must be unique. Rule and parameter checks belong to the compiler, which Build(registry) runs afterwards.
internal static class ProfileValidation
{
    public static ProfileCheckOutcome Check(ProfileDocument document)
    {
        var declared = document.Fields
            .GroupBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        ProfileProblem[] problems =
        [
            .. Duplicates(document.Fields),
            .. document.Normalization.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => Rules(pair.Key, pair.Value, declared)),
            .. document.Consolidation.OrderBy(pair => pair.Key, StringComparer.Ordinal).SelectMany(pair => Consolidation(pair.Key, pair.Value, declared)),
            .. Paths(document.Paths, declared),
        ];
        return new ProfileCheckOutcome(document, problems);
    }

    private static IEnumerable<ProfileProblem> Duplicates(IReadOnlyList<FieldDeclaration> fields) =>
        fields
            .Select((field, index) => (field.Name, Index: index))
            .GroupBy(pair => pair.Name, StringComparer.Ordinal)
            .SelectMany(group => group.Skip(1))
            .Select(pair => new ProfileProblem(DuplicateField, $"Field '{pair.Name}' is declared more than once.", $"fields[{Text(pair.Index)}]"));

    internal static IReadOnlyList<(string Phase, IReadOnlyList<RuleCall> Calls)> Phases(NormalizationSide side) =>
        [("identify", side.Identify), ("parse", side.Parse), ("derive", side.Derive), ("validate", side.Validate)];

    private static IEnumerable<ProfileProblem> Rules(string side, NormalizationSide normalization, IReadOnlyDictionary<string, FieldDeclaration> declared) =>
        Phases(normalization).SelectMany(phase => phase.Calls.SelectMany((call, index) => Rule($"normalization.{side}.{phase.Phase}[{Text(index)}]", call, declared)));

    private static IReadOnlyList<(string Part, IReadOnlyList<string> Fields)> Parts(PassDeclaration pass) =>
        [("key", pass.Key), ("block", pass.Block), ("conserve", pass.Conserve)];

    private static IEnumerable<ProfileProblem> Rule(string location, RuleCall call, IReadOnlyDictionary<string, FieldDeclaration> declared)
    {
        var target = call.Strings.TryGetValue("to", out var to) && !declared.ContainsKey(to)
            ? [new ProfileProblem(UndeclaredRuleTarget, $"Rule '{call.Rule}' writes '{to}', which is not a declared field.", $"{location}.to")]
            : Array.Empty<ProfileProblem>();
        var formatBy = call.Strings.TryGetValue("formatBy", out var field) && !declared.ContainsKey(field)
            ? [new ProfileProblem(UndeclaredField, $"Rule '{call.Rule}' takes its date format by '{field}', which is not a declared field.", $"{location}.formatBy")]
            : Array.Empty<ProfileProblem>();
        var required = call.Rule is "required.present" && call.Lists.TryGetValue("fields", out var fields)
            ? fields.Select((name, index) => (name, index)).Where(pair => !declared.ContainsKey(pair.name)).Select(pair =>
                new ProfileProblem(UndeclaredField, $"Rule '{call.Rule}' requires '{pair.name}', which is not a declared field.", $"{location}.fields[{Text(pair.index)}]"))
            : [];
        return [.. target, .. formatBy, .. required];
    }

    private static IEnumerable<ProfileProblem> Consolidation(string side, ConsolidationSide plan, IReadOnlyDictionary<string, FieldDeclaration> declared)
    {
        var lifecycle = plan.Lifecycle is { } life
            ? Undeclared($"consolidation.{side}.lifecycle", [("action", life.ActionField), ("target", life.TargetField), ("scope", life.ScopeField)], declared, "Lifecycle")
            : [];
        var packages = plan.Packages.SelectMany((package, index) =>
            Undeclared(
                $"consolidation.{side}.packages[{Text(index)}]",
                [("link", package.LinkField), ("role", package.RoleField), .. package.Aggregates.Select(aggregate => ($"aggregate.{aggregate.Field}", aggregate.Field))],
                declared,
                "Package"));
        return [.. lifecycle, .. packages];
    }

    private static IEnumerable<ProfileProblem> Undeclared(string location, IEnumerable<(string Name, string Field)> fields, IReadOnlyDictionary<string, FieldDeclaration> declared, string what) =>
        fields
            .Where(pair => !declared.ContainsKey(pair.Field))
            .Select(pair => new ProfileProblem(UndeclaredField, $"{what} setting '{pair.Name}' names '{pair.Field}', which is not a declared field.", $"{location}.{pair.Name}"));

    private static IEnumerable<ProfileProblem> Paths(IReadOnlyList<PathDeclaration> paths, IReadOnlyDictionary<string, FieldDeclaration> declared)
    {
        var missing = paths.Count == 0
            ? [new ProfileProblem(MissingPath, "A profile needs at least one path.", "paths")]
            : Array.Empty<ProfileProblem>();
        var duplicates = paths
            .GroupBy(path => path.Id, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => new ProfileProblem(DuplicatePath, $"Path '{group.Key}' is declared more than once.", $"paths[{group.Key}]"));
        return [.. missing, .. duplicates, .. paths.SelectMany(path => Path(path, declared))];
    }

    private static IEnumerable<ProfileProblem> Path(PathDeclaration path, IReadOnlyDictionary<string, FieldDeclaration> declared)
    {
        var location = $"paths[{path.Id}]";
        var route = path.Route.Field is { } field && !declared.ContainsKey(field)
            ? [new ProfileProblem(UndeclaredField, $"Path '{path.Id}' routes on '{field}', which is not a declared field.", $"{location}.route.field")]
            : Array.Empty<ProfileProblem>();
        var duplicates = path.Passes
            .GroupBy(pass => pass.Id, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => new ProfileProblem(DuplicatePass, $"Pass '{group.Key}' appears more than once in path '{path.Id}'.", $"{location}.passes[{group.Key}]"));
        var passes = path.Passes.SelectMany(pass =>
            Parts(pass)
                .SelectMany(part => part.Fields
                    .Select((name, index) => (name, index))
                    .Where(pair => !declared.ContainsKey(pair.name))
                    .Select(pair => new ProfileProblem(UndeclaredPassField, $"Pass '{pass.Id}' uses '{pair.name}' in its {part.Part}, which is not a declared field.", $"{location}.passes[{pass.Id}].{part.Part}[{Text(pair.index)}]"))));
        var diff = (path.Diff?.Fields ?? new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => Diff($"{location}.diff[{pair.Key}]", pair.Key, pair.Value, declared));
        return [.. route, .. duplicates, .. passes, .. diff];
    }

    private static IEnumerable<ProfileProblem> Diff(string location, string field, FieldDiffDeclaration diff, IReadOnlyDictionary<string, FieldDeclaration> declared) =>
        declared.TryGetValue(field, out var declaration) switch
        {
            false => [new ProfileProblem(UndeclaredDiffField, $"A tolerance is set on '{field}', which is not a declared field.", location)],
            true when !Fits(declaration.Type, diff.Tolerance.Kind) =>
                [new ProfileProblem(ToleranceMismatch, $"A {diff.Tolerance.Kind} tolerance does not fit '{field}', a {declaration.Type} field.", $"{location}.tolerance")],
            _ => [],
        };

    // The same table the compiler applies, so a profile that builds never fails compile on tolerance type.
    private static bool Fits(FieldType type, string kind) => (type, kind) switch
    {
        (FieldType.Decimal, "Exact" or "Absolute" or "Relative" or "AbsoluteOrRelative" or "Attributed") => true,
        (FieldType.Date, "Exact" or "Days") => true,
        (_, "Exact") => true,
        _ => false,
    };

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record ProfileCheckOutcome(ProfileDocument Document, IReadOnlyList<ProfileProblem> Problems);
