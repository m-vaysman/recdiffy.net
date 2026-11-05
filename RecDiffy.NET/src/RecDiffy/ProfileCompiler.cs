using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecDiffy.Engine;

internal static class ProfileCompiler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry)
    {
        using var json = JsonDocument.Parse(document.Json);
        var root = json.RootElement;
        var format = root.GetProperty("formatVersion").GetInt32();
        if (format != 1)
            throw new ProfileException("UnsupportedFormat", $"formatVersion {format} is not supported.");

        var midpoint = root.GetProperty("rounding").GetProperty("midpoint").GetString() switch
        {
            "ToEven" => MidpointMode.ToEven,
            "AwayFromZero" => MidpointMode.AwayFromZero,
            var other => throw new ProfileException("MissingParameter", $"Unknown midpoint '{other}'."),
        };

        var sources = root.GetProperty("sources");
        var fields = ReadFields(root);
        var fieldNames = fields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var paths = ReadPaths(root, fieldNames);
        var (leftUses, leftCols) = ReadRules(root, "left", registry, fieldNames);
        var (rightUses, rightCols) = ReadRules(root, "right", registry, fieldNames);
        ValidateRules(leftUses, "left");
        ValidateRules(rightUses, "right");
        ValidatePaths(paths, fieldNames, fields);

        var model = new ProfileModel
        {
            ProfileId = root.GetProperty("profileId").GetString() ?? "",
            Version = root.GetProperty("version").GetString() ?? "",
            Midpoint = midpoint,
            LeftSource = "left",
            RightSource = "right",
            LeftKey = sources.GetProperty("left").GetProperty("rowKey").GetString() ?? "",
            RightKey = sources.GetProperty("right").GetProperty("rowKey").GetString() ?? "",
            Fields = fields,
            Paths = paths,
            MappedColumnsLeft = leftCols,
            MappedColumnsRight = rightCols,
            RawRetention = root.TryGetProperty("rawRetention", out var raw) ? raw.GetString() ?? "OnDifference" : "OnDifference",
        };

        return new CompiledProfile(
            model.ProfileId,
            model.Version,
            Hash(document.Json),
            model,
            leftUses,
            rightUses,
            ReadLife(root, "left", registry),
            ReadLife(root, "right", registry),
            ReadPackages(root, "left", registry),
            ReadPackages(root, "right", registry));
    }

    private static string Hash(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(doc.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static List<FieldModel> ReadFields(JsonElement root)
    {
        var fields = new List<FieldModel>();
        foreach (var field in root.GetProperty("fields").EnumerateArray())
        {
            var type = field.GetProperty("type").GetString() switch
            {
                "Decimal" => FieldType.Decimal,
                "Date" => FieldType.Date,
                "Symbol" => FieldType.Symbol,
                "Identifier" => FieldType.Identifier,
                "Text" => FieldType.Text,
                var other => throw new ProfileException("MissingParameter", $"Unknown field type '{other}'."),
            };
            var role = field.GetProperty("role").GetString() switch
            {
                "Key" => FieldRole.Key,
                "Economic" => FieldRole.Economic,
                "Identity" => FieldRole.Identity,
                "Lifecycle" => FieldRole.Lifecycle,
                "Package" => FieldRole.Package,
                "Text" => FieldRole.Descriptive,
                var other => throw new ProfileException("MissingParameter", $"Unknown role '{other}'."),
            };
            fields.Add(new FieldModel
            {
                Name = field.GetProperty("name").GetString() ?? "",
                Type = type,
                Scale = field.TryGetProperty("scale", out var scale) ? scale.GetInt32() : 0,
                Role = role,
            });
        }
        return fields;
    }

    private static List<PathModel> ReadPaths(JsonElement root, HashSet<string> fields)
    {
        var paths = new List<PathModel>();
        foreach (var path in root.GetProperty("paths").EnumerateArray())
        {
            var route = path.GetProperty("route");
            var routeRule = route.GetProperty("rule").GetString();
            var passes = new List<PassModel>();
            foreach (var pass in path.GetProperty("passes").EnumerateArray())
            {
                var limits = new Limits();
                if (pass.TryGetProperty("limits", out var limitNode))
                    limits = ReadLimits(limitNode);
                else if (root.TryGetProperty("limits", out var global))
                    limits = ReadLimits(global);
                if (limits.MaxGroupSize > 16)
                    throw new ProfileException("Limit", "MaxGroupSize cannot exceed 16.");
                passes.Add(new PassModel
                {
                    Id = pass.GetProperty("id").GetString() ?? "",
                    Kind = pass.GetProperty("pass").GetString() ?? "",
                    Key = Strings(pass, "key"),
                    Block = Strings(pass, "block"),
                    Conserve = Strings(pass, "conserve"),
                    Many = pass.TryGetProperty("many", out var many) ? many.GetString() ?? "left" : "left",
                    Veto = pass.TryGetProperty("veto", out var veto) ? veto.GetString() : null,
                    Limits = limits,
                });
            }

            var diffs = new List<DiffFieldModel>();
            if (path.TryGetProperty("diff", out var diff) && diff.TryGetProperty("fields", out var diffFields))
            {
                foreach (var field in diffFields.EnumerateObject())
                {
                    var spec = field.Value;
                    WhenClause? tolWhen = null;
                    decimal? absolute = null;
                    decimal? relative = null;
                    string? attributed = null;
                    if (spec.TryGetProperty("tolerance", out var tolerance))
                    {
                        if (tolerance.TryGetProperty("absolute", out var abs))
                            absolute = abs.ValueKind == JsonValueKind.String
                                ? decimal.Parse(abs.GetString()!, CultureInfo.InvariantCulture)
                                : abs.GetDecimal();
                        if (tolerance.TryGetProperty("relative", out var rel))
                            relative = rel.GetDecimal();
                        if (tolerance.TryGetProperty("attributed", out var attr))
                            attributed = attr.GetString();
                        if (tolerance.TryGetProperty("when", out var when))
                            tolWhen = ReadWhen(when);
                    }
                    diffs.Add(new DiffFieldModel
                    {
                        Field = field.Name,
                        Absolute = absolute,
                        Relative = relative,
                        Attributed = spec.TryGetProperty("attributed", out var top) ? top.GetString() : attributed,
                        ToleranceWhen = tolWhen,
                        ApplicableWhen = spec.TryGetProperty("applicableWhen", out var apply) ? ReadWhen(apply) : null,
                    });
                }
            }

            paths.Add(new PathModel
            {
                Id = path.GetProperty("id").GetString() ?? "",
                Route = routeRule ?? "",
                RouteField = route.TryGetProperty("field", out var rf) ? rf.GetString() : null,
                RouteEquals = route.TryGetProperty("equals", out var eq) ? eq.GetString() : null,
                Passes = passes,
                Diffs = diffs,
            });
        }

        if (paths.Count > 1 && paths.Any(p => p.Route == "route.all"))
            throw new ProfileException("RouteOverlap", "route.all overlaps another path.");
        return paths;
    }

    private static Limits ReadLimits(JsonElement node) => new()
    {
        MaxGroupSize = node.TryGetProperty("maxGroupSize", out var g) ? g.GetInt32() : 8,
        MaxBlockItems = node.TryGetProperty("maxBlockItems", out var b) ? b.GetInt32() : 24,
        MaxTargetsPerComponent = node.TryGetProperty("maxTargetsPerComponent", out var t) ? t.GetInt32() : 8,
        MaxNodesPerTarget = node.TryGetProperty("maxNodesPerTarget", out var n) ? n.GetInt32() : 250_000,
        StopAfterSolutions = node.TryGetProperty("stopAfterSolutions", out var s) ? s.GetInt32() : 2,
    };

    private static (List<RuleUse> Uses, List<string> Columns) ReadRules(JsonElement root, string side, RuleRegistry registry, HashSet<string> fields)
    {
        var uses = new List<RuleUse>();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("normalization", out var normalization) || !normalization.TryGetProperty(side, out var sideNode))
            return (uses, []);
        foreach (var phaseName in new[] { "identify", "parse", "derive", "validate" })
        {
            if (!sideNode.TryGetProperty(phaseName, out var list))
                continue;
            var phase = phaseName switch
            {
                "identify" => RulePhase.Identify,
                "parse" => RulePhase.Parse,
                "derive" => RulePhase.Derive,
                _ => RulePhase.Validate,
            };
            foreach (var element in list.EnumerateArray())
            {
                var ruleId = element.GetProperty("rule").GetString() ?? "";
                if (!registry.Normalization.TryGetValue(ruleId, out var factory))
                    throw new ProfileException("UnknownRule", $"Unknown rule '{ruleId}'.");
                if (factory.Phase != phase)
                    throw new ProfileException("WrongPhase", $"Rule '{ruleId}' is not a {phaseName} rule.");
                var args = Arguments(element);
                var rule = factory.Bind(args);
                foreach (var read in rule.Reads)
                {
                    if (!fields.Contains(read.Name))
                        throw new ProfileException("UnknownField", $"Rule '{ruleId}' reads unknown field '{read.Name}'.");
                }
                foreach (var write in rule.Writes)
                {
                    if (!fields.Contains(write.Name))
                        throw new ProfileException("UnknownField", $"Rule '{ruleId}' writes unknown field '{write.Name}'.");
                }
                CollectColumns(element, columns);
                uses.Add(new RuleUse { Rule = rule, When = element.TryGetProperty("when", out var when) ? ReadWhen(when) : null });
            }
        }
        return (uses, columns.ToList());
    }

    private static void ValidateRules(IReadOnlyList<RuleUse> uses, string side)
    {
        var written = new Dictionary<string, RuleUse>(StringComparer.Ordinal);
        foreach (var use in uses)
        {
            foreach (var read in use.Rule.Reads)
            {
                if (!written.ContainsKey(read.Name))
                    throw new ProfileException("FieldReadBeforeWrite", $"{side}: '{use.Rule.RuleId}' reads '{read.Name}' before it is written.");
            }
            foreach (var write in use.Rule.Writes)
            {
                if (!written.TryGetValue(write.Name, out var earlier))
                {
                    written[write.Name] = use;
                    continue;
                }
                var readsIt = use.Rule.Reads.Any(r => r.Name == write.Name);
                if (readsIt || Exclusive(earlier.When, use.When))
                {
                    written[write.Name] = use;
                    continue;
                }
                throw new ProfileException("AmbiguousWrite", $"{side}: '{use.Rule.RuleId}' and '{earlier.Rule.RuleId}' both write '{write.Name}'.");
            }
        }
    }

    private static bool Exclusive(WhenClause? left, WhenClause? right)
    {
        if (left is null || right is null || left.Field != right.Field)
            return false;
        if (left.EqualsValue is not null && right.EqualsValue is not null)
            return !string.Equals(left.EqualsValue, right.EqualsValue, StringComparison.Ordinal);
        if (left.EqualsValue is not null && right.NotEquals == left.EqualsValue)
            return true;
        if (right.EqualsValue is not null && left.NotEquals == right.EqualsValue)
            return true;
        return false;
    }

    private static void ValidatePaths(IReadOnlyList<PathModel> paths, HashSet<string> fields, IReadOnlyList<FieldModel> models)
    {
        var roles = models.ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var known = path.KindPasses();
            foreach (var pass in path.Passes)
            {
                if (pass.Kind is not ("match.sharedRef" or "match.naturalKey" or "match.groupSum" or "match.reducedKey"))
                    throw new ProfileException("UnknownRule", $"Unknown pass '{pass.Kind}'.");
                foreach (var field in pass.Key.Concat(pass.Block).Concat(pass.Conserve))
                {
                    if (!fields.Contains(field))
                        throw new ProfileException("UnknownField", $"Pass '{pass.Id}' names unknown field '{field}'.");
                    if (roles[field].Type == FieldType.Text)
                        throw new ProfileException("TextKey", $"Pass '{pass.Id}' keys on text field '{field}'.");
                }
            }
            foreach (var diff in path.Diffs)
            {
                if (!fields.Contains(diff.Field))
                    throw new ProfileException("UnknownField", $"Diff names unknown field '{diff.Field}'.");
                var type = roles[diff.Field].Type;
                if (diff.Absolute is not null && type is not FieldType.Decimal)
                    throw new ProfileException("ToleranceType", $"Absolute tolerance is not valid for '{diff.Field}'.");
            }
            _ = known;
        }
    }

    private static LifecycleBinding? ReadLife(JsonElement root, string side, RuleRegistry registry)
    {
        if (!root.TryGetProperty("consolidation", out var consolidation) || !consolidation.TryGetProperty(side, out var node))
            return null;
        if (!node.TryGetProperty("lifecycle", out var life))
            return null;
        var rule = life.GetProperty("rule").GetString() ?? "";
        if (!registry.Lifecycles.TryGetValue(rule, out var factory))
            throw new ProfileException("UnknownRule", $"Unknown lifecycle rule '{rule}'.");
        return factory.Bind(Arguments(life));
    }

    private static List<PackageBinding> ReadPackages(JsonElement root, string side, RuleRegistry registry)
    {
        var result = new List<PackageBinding>();
        if (!root.TryGetProperty("consolidation", out var consolidation) || !consolidation.TryGetProperty(side, out var node))
            return result;
        if (!node.TryGetProperty("packages", out var packages))
            return result;
        foreach (var package in packages.EnumerateArray())
        {
            var rule = package.GetProperty("rule").GetString() ?? "";
            if (!registry.Packages.TryGetValue(rule, out var factory))
                throw new ProfileException("UnknownRule", $"Unknown package rule '{rule}'.");
            result.Add(factory.Bind(Arguments(package), registry));
        }
        return result;
    }

    private static RuleArguments Arguments(JsonElement element)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var maps = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is "rule" or "when")
                continue;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    values[property.Name] = property.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                    values[property.Name] = property.Value.GetRawText();
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    values[property.Name] = property.Value.GetBoolean() ? "true" : "false";
                    break;
                case JsonValueKind.Array:
                    lists[property.Name] = property.Value.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
                    break;
                case JsonValueKind.Object:
                    maps[property.Name] = property.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
                    break;
            }
        }
        return new RuleArguments(values, lists, maps);
    }

    private static WhenClause ReadWhen(JsonElement when) => new()
    {
        Field = when.GetProperty("field").GetString() ?? "",
        EqualsValue = when.TryGetProperty("equals", out var eq) ? eq.GetString() : null,
        NotEquals = when.TryGetProperty("notEquals", out var ne) ? ne.GetString() : null,
        In = when.TryGetProperty("in", out var inn) ? inn.EnumerateArray().Select(i => i.GetString() ?? "").ToArray() : null,
        NotIn = when.TryGetProperty("notIn", out var notIn) ? notIn.EnumerateArray().Select(i => i.GetString() ?? "").ToArray() : null,
    };

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var node) && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray().Select(i => i.GetString() ?? "").ToArray()
            : [];

    private static void CollectColumns(JsonElement element, HashSet<string> columns)
    {
        foreach (var name in new[] { "from", "code", "quantity", "link", "role" })
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                columns.Add(value.GetString() ?? "");
        }
        if (element.TryGetProperty("columns", out var list))
        {
            foreach (var item in list.EnumerateArray())
                columns.Add(item.GetString() ?? "");
        }
    }
}

internal static class PathExtensions
{
    public static string KindPasses(this PathModel path) => path.Id;
}
