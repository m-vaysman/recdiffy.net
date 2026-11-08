using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RecDiffy.Tests;

// Every profile JSON in the repository lives as a C# raw string literal. This finds them by reading the source,
// so a profile added anywhere under src joins the round-trip tests without registering itself.
internal static class ProfileCorpus
{
    // Parameterized templates take their holes from method arguments. These are the values their own tests pass.
    private static readonly Dictionary<string, string[]> Arguments = new(StringComparer.Ordinal)
    {
        ["key"] = ["""["Sku"]""", """["Sku", "Quantity"]""", """["Bond,Swap"]""", """["Bond", "Swap"]""", """["Counterparty", "Instrument", "TradeDate", "Side"]""", """["pipe|comma,equals=back\\slash \"quote\" ünï"]""", "[]"],
        ["block"] = ["""["Counterparty", "Instrument", "TradeDate", "Side", "FixedRate"]""", """["Counterparty", "Instrument", "TradeDate", "Side"]"""],
        ["diff"] = ["", """ , "diff": { "fields": { "Quantity": { "tolerance": { "absolute": "999999" } } } } """],
        // RecDiffy.IO.Tests Probe.cs fills these from its call sites. The corpus expands every combination.
        ["fields"] =
        [
            """{ "name": "Id", "type": "Text", "role": "Text" }, { "name": "Name", "type": "Text", "role": "Text" }, { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }""",
            """{ "name": "Id", "type": "Identifier", "role": "Key" }, { "name": "Day", "type": "Date", "role": "Economic" }, { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }""",
            """{ "name": "Unused", "type": "Text", "role": "Text" }""",
        ],
        ["rules"] =
        [
            "\"identify\": [ { \"rule\": \"text.copy\", \"from\": \"Id\", \"to\": \"Id\" }, { \"rule\": \"text.copy\", \"from\": \"Name\", \"to\": \"Name\" } ], \"parse\": [ { \"rule\": \"decimal.parse\", \"from\": \"Amount\", \"to\": \"Amount\" } ]",
            "\"identify\": [ { \"rule\": \"text.copy\", \"from\": \"Column1\", \"to\": \"Id\" }, { \"rule\": \"text.copy\", \"from\": \"Column2\", \"to\": \"Name\" } ], \"parse\": [ { \"rule\": \"decimal.parse\", \"from\": \"Column3\", \"to\": \"Amount\" } ]",
            "\"identify\": [ { \"rule\": \"identifier.map\", \"from\": \"Id\", \"to\": \"Id\", \"scheme\": \"ID\" } ], \"parse\": [ { \"rule\": \"date.parse\", \"from\": \"Day\", \"to\": \"Day\" }, { \"rule\": \"decimal.parse\", \"from\": \"Amount\", \"to\": \"Amount\" } ]",
            "\"identify\": [ { \"rule\": \"identifier.map\", \"from\": \"Column1\", \"to\": \"Id\", \"scheme\": \"ID\" } ], \"parse\": [ { \"rule\": \"date.parse\", \"from\": \"Column2\", \"to\": \"Day\" }, { \"rule\": \"decimal.parse\", \"from\": \"Column3\", \"to\": \"Amount\" } ]",
            "",
        ],
        // CultureProfile.cs fills these from DeclaredCultureTests, StrictDateFormatTests and CultureDecimalRuleTests.
        ["run"] = ["", "\"culture\": \"en-US\",", "\"culture\": \"de-DE\", \"strictDateFormats\": false,"],
        ["left"] = ["", ", \"culture\": \"de-DE\"", ", \"strictDateFormats\": true"],
        ["right"] = ["", ", \"culture\": \"en-US\""],
        ["formats"] = ["\"dd/MM/yyyy\""],
    };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Found = new(Scan);

    public static IReadOnlyDictionary<string, string> Profiles => Found.Value;

