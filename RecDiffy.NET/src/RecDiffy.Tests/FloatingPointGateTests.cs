using System.Reflection;

namespace RecDiffy.Tests;

// #361: A guard that fails the test run when floating-point code reaches a money or quantity path. It scans the source
// of the two engine libraries, line by line, and reflects over both assemblies' members. Everything legitimate must be
// listed, with a reason, in FloatingPointAllowlist.cs; the allowlist itself must stay current or the gate fails.
public class FloatingPointGateTests
{
    private const string MoneyAndQuantityAreDecimalOnly =
        "Money and quantity are decimal only; if this use is legitimate, add it to FloatingPointAllowlist.cs with a reason.";

    // ---- Source gate ---------------------------------------------------------------------------------------------

    [Fact]
    public void No_floating_point_lines_reach_the_libraries_without_an_allowlist_entry()
    {
        var violations = Scan()
            .Where(match => !IsAllowlisted(match.RelativePath, match.TrimmedLine))
            .Select(match => Format(match.RelativePath, match.LineNumber, match.Line))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_allowlisted_line_still_matches_a_real_line()
    {
        var scanned = Scan()
            .Select(match => (match.RelativePath, match.TrimmedLine))
            .ToHashSet();

        var stale = FloatingPointAllowlist.Lines
            .Where(entry => !scanned.Contains((entry.RelativePath, entry.LineText)))
            .Select(entry => $"{entry.RelativePath}: \"{entry.LineText}\"")
            .ToArray();

        Assert.True(stale.Length == 0,
            "The following allowlist entries no longer match a scanned line; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    // ---- Reflection gate ------------------------------------------------------------------------------------------

    [Fact]
    public void No_member_signature_uses_double_float_or_Half_without_an_allowlist_entry()
    {
        Assembly[] assemblies =
        [
            typeof(Reconciler).Assembly,
            typeof(RulePacks.Trading.TradeFields).Assembly,
        ];

        var violations = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => IsCompilerGenerated(type) is false)
            .SelectMany(WalkType)
            .OfType<FloatingPointMemberViolation>()
            .ToArray();

        var allowlisted = new HashSet<string>(FloatingPointAllowlist.Members.Select(entry => entry.Key), StringComparer.Ordinal);

        var offenders = violations
            .Where(violation => allowlisted.Contains(violation.Key) is false)
            .Select(violation => $"{violation.Key} ({(violation.FloatType)})")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These members declare double/float/Half and are not allowlisted:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine + MoneyAndQuantityAreDecimalOnly);
    }

    [Fact]
    public void Every_allowlisted_member_still_exists()
    {
        Assembly[] assemblies =
        [
            typeof(Reconciler).Assembly,
            typeof(RulePacks.Trading.TradeFields).Assembly,
        ];

        var present = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => IsCompilerGenerated(type) is false)
            .SelectMany(WalkType)
            .OfType<FloatingPointMemberViolation>()
            .Select(violation => violation.Key)
            .ToHashSet(StringComparer.Ordinal);

        var stale = FloatingPointAllowlist.Members
            .Select(entry => entry.Key)
            .Where(key => present.Contains(key) is false)
            .ToArray();

        Assert.True(stale.Length == 0,
            "The following allowlisted members no longer exist; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    // ---- Negative tests proving the scanner fires ----------------------------------------------------------------

    [Theory]
    [InlineData("double Amount;")]
    [InlineData("float? Price;")]
    [InlineData("Half h = default;")]
    [InlineData("var x = 1.5;")]
    [InlineData("var y = 2d;")]
    [InlineData("var z = 3f;")]
    [InlineData("(double)quantity")]
    [InlineData("List<double> xs;")]
    [InlineData("Convert.ToDouble(value)")]
    [InlineData("double[] values;")]
    public void Scanner_catches_floating_point_forms(string line) =>
        Assert.True(FloatingPointLineScanner.Matches(line), $"line was not caught: {line}");

    [Theory]
    [InlineData("decimal amount = 1.5m;")]
    [InlineData("// a double here is fine in a comment")]
    [InlineData("var text = \"double\";")]
    [InlineData("value.ToString(\"F2\", CultureInfo.InvariantCulture)")]
    [InlineData("var count = 15;")]
    [InlineData("x.Date.ToString(\"yyyy-MM-dd\")")]
    public void Scanner_ignores_comments_strings_and_decimal_literals(string line) =>
        Assert.False(FloatingPointLineScanner.Matches(line), $"line was wrongly caught: {line}");

    // ---- Internals ------------------------------------------------------------------------------------------------

    private static IEnumerable<SourceMatch> Scan()
    {
        string[] roots =
        [
            Path.Combine(ProfileCorpus.Source, "RecDiffy"),
            Path.Combine(ProfileCorpus.Source, "RecDiffy.RulePacks.Trading"),
        ];

        return
        [
            .. roots
                .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                .Where(file => Segment(file, "bin") is false && Segment(file, "obj") is false)
                .OrderBy(file => file, StringComparer.Ordinal)
                .SelectMany(file =>
                {
                    var relativePath = Path.GetRelativePath(ProfileCorpus.Source, file).Replace('\\', '/');
                    return File
                        .ReadLines(file)
                        .Select((line, index) => (Line: line, Number: index + 1))
                        .Select(match => new SourceMatch(
                            relativePath,
                            match.Number,
                            match.Line,
                            match.Line.Trim()))
                        .Where(match => FloatingPointLineScanner.Matches(match.Line));
                }),
        ];
    }

    private static bool Segment(string file, string name) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(name, StringComparer.Ordinal);

    private static bool IsAllowlisted(string relativePath, string trimmedLine) =>
        FloatingPointAllowlist.Lines.Any(entry =>
            entry.RelativePath == relativePath && entry.LineText == trimmedLine);

    private static string Format(string relativePath, int lineNumber, string line) =>
        $"{relativePath}:{lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}: {line} -> {MoneyAndQuantityAreDecimalOnly}";

    private static bool IsCompilerGenerated(Type type) =>
        type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null
        || type.Name.StartsWith("<", StringComparison.Ordinal);

    private static IEnumerable<FloatingPointMemberViolation> WalkType(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var fields = type.GetFields(flags).Select(field => (
            Key: $"{type.Name}.{field.Name}",
            FieldType: field.FieldType));
        var properties = type.GetProperties(flags).Select(property => (
            Key: $"{type.Name}.{property.Name}",
            FieldType: property.PropertyType));
        var methods = type.GetMethods(flags)
            .Where(method => IsCompilerGeneratedMethod(method) is false)
            .SelectMany(method =>
            {
                var returnType = method.ReturnType;
                var parameters = method.GetParameters();
                return parameters
                    .Select(parameter => (Key: $"{type.Name}.{method.Name}", FieldType: parameter.ParameterType))
                    .Append((Key: $"{type.Name}.{method.Name}", FieldType: returnType));
            });

        return fields
            .Concat(properties)
            .Concat(methods)
            .Select(member => ContainsFloat(member.FieldType, out var floatType)
                ? new FloatingPointMemberViolation(member.Key, floatType)
                : null)
            .OfType<FloatingPointMemberViolation>();
    }

    private static bool IsCompilerGeneratedMethod(MethodInfo method) =>
        method.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;

    private static bool ContainsFloat(Type type, out string floatType)
    {
        floatType = "";
        if (type.IsByRef)
            return ContainsFloat(type.GetElementType()!, out floatType);

        if (type.IsArray)
            return ContainsFloat(type.GetElementType()!, out floatType);

        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition.FullName == typeof(Nullable<>).FullName)
                return ContainsFloat(type.GetGenericArguments()[0], out floatType);

            foreach (Type argument in type.GetGenericArguments())
            {
                if (ContainsFloat(argument, out floatType))
                    return true;
            }

            return false;
        }

        floatType = type.FullName switch
        {
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Half" => "Half",
            _ => "",
        };

        return floatType.Length > 0;
    }

    private readonly record struct SourceMatch(string RelativePath, int LineNumber, string Line, string TrimmedLine);

    private sealed record FloatingPointMemberViolation(string Key, string FloatType);
}