using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace RecDiffy;

internal static class ProfileParser
{
    public static ProfileDocument Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ProfileCompileException([new ProfileError("InvalidJson", exception.Message)]);
        }

        using (document)
        {
            var root = document.RootElement;
            var errors = root.ValueKind == JsonValueKind.Object
                ? ImmutableList<ProfileError>.Empty
                : ImmutableList.Create(new ProfileError("InvalidJson", "Profile root must be an object."));
            if (errors.Count > 0)
                throw new ProfileCompileException(errors);

            errors = errors
                .AddRange(Require(root, "formatVersion"))
                .AddRange(Require(root, "profileId"))
                .AddRange(Require(root, "version"))
                .AddRange(root.TryGetProperty("fields", out _) ? [] : [new ProfileError("MissingParameter", "fields is required.")])
                .AddRange(root.TryGetProperty("paths", out _) ? [] : [new ProfileError("MissingParameter", "paths is required.")]);
            if (errors.Count > 0)
                throw new ProfileCompileException(errors);

            return new ProfileDocument
            {
                FormatVersion = root.GetProperty("formatVersion").GetInt32(),
                ProfileId = root.GetProperty("profileId").GetString() ?? "",
                Version = root.GetProperty("version").GetString() ?? "",
                Midpoint = ReadMidpoint(root),
                Fields = ReadFields(root),
                Normalization = ReadNormalization(root),
                Paths = ReadPaths(root),
                SourceKeys = ReadSourceKeys(root),
                Limits = ReadLimits(root),
                Consolidation = ReadConsolidation(root),
            };
        }
    }

    private static IEnumerable<ProfileError> Require(JsonElement element, string name) =>
        element.TryGetProperty(name, out _) ? [] : [new ProfileError("MissingParameter", $"{name} is required.")];

    private static MidpointMode ReadMidpoint(JsonElement root) =>
        root.TryGetProperty("rounding", out var rounding) && rounding.TryGetProperty("midpoint", out var midpoint)
            ? midpoint.GetString() switch
            {
                "AwayFromZero" => MidpointMode.AwayFromZero,
                "ToEven" => MidpointMode.ToEven,
                var other => throw new ProfileCompileException([new ProfileError("InvalidParameter", $"Unknown midpoint '{other}'.")]),
            }
            : MidpointMode.ToEven;

    private static IReadOnlyList<FieldDeclaration> ReadFields(JsonElement root) =>
        [.. root.GetProperty("fields").EnumerateArray().Select(ReadField)];

    private static FieldDeclaration ReadField(JsonElement field) => new(
        field.GetProperty("name").GetString() ?? "",
        ParseEnum<FieldType>(field.GetProperty("type").GetString(), "type"),
        ParseEnum<FieldRole>(field.GetProperty("role").GetString(), "role"),
        field.TryGetProperty("scale", out var scale) ? scale.GetInt32() : 0);

    private static TEnum ParseEnum<TEnum>(string? text, string name) where TEnum : struct =>
        Enum.TryParse<TEnum>(text, out var parsed)
            ? parsed
            : throw new ProfileCompileException([new ProfileError("InvalidParameter", $"Unknown {name} '{text}'.")]);

    private static IReadOnlyDictionary<string, NormalizationSide> ReadNormalization(JsonElement root) =>
        root.TryGetProperty("normalization", out var normalization) && normalization.ValueKind == JsonValueKind.Object
            ? normalization.EnumerateObject().ToImmutableDictionary(
                property => property.Name,
                property => new NormalizationSide(
                    ReadRules(property.Value, "identify"),
                    ReadRules(property.Value, "parse"),
                    ReadRules(property.Value, "derive"),
                    ReadRules(property.Value, "validate")),
                StringComparer.Ordinal)
            : ImmutableDictionary<string, NormalizationSide>.Empty;

    private static IReadOnlyList<RuleCall> ReadRules(JsonElement side, string phase) =>
        side.TryGetProperty(phase, out var rules) && rules.ValueKind == JsonValueKind.Array
            ? [.. rules.EnumerateArray().Select(ReadRule)]
            : [];

    private static RuleCall ReadRule(JsonElement rule)
    {
        var arguments = rule.EnumerateObject().Where(property => property.Name is not ("rule" or "when")).ToArray();
        return new RuleCall(
            rule.GetProperty("rule").GetString() ?? "",
            rule.TryGetProperty("when", out var when) ? ReadWhen(when) : null,
            arguments.Where(property => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                .ToImmutableDictionary(property => property.Name, property => Scalar(property.Value), StringComparer.Ordinal),
            arguments.Where(property => property.Value.ValueKind is JsonValueKind.Array)
                .ToImmutableDictionary(property => property.Name, property => (IReadOnlyList<string>)[.. property.Value.EnumerateArray().Select(item => item.GetString() ?? "")], StringComparer.Ordinal),
            arguments.Where(property => property.Value.ValueKind is JsonValueKind.Object)
                .ToImmutableDictionary(
                    property => property.Name,
                    property => (IReadOnlyDictionary<string, string>)property.Value.EnumerateObject().ToImmutableDictionary(item => item.Name, item => item.Value.GetString() ?? "", StringComparer.Ordinal),
                    StringComparer.Ordinal));
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "",
    };

    private static WhenClause ReadWhen(JsonElement when) => new(
        when.GetProperty("field").GetString() ?? "",
        when.TryGetProperty("equals", out var equals) ? equals.GetString() : null,
        when.TryGetProperty("notEquals", out var notEquals) ? notEquals.GetString() : null,
        when.TryGetProperty("in", out var inn) ? [.. inn.EnumerateArray().Select(item => item.GetString() ?? "")] : null);

    private static IReadOnlyList<PathDeclaration> ReadPaths(JsonElement root) =>
        [.. root.GetProperty("paths").EnumerateArray().Select(ReadPath)];

    private static PathDeclaration ReadPath(JsonElement path) => new(
        path.GetProperty("id").GetString() ?? "",
        new RouteDeclaration(
            path.GetProperty("route").GetProperty("rule").GetString() ?? "",
            Optional(path.GetProperty("route"), "field"),
            Optional(path.GetProperty("route"), "equals")),
        path.TryGetProperty("passes", out var passes) ? [.. passes.EnumerateArray().Select(ReadPass)] : [],
        path.TryGetProperty("diff", out var diff) ? ReadDiff(diff) : null);

    private static PassDeclaration ReadPass(JsonElement pass) => new(
        pass.GetProperty("id").GetString() ?? "",
        pass.GetProperty("pass").GetString() ?? "",
        Strings(pass, "key"),
        Strings(pass, "block"),
        Strings(pass, "conserve"),
        Optional(pass, "many"),
        pass.TryGetProperty("maxGroupSize", out var size) ? size.GetInt32() : null);

    private static DiffDeclaration ReadDiff(JsonElement diff) => new(
        diff.TryGetProperty("fields", out var fields)
            ? fields.EnumerateObject().ToImmutableDictionary(property => property.Name, property => ReadFieldDiff(property.Value), StringComparer.Ordinal)
            : ImmutableDictionary<string, FieldDiffDeclaration>.Empty);

    private static FieldDiffDeclaration ReadFieldDiff(JsonElement field) => new(
        field.TryGetProperty("tolerance", out var tolerance) ? ReadTolerance(tolerance) : new ToleranceDeclaration("Exact", null, null, null, null),
        Optional(field, "attributed"),
        field.TryGetProperty("applicableWhen", out var when) ? ReadWhen(when) : null);

    private static ToleranceDeclaration ReadTolerance(JsonElement tolerance)
    {
        var absolute = DecimalOrNull(tolerance, "absolute");
        var relative = DecimalOrNull(tolerance, "relative");
        var attributed = Optional(tolerance, "attributed");
        var kind = (absolute, relative, attributed, tolerance.TryGetProperty("days", out _)) switch
        {
            (not null, not null, _, _) => "AbsoluteOrRelative",
            (not null, _, _, _) => "Absolute",
            (_, not null, _, _) => "Relative",
            (_, _, not null, _) => "Attributed",
            (_, _, _, true) => "Days",
            _ => "Exact",
        };
        return new ToleranceDeclaration(kind, absolute, relative, attributed, tolerance.TryGetProperty("when", out var when) ? ReadWhen(when) : null);
    }

    private static decimal? DecimalOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture),
                JsonValueKind.Number => value.GetDecimal(),
                _ => null,
            }
            : null;

    private static IReadOnlyDictionary<string, string> ReadSourceKeys(JsonElement root) =>
        root.TryGetProperty("sources", out var sources) && sources.ValueKind is JsonValueKind.Object
            ? sources.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.TryGetProperty("rowKey", out var key) ? key.GetString() ?? "" : "",
                StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static SearchLimits ReadLimits(JsonElement root) =>
        root.TryGetProperty("limits", out var limits)
            ? new SearchLimits(
                IntOr(limits, "maxGroupSize", SearchLimits.Default.MaxGroupSize),
                IntOr(limits, "maxBlockItems", SearchLimits.Default.MaxBlockItems),
                IntOr(limits, "maxTargetsPerComponent", SearchLimits.Default.MaxTargetsPerComponent),
                IntOr(limits, "maxNodesPerTarget", SearchLimits.Default.MaxNodesPerTarget))
            : SearchLimits.Default;

    private static int IntOr(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(item => item.GetString() ?? "")]
            : [];

    private static IReadOnlyDictionary<string, ConsolidationSide> ReadConsolidation(JsonElement root) =>
        root.TryGetProperty("consolidation", out var node) && node.ValueKind == JsonValueKind.Object
            ? node.EnumerateObject().ToImmutableDictionary(property => property.Name, property => ReadConsolidationSide(property.Value), StringComparer.Ordinal)
            : ImmutableDictionary<string, ConsolidationSide>.Empty;

    private static ConsolidationSide ReadConsolidationSide(JsonElement side) => new(
        side.TryGetProperty("lifecycle", out var lifecycle)
            ? new LifecyclePlan(
                Optional(lifecycle, "action") ?? "EventAction",
                Optional(lifecycle, "target") ?? "EventTarget",
                Optional(lifecycle, "scope") ?? "Counterparty",
                Optional(lifecycle, "corrTarget") ?? Optional(lifecycle, "corrPointsAt") ?? "CANC")
            : null,
        side.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array
            ? [.. packages.EnumerateArray().Select(ReadPackage)]
            : []);

    private static PackagePlan ReadPackage(JsonElement package)
    {
        var aggregate = package.TryGetProperty("aggregate", out var fields) && fields.ValueKind == JsonValueKind.Object
            ? fields.EnumerateObject().Select(property => ReadAggregate(property.Name, property.Value.GetString() ?? "")).ToArray()
            : [];
        return new PackagePlan(
            Optional(package, "link") ?? "Parent_Link_ID",
            Optional(package, "role") ?? "Leg_Structure",
            Strings(package, "requiredRoles"),
            aggregate);
    }

    private static FieldAggregate ReadAggregate(string field, string text) => text switch
    {
        "Same" => new FieldAggregate(field, AggregateKind.Same, null, null),
        "Sum" => new FieldAggregate(field, AggregateKind.Sum, null, null),
        _ when text.StartsWith("FromRole(", StringComparison.Ordinal) && text.EndsWith(')') =>
            new FieldAggregate(field, AggregateKind.FromRole, text[9..^1], null),
        _ when text.StartsWith("DifferenceByRole(", StringComparison.Ordinal) && text.EndsWith(')') => Difference(field, text[17..^1]),
        _ => throw new ProfileCompileException([new ProfileError("InvalidParameter", $"Unknown aggregator '{text}'.")]),
    };

    private static FieldAggregate Difference(string field, string roles)
    {
        var parts = roles.Split(',');
        return new FieldAggregate(field, AggregateKind.DifferenceByRole, parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private static string? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