    public static string Source => Path.Combine(RepoRoot(), "RecDiffy.NET", "src");

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private static IReadOnlyDictionary<string, string> Scan() =>
        Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Segment(file, "bin") && !Segment(file, "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(InFile)
            .ToDictionary(pair => pair.Id, pair => pair.Json, StringComparer.Ordinal);

    private static bool Segment(string file, string name) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(name, StringComparer.Ordinal);

    private static IEnumerable<(string Id, string Json)> InFile(string file)
    {
        var relative = Path.GetRelativePath(Source, file).Replace('\\', '/');
        var literals = Literals(File.ReadAllText(file)).ToArray();
        var constants = literals
            .Where(literal => literal is { Interpolated: false, Name: not null })
            .GroupBy(literal => literal.Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Body, StringComparer.Ordinal);
        return literals
            .Where(literal => literal.Body.Contains("\"formatVersion\"", StringComparison.Ordinal))
            .SelectMany(literal => Expand(literal, constants)
                .Select((json, variant) => ($"{relative}:{literal.Line.ToString(CultureInfo.InvariantCulture)}{(literal.Interpolated ? $"#{variant.ToString(CultureInfo.InvariantCulture)}" : "")}", json)));
    }

    private static IEnumerable<string> Expand(Literal literal, IReadOnlyDictionary<string, string> constants)
    {
        if (!literal.Interpolated)
            return [literal.Body];
        var holes = Hole.Matches(literal.Body).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToArray();
        return holes.Aggregate(
            (IEnumerable<string>)[literal.Body],
            (bodies, hole) => bodies.SelectMany(body => Values(hole, constants, literal).Select(value => body.Replace("{{" + hole + "}}", value, StringComparison.Ordinal))));
    }

    private static IEnumerable<string> Values(string hole, IReadOnlyDictionary<string, string> constants, Literal literal) =>
        constants.TryGetValue(hole, out var constant) ? [constant]
        : Arguments.TryGetValue(hole, out var values) ? values
        : throw new InvalidOperationException($"Template at line {literal.Line} has hole '{{{{{hole}}}}}' with no value. Add its call-site values to ProfileCorpus.Arguments.");

    // A small lexer for raw string literals: an opening run of quotes either ends its line (multi-line) or closes on the same line.
    private static IEnumerable<Literal> Literals(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            var open = line.IndexOf("\"\"\"", StringComparison.Ordinal);
            if (open < 0)
            {
                index++;
                continue;
            }

            var after = line[(open + 3)..];
            var close = after.IndexOf("\"\"\"", StringComparison.Ordinal);
            if (close >= 0)
            {
                lines[index] = after[(close + 3)..];
                continue;
            }

            var end = Array.FindIndex(lines, index + 1, candidate => candidate.TrimStart().StartsWith("\"\"\"", StringComparison.Ordinal));
            var indent = lines[end][..(lines[end].Length - lines[end].TrimStart().Length)];
            var body = string.Join('\n', lines[(index + 1)..end].Select(content => content.StartsWith(indent, StringComparison.Ordinal) ? content[indent.Length..] : content.TrimStart()));
            var prefix = line[..open];
            yield return new Literal(index + 1, prefix.EndsWith('$'), Declared.Match(prefix) is { Success: true } match ? match.Groups["name"].Value : null, body);
            lines[end] = lines[end].TrimStart()[3..];
            index = end;
        }
    }

    // Plain Regex, not [GeneratedRegex]: the Sonar scanner strips analyzers, source generators included, from test projects.
    private static readonly Regex Hole = new(@"\{\{(?<name>\w+)\}\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private static readonly Regex Declared = new(@"(?<name>\w+)\s*(=>|=)\s*\$*$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private sealed record Literal(int Line, bool Interpolated, string? Name, string Body);
}

// A document's structure as text: every member, lists in order, dictionaries by key. Two documents are structurally
// equal when their shapes are equal. The records hold collections, which record equality compares by reference.
internal static class ProfileShape
{
    public static string Of(ProfileDocument document) => string.Join('\n', Lines(document));

    private static IEnumerable<string> Lines(ProfileDocument document)
    {
        yield return $"formatVersion={document.FormatVersion}";
        yield return $"profileId={document.ProfileId}";
        yield return $"version={document.Version}";
        yield return $"midpoint={document.Midpoint}";
        yield return $"limits={document.Limits.MaxGroupSize},{document.Limits.MaxBlockItems},{document.Limits.MaxTargetsPerComponent},{document.Limits.MaxNodesPerTarget}";
        foreach (var (side, key) in Sorted(document.SourceKeys))
            yield return $"sources[{side}].rowKey={key}";
        foreach (var (field, index) in document.Fields.Select((field, index) => (field, index)))
            yield return $"fields[{index}]={field.Name}|{field.Type}|{field.Role}|{field.Scale}";
        foreach (var (side, phases) in Sorted(document.Normalization))
        {
            yield return $"normalization[{side}]";
            foreach (var line in Rules($"normalization[{side}].identify", phases.Identify)
                .Concat(Rules($"normalization[{side}].parse", phases.Parse))
                .Concat(Rules($"normalization[{side}].derive", phases.Derive))
                .Concat(Rules($"normalization[{side}].validate", phases.Validate)))
                yield return line;
        }

        foreach (var (side, plan) in Sorted(document.Consolidation))
        {
            yield return $"consolidation[{side}]";
            if (plan.Lifecycle is { } life)
                yield return $"consolidation[{side}].lifecycle={life.ActionField}|{life.TargetField}|{life.ScopeField}|{life.CorrPointsAt}";
            foreach (var (package, index) in plan.Packages.Select((package, index) => (package, index)))
            {
                yield return $"consolidation[{side}].packages[{index}]={package.LinkField}|{package.RoleField}|{List(package.RequiredRoles)}";
                foreach (var aggregate in package.Aggregates)
                    yield return $"consolidation[{side}].packages[{index}].aggregate={aggregate.Field}|{aggregate.Kind}|{aggregate.Role ?? "<null>"}|{aggregate.OtherRole ?? "<null>"}";
            }
        }

        foreach (var (path, index) in document.Paths.Select((path, index) => (path, index)))
        {
            yield return $"paths[{index}]={path.Id}|{path.Route.Rule}|{path.Route.Field ?? "<null>"}|{path.Route.EqualsValue ?? "<null>"}|diff={(path.Diff is null ? "<null>" : "present")}";
            foreach (var pass in path.Passes)
                yield return $"paths[{index}].pass={pass.Id}|{pass.Pass}|key={List(pass.Key)}|block={List(pass.Block)}|conserve={List(pass.Conserve)}|many={pass.Many ?? "<null>"}|max={pass.MaxGroupSize?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}";
            foreach (var (field, diff) in Sorted(path.Diff?.Fields ?? new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)))
            {
                var tolerance = diff.Tolerance;
                yield return $"paths[{index}].diff[{field}]={tolerance.Kind}|abs={Number(tolerance.Absolute)}|rel={Number(tolerance.Relative)}|days={tolerance.Days?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}|tolAttr={tolerance.Attributed ?? "<null>"}|tolWhen={When(tolerance.When)}|attr={diff.Attributed ?? "<null>"}|applicable={When(diff.ApplicableWhen)}";
            }
        }
    }

    private static IEnumerable<string> Rules(string prefix, IReadOnlyList<RuleCall> rules) =>
        rules.SelectMany((rule, index) => (IEnumerable<string>)
        [
            $"{prefix}[{index}]={rule.Rule}|when={When(rule.When)}",
            .. Sorted(rule.Strings).Select(pair => $"{prefix}[{index}].string[{pair.Key}]={pair.Value}"),
            .. Sorted(rule.Lists).Select(pair => $"{prefix}[{index}].list[{pair.Key}]={List(pair.Value)}"),
            .. Sorted(rule.Maps).Select(pair => $"{prefix}[{index}].map[{pair.Key}]={string.Join(",", Sorted(pair.Value).Select(entry => $"{entry.Key}=>{entry.Value}"))}"),
        ]);

    private static string When(WhenClause? when) =>
        when is null ? "<null>" : $"{when.Field}|eq={when.EqualsValue ?? "<null>"}|ne={when.NotEquals ?? "<null>"}|in={(when.In is null ? "<null>" : List(when.In))}";

    // Scale is part of the value: 1.20 and 1.2 hash differently.
    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "<null>";

    private static string List(IReadOnlyList<string> values) =>
        new StringBuilder("[").AppendJoin(",", values.Select(value => $"<{value}>")).Append(']').ToString();

    private static IEnumerable<KeyValuePair<string, TValue>> Sorted<TValue>(IReadOnlyDictionary<string, TValue> pairs) =>
        pairs.OrderBy(pair => pair.Key, StringComparer.Ordinal);
}
