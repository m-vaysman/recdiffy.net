using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RecDiffy;

/// <summary>Writes a <see cref="ProfileDocument"/> as the profile JSON that <see cref="ProfileDocument.Parse"/> reads.</summary>
/// <remarks>
/// The output is deterministic: a fixed property order, dictionary keys sorted ordinally, lists kept in document order,
/// <c>\n</c> line endings and invariant number formatting. Policy values are written even when they equal the parser's
/// default (rounding, limits, lifecycle and package field names, decimal scale), so a saved profile never depends on a
/// default. Empty optional structure (empty lists, absent conditions, an exact tolerance) is left out.
/// </remarks>
public static class ProfileWriter
{
    private static readonly string[] Leading = ["from", "to"];

    public static string ToJson(this ProfileDocument document, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options(indented)))
        {
            WriteDocument(writer, document);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static JsonWriterOptions Options(bool indented) => new()
    {
        Indented = indented,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void WriteDocument(Utf8JsonWriter writer, ProfileDocument document)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", document.FormatVersion);
        writer.WriteString("profileId", document.ProfileId);
        writer.WriteString("version", document.Version);
        writer.WriteStartObject("rounding");
        writer.WriteString("midpoint", Midpoint(document.Midpoint));
        writer.WriteEndObject();
        WriteSources(writer, document.SourceKeys);
        WriteFields(writer, document.Fields);
        WriteNormalization(writer, document.Normalization);
        WriteConsolidation(writer, document.Consolidation);
        WritePaths(writer, document.Paths);
        WriteLimits(writer, document.Limits);
        writer.WriteEndObject();
    }

    private static string Midpoint(MidpointMode midpoint) => midpoint switch
    {
        MidpointMode.ToEven => "ToEven",
        MidpointMode.AwayFromZero => "AwayFromZero",
        _ => throw new ArgumentException($"Midpoint '{midpoint}' has no profile name.", nameof(midpoint)),
    };

    private static void WriteSources(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> sources)
    {
        if (sources.Count == 0)
            return;
        writer.WriteStartObject("sources");
        foreach (var (side, rowKey) in Ordered(sources))
        {
            writer.WriteStartObject(side);
            writer.WriteString("rowKey", rowKey);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteFields(Utf8JsonWriter writer, IReadOnlyList<FieldDeclaration> fields)
    {
        writer.WriteStartArray("fields");
        foreach (var field in fields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("type", field.Type.ToString());
            writer.WriteString("role", field.Role.ToString());
            if (field is { Type: FieldType.Decimal } or { Scale: not 0 })
                writer.WriteNumber("scale", field.Scale);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteNormalization(Utf8JsonWriter writer, IReadOnlyDictionary<string, NormalizationSide> normalization)
    {
        if (normalization.Count == 0)
            return;
        writer.WriteStartObject("normalization");
        foreach (var (side, phases) in Ordered(normalization))
        {
            writer.WriteStartObject(side);
            WriteRules(writer, "identify", phases.Identify);
            WriteRules(writer, "parse", phases.Parse);
            WriteRules(writer, "derive", phases.Derive);
            WriteRules(writer, "validate", phases.Validate);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteRules(Utf8JsonWriter writer, string phase, IReadOnlyList<RuleCall> rules)
    {
        if (rules.Count == 0)
            return;
        writer.WriteStartArray(phase);
        foreach (var rule in rules)
            WriteRule(writer, rule);
        writer.WriteEndArray();
    }

    private static void WriteRule(Utf8JsonWriter writer, RuleCall rule)
    {
        writer.WriteStartObject();
        writer.WriteString("rule", rule.Rule);
        foreach (var name in ArgumentNames(rule))
            WriteArgument(writer, rule, name);
        if (rule.When is { } when)
            WriteWhen(writer, "when", when);
        writer.WriteEndObject();
    }

    // from and to lead because every reader looks for them first; the rest follow in ordinal order.
    private static IReadOnlyList<string> ArgumentNames(RuleCall rule)
    {
        string[] names = [.. rule.Strings.Keys, .. rule.Lists.Keys, .. rule.Maps.Keys];
        var clash = names
            .GroupBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1 || group.Key is "rule" or "when");
        return clash is null
            ? [.. Leading.Where(names.Contains), .. names.Where(name => !Leading.Contains(name)).Order(StringComparer.Ordinal)]
            : throw new ArgumentException($"Rule '{rule.Rule}' argument '{clash.Key}' cannot be written as a distinct JSON property.", nameof(rule));
    }

    private static void WriteArgument(Utf8JsonWriter writer, RuleCall rule, string name)
    {
        if (rule.Strings.TryGetValue(name, out var text))
        {
            writer.WriteString(name, text);
        }
        else if (rule.Lists.TryGetValue(name, out var list))
        {
            WriteStrings(writer, name, list);
        }
        else
        {
            writer.WriteStartObject(name);
            foreach (var (key, value) in Ordered(rule.Maps[name]))
                writer.WriteString(key, value);
            writer.WriteEndObject();
        }
    }

    private static void WriteWhen(Utf8JsonWriter writer, string name, WhenClause when)
    {
        writer.WriteStartObject(name);
        writer.WriteString("field", when.Field);
        if (when.EqualsValue is { } equals)
            writer.WriteString("equals", equals);
        if (when.NotEquals is { } notEquals)
            writer.WriteString("notEquals", notEquals);
        if (when.In is { } values)
            WriteStrings(writer, "in", values);
        writer.WriteEndObject();
    }

    private static void WriteConsolidation(Utf8JsonWriter writer, IReadOnlyDictionary<string, ConsolidationSide> consolidation)
    {
        if (consolidation.Count == 0)
            return;
        writer.WriteStartObject("consolidation");
        foreach (var (side, plan) in Ordered(consolidation))
        {
            writer.WriteStartObject(side);
            if (plan.Lifecycle is { } lifecycle)
                WriteLifecycle(writer, lifecycle);
            if (plan.Packages.Count > 0)
            {
                writer.WriteStartArray("packages");
                foreach (var package in plan.Packages)
                    WritePackage(writer, package);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteLifecycle(Utf8JsonWriter writer, LifecyclePlan lifecycle)
    {
        writer.WriteStartObject("lifecycle");
        writer.WriteString("action", lifecycle.ActionField);
        writer.WriteString("target", lifecycle.TargetField);
        writer.WriteString("scope", lifecycle.ScopeField);
        writer.WriteString("corrTarget", lifecycle.CorrPointsAt);
        writer.WriteEndObject();
    }

    private static void WritePackage(Utf8JsonWriter writer, PackagePlan package)
    {
        writer.WriteStartObject();
        writer.WriteString("link", package.LinkField);
        writer.WriteString("role", package.RoleField);
        if (package.RequiredRoles.Count > 0)
            WriteStrings(writer, "requiredRoles", package.RequiredRoles);
        if (package.Aggregates.Count > 0)
        {
            writer.WriteStartObject("aggregate");
            foreach (var aggregate in package.Aggregates)
                writer.WriteString(aggregate.Field, Aggregator(aggregate));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static string Aggregator(FieldAggregate aggregate) => aggregate switch
    {
        { Kind: AggregateKind.Same } => "Same",
        { Kind: AggregateKind.Sum } => "Sum",
        { Kind: AggregateKind.FromRole } => $"FromRole({aggregate.Role})",
        { Kind: AggregateKind.DifferenceByRole, OtherRole: null } => $"DifferenceByRole({aggregate.Role})",
        { Kind: AggregateKind.DifferenceByRole } => $"DifferenceByRole({aggregate.Role},{aggregate.OtherRole})",
        _ => throw new ArgumentException($"Aggregator '{aggregate.Kind}' on '{aggregate.Field}' has no profile name.", nameof(aggregate)),
    };

    private static void WritePaths(Utf8JsonWriter writer, IReadOnlyList<PathDeclaration> paths)
    {
        writer.WriteStartArray("paths");
        foreach (var path in paths)
            WritePath(writer, path);
        writer.WriteEndArray();
    }

    private static void WritePath(Utf8JsonWriter writer, PathDeclaration path)
    {
        writer.WriteStartObject();
        writer.WriteString("id", path.Id);
        writer.WriteStartObject("route");
        writer.WriteString("rule", path.Route.Rule);
        if (path.Route.Field is { } field)
            writer.WriteString("field", field);
        if (path.Route.EqualsValue is { } equals)
            writer.WriteString("equals", equals);
        writer.WriteEndObject();
        if (path.Passes.Count > 0)
        {
            writer.WriteStartArray("passes");
            foreach (var pass in path.Passes)
                WritePass(writer, pass);
            writer.WriteEndArray();
        }

        if (path.Diff is { } diff)
            WriteDiff(writer, diff);
        writer.WriteEndObject();
    }

    private static void WritePass(Utf8JsonWriter writer, PassDeclaration pass)
    {
        writer.WriteStartObject();
        writer.WriteString("id", pass.Id);
        writer.WriteString("pass", pass.Pass);
        if (pass.Key.Count > 0)
            WriteStrings(writer, "key", pass.Key);
        if (pass.Block.Count > 0)
            WriteStrings(writer, "block", pass.Block);
        if (pass.Conserve.Count > 0)
            WriteStrings(writer, "conserve", pass.Conserve);
        if (pass.Many is { } many)
            writer.WriteString("many", many);
        if (pass.MaxGroupSize is { } size)
            writer.WriteNumber("maxGroupSize", size);
        writer.WriteEndObject();
    }

    private static void WriteDiff(Utf8JsonWriter writer, DiffDeclaration diff)
    {
        writer.WriteStartObject("diff");
        writer.WriteStartObject("fields");
        foreach (var (field, declaration) in Ordered(diff.Fields))
        {
            writer.WriteStartObject(field);
            WriteTolerance(writer, field, declaration.Tolerance);
            if (declaration.Attributed is { } attributed)
                writer.WriteString("attributed", attributed);
            if (declaration.ApplicableWhen is { } applicable)
                WriteWhen(writer, "applicableWhen", applicable);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTolerance(Utf8JsonWriter writer, string field, ToleranceDeclaration tolerance)
    {
        var expected = KindOf(tolerance);
        if (tolerance.Kind != expected)
            throw new ArgumentException($"Field '{field}' tolerance kind '{tolerance.Kind}' does not match its values, which read as '{expected}'.", nameof(tolerance));
        if (tolerance is { Kind: "Exact", When: null })
            return;
        writer.WriteStartObject("tolerance");
        if (tolerance.Absolute is { } absolute)
            writer.WriteNumber("absolute", absolute);
        if (tolerance.Relative is { } relative)
            writer.WriteNumber("relative", relative);
        if (tolerance.Attributed is { } attributed)
            writer.WriteString("attributed", attributed);
        if (tolerance.Days is { } days)
            writer.WriteNumber("days", days);
        if (tolerance.When is { } when)
            WriteWhen(writer, "when", when);
        writer.WriteEndObject();
    }

    // Mirrors how ProfileParser derives the kind, so a written tolerance parses back to the same kind.
    private static string KindOf(ToleranceDeclaration tolerance) => tolerance switch
    {
        { Absolute: not null, Relative: not null } => "AbsoluteOrRelative",
        { Absolute: not null } => "Absolute",
        { Relative: not null } => "Relative",
        { Attributed: not null } => "Attributed",
        { Days: not null } => "Days",
        _ => "Exact",
    };

    private static void WriteLimits(Utf8JsonWriter writer, SearchLimits limits)
    {
        writer.WriteStartObject("limits");
        writer.WriteNumber("maxGroupSize", limits.MaxGroupSize);
        writer.WriteNumber("maxBlockItems", limits.MaxBlockItems);
        writer.WriteNumber("maxTargetsPerComponent", limits.MaxTargetsPerComponent);
        writer.WriteNumber("maxNodesPerTarget", limits.MaxNodesPerTarget);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static IEnumerable<KeyValuePair<string, TValue>> Ordered<TValue>(IReadOnlyDictionary<string, TValue> pairs) =>
        pairs.OrderBy(pair => pair.Key, StringComparer.Ordinal);
}
