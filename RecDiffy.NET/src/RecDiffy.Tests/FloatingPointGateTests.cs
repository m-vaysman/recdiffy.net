using System.Reflection;

namespace RecDiffy.Tests;

// #361: Fails the test run when floating-point code reaches the money and quantity paths of the two engine libraries.
// Three gates: the source text, the declared signatures, and the IL of every method body. Each has an allowlist with reasons.
public class FloatingPointGateTests
{
    private const string MoneyAndQuantityAreDecimalOnly =
        "Money and quantity are decimal only; if this use is legitimate, add it to FloatingPointAllowlist.cs with a reason.";

    private static readonly Lazy<IReadOnlyList<SourceHit>> SourceHits = new(ScanSource);

    private static readonly Lazy<IReadOnlyList<MemberHit>> MemberHits = new(ScanMembers);

    private static readonly Lazy<IReadOnlyList<FloatingPointFinding>> IlFindings = new(ScanIl);

    private static Assembly[] Libraries =>
    [
        typeof(Reconciler).Assembly,
        typeof(RulePacks.Trading.TradeFields).Assembly,
    ];

    [Fact]
    public void No_floating_point_lines_reach_the_libraries_without_an_allowlist_entry()
    {
        var violations = SourceHits.Value
            .GroupBy(hit => (hit.RelativePath, hit.TrimmedLine))
            .Where(group => group.Count() > AllowedLineCount(group.Key.RelativePath, group.Key.TrimmedLine))
            .SelectMany(group => group)
            .Select(hit => $"{hit.RelativePath}:{hit.Number.ToInvariant()}: {hit.Line}")
            .ToArray();

        Assert.True(violations.Length == 0,
            string.Join(Environment.NewLine, violations) + Environment.NewLine + MoneyAndQuantityAreDecimalOnly);
    }

    [Fact]
    public void Every_allowlisted_line_matches_exactly_as_many_lines_as_its_count()
    {
        var counts = SourceHits.Value
            .GroupBy(hit => (hit.RelativePath, hit.TrimmedLine))
            .ToDictionary(group => group.Key, group => group.Count());

        var stale = FloatingPointAllowlist.Lines
            .Select(entry => (Entry: entry, Actual: counts.GetValueOrDefault((entry.RelativePath, entry.LineText))))
            .Where(item => item.Actual != item.Entry.Count)
            .Select(item => $"{item.Entry.RelativePath}: \"{item.Entry.LineText}\" allows {item.Entry.Count.ToInvariant()} but {item.Actual.ToInvariant()} lines match")
            .ToArray();

        Assert.True(stale.Length == 0,
            "These allowlist line entries do not match the scanned lines; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void No_member_signature_uses_double_float_or_Half_without_an_allowlist_entry()
    {
        var allowed = FloatingPointAllowlist.Members.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var offenders = MemberHits.Value
            .Where(hit => allowed.Contains(hit.Key) is false)
            .Select(hit => $"{hit.Key} ({hit.FloatType})")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These members declare double/float/Half and are not allowlisted:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine + MoneyAndQuantityAreDecimalOnly);
    }

    [Fact]
    public void Every_allowlisted_member_still_exists()
    {
        var present = MemberHits.Value.Select(hit => hit.Key).ToHashSet(StringComparer.Ordinal);

        var stale = FloatingPointAllowlist.Members
            .Select(entry => entry.Key)
            .Where(key => present.Contains(key) is false)
            .ToArray();

        Assert.True(stale.Length == 0,
            "These allowlisted members no longer declare a floating-point type; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void Every_method_body_token_can_be_resolved()
    {
        var unresolved = IlFindings.Value
            .Where(finding => finding.Unresolved)
            .Select(finding => $"{finding.Method}: {finding.What}")
            .ToArray();

        Assert.True(unresolved.Length == 0,
            "The IL gate could not read these tokens, so it cannot vouch for the method:" + Environment.NewLine + string.Join(Environment.NewLine, unresolved));
    }

    [Fact]
    public void No_method_body_uses_floating_point_without_an_allowlist_entry()
    {
        var allowed = FloatingPointAllowlist.IlMethods.Select(entry => entry.Method).ToHashSet(StringComparer.Ordinal);

        var offenders = IlFindings.Value
            .Where(finding => finding.Unresolved is false && allowed.Contains(finding.Method) is false)
            .Select(finding => $"{finding.Method} {finding.What}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These method bodies use floating point and are not allowlisted:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine + MoneyAndQuantityAreDecimalOnly);
    }

    [Fact]
    public void Every_allowlisted_method_body_still_uses_floating_point()
    {
        var present = IlFindings.Value
            .Where(finding => finding.Unresolved is false)
            .Select(finding => finding.Method)
            .ToHashSet(StringComparer.Ordinal);

        var stale = FloatingPointAllowlist.IlMethods
            .Select(entry => entry.Method)
            .Where(method => present.Contains(method) is false)
            .ToArray();

        Assert.True(stale.Length == 0,
            "These allowlisted methods no longer use floating point in their body; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void The_allowlists_have_no_duplicate_entries_and_every_entry_has_a_reason()
    {
        var duplicates = new[]
        {
            Duplicates("line", FloatingPointAllowlist.Lines.Select(entry => $"{entry.RelativePath}: {entry.LineText}")),
            Duplicates("member", FloatingPointAllowlist.Members.Select(entry => entry.Key)),
            Duplicates("method body", FloatingPointAllowlist.IlMethods.Select(entry => entry.Method)),
        }.SelectMany(found => found);

        var withoutReason = FloatingPointAllowlist.Lines.Select(entry => (Name: entry.LineText, entry.Reason))
            .Concat(FloatingPointAllowlist.Members.Select(entry => (Name: entry.Key, entry.Reason)))
            .Concat(FloatingPointAllowlist.IlMethods.Select(entry => (Name: entry.Method, entry.Reason)))
            .Where(entry => string.IsNullOrWhiteSpace(entry.Reason))
            .Select(entry => $"no reason: {entry.Name}");

        var problems = duplicates.Concat(withoutReason).ToArray();

        Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_line_allowlist_count_is_at_least_one()
    {
        var wrong = FloatingPointAllowlist.Lines.Where(entry => entry.Count < 1).Select(entry => entry.LineText).ToArray();

        Assert.True(wrong.Length == 0, string.Join(Environment.NewLine, wrong));
    }

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
    [InlineData("var a = 1e-2;")]
    [InlineData("var b = 1.25e-2;")]
    [InlineData("var c = .5;")]
    [InlineData("var d = 1_000.5;")]
    [InlineData("var e = 1_000d;")]
    [InlineData("var f = 1E3;")]
    [InlineData("var g = 1e+3;")]
    [InlineData("var h = 0.5F;")]
    [InlineData("var i = 10D;")]
    [InlineData("System.Single x;")]
    [InlineData("var p = Single.Parse(s);")]
    [InlineData("Single x = 1;")]
    [InlineData("var q = (Single)x;")]
    [InlineData("List<Single> xs;")]
    [InlineData("var r = System.Double.Parse(s);")]
    [InlineData("var s = (decimal)(double)x;")]
    public void Scanner_catches_floating_point_forms(string line) =>
        Assert.True(FloatingPointLineScanner.Matches(line), $"line was not caught: {line}");

    [Theory]
    [InlineData("decimal amount = 1.5m;")]
    [InlineData("// a double here is fine in a comment")]
    [InlineData("var text = \"double\";")]
    [InlineData("value.ToString(\"F2\", CultureInfo.InvariantCulture)")]
    [InlineData("var count = 15;")]
    [InlineData("x.Date.ToString(\"yyyy-MM-dd\")")]
    [InlineData("var hex = 0x1F;")]
    [InlineData("var hex = 0xFD;")]
    [InlineData("var bits = 0b1010_1010;")]
    [InlineData("var wide = 1_000.5m;")]
    [InlineData("var big = 10UL;")]
    [InlineData("var t = x.5;")]
    [InlineData("var u = v1.2;")]
    [InlineData("var range = items[1..5];")]
    [InlineData("var row = rows.Single();")]
    [InlineData("var row = rows.Single<int>();")]
    [InlineData("var row = rows.Single(x => x > 0);")]
    [InlineData("var row = Enumerable.Single(rows);")]
    [InlineData("var s = \"\"\"a \"double\" b\"\"\";")]
    [InlineData("var s = @\"a \"\"double\"\" b\";")]
    [InlineData("var s = $\"{{double}} {count}\";")]
    [InlineData("var s = $\"{amount:0.00}\";")]
    [InlineData("var c = '\"'; var d = 1.5m;")]
    [InlineData("var c = '\\'';")]
    [InlineData("/* double */ var a = 1m;")]
    public void Scanner_ignores_comments_strings_decimal_and_integer_literals_and_linq(string line) =>
        Assert.False(FloatingPointLineScanner.Matches(line), $"line was wrongly caught: {line}");

    [Theory]
    [InlineData("var s = $\"x={(double)qty}\";")]
    [InlineData("var s = $\"x={qty:F2} {(double)qty}\";")]
    [InlineData("var s = $\"{(flag ? \"a\" : \"b\")}{(float)qty}\";")]
    [InlineData("var s = @\"\\\" + (double)x;")]
    [InlineData("var s = \"\"\"a \" b\"\"\" + (double)x;")]
    [InlineData("var s = $$\"\"\"{{(double)x}}\"\"\";")]
    [InlineData("var s = $\"\"\"{(double)x}\"\"\";")]
    [InlineData("var s = $@\"{(double)x}\";")]
    [InlineData("var s = \"a\" + 1.5;")]
    public void Scanner_scans_code_beside_and_inside_strings(string line) =>
        Assert.True(FloatingPointLineScanner.Matches(line), $"line was not caught: {line}");

    [Fact]
    public void Scanner_ignores_a_multi_line_block_comment()
    {
        var text = string.Join("\n", "var a = 1m;", "/* a double", "   var b = 1.0;", "   float c; */", "var d = 2m;");

        Assert.Empty(FloatingPointLineScanner.Scan(text));
    }

    [Fact]
    public void Scanner_ignores_a_multi_line_raw_string_and_scans_the_code_after_it()
    {
        var text = string.Join("\n", "var s = \"\"\"", "    a double and 1.0", "    \"quoted\" float", "    \"\"\" + (double)x;", "var t = 1m;");

        var found = FloatingPointLineScanner.Scan(text);

        Assert.Equal(new[] { 4 }, found.Select(line => line.Number));
    }

    [Fact]
    public void Scanner_ends_a_raw_string_only_at_a_run_as_long_as_its_opening()
    {
        var text = string.Join("\n", "var s = \"\"\"\"", "    has \"\"\" and double inside", "    \"\"\"\";", "var t = (float)1;");

        var found = FloatingPointLineScanner.Scan(text);

        Assert.Equal(new[] { 4 }, found.Select(line => line.Number));
    }

    [Fact]
    public void Scanner_scans_the_code_in_a_multi_line_interpolated_raw_string()
    {
        var text = string.Join("\n", "var s = $$\"\"\"", "    double {{(double)x}} {y}", "    \"\"\";");

        var found = FloatingPointLineScanner.Scan(text);

        Assert.Equal(new[] { 2 }, found.Select(line => line.Number));
    }

    [Fact]
    public void Scanner_reports_the_original_line_numbers_and_text_with_windows_line_ends()
    {
        var text = "var a = 1m;\r\n// double\r\nvar b = 1.5;\r\nvar c = 2m;\r\n";

        var found = FloatingPointLineScanner.Scan(text);

        Assert.Equal(new[] { new FloatingPointLine(3, "var b = 1.5;") }, found);
    }

    [Fact]
    public void Scanner_keeps_every_newline_when_it_masks()
    {
        var text = "/* a\nb */ var s = \"\"\"\nx\n\"\"\"; // end\n";

        Assert.Equal(text.Count(character => character == '\n'), FloatingPointLineScanner.Mask(text).Count(character => character == '\n'));
        Assert.Equal(text.Length, FloatingPointLineScanner.Mask(text).Length);
    }

    private static int AllowedLineCount(string relativePath, string trimmedLine) =>
        FloatingPointAllowlist.Lines
            .Where(entry => entry.RelativePath == relativePath && entry.LineText == trimmedLine)
            .Select(entry => entry.Count)
            .FirstOrDefault();

    private static IEnumerable<string> Duplicates(string list, IEnumerable<string> keys) =>
        keys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"duplicate {list} entry: {group.Key}");

    private static IReadOnlyList<SourceHit> ScanSource()
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
                .Select(file => (File: file, RelativePath: Path.GetRelativePath(ProfileCorpus.Source, file).Replace('\\', '/')))
                .Where(item => HasSegment(item.RelativePath, "bin") is false && HasSegment(item.RelativePath, "obj") is false)
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .SelectMany(item => FloatingPointLineScanner
                    .Scan(File.ReadAllText(item.File))
                    .Select(line => new SourceHit(item.RelativePath, line.Number, line.Text, line.Text.Trim()))),
        ];
    }

    private static bool HasSegment(string relativePath, string name) =>
        relativePath.Split('/').Contains(name, StringComparer.Ordinal);

    // Every field, method and constructor, compiler-generated ones included; properties are covered by their accessors.
    private static IReadOnlyList<MemberHit> ScanMembers()
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return
        [
            .. Libraries
                .SelectMany(assembly => assembly.GetTypes())
                .SelectMany(type => type.GetFields(declared)
                    .Select(field => (Key: FloatingPointTypes.FieldKey(field), FloatType: FloatingPointTypes.Find(field.FieldType)))
                    .Concat(type.GetMethods(declared).Cast<MethodBase>().Concat(type.GetConstructors(declared))
                        .Select(method => (Key: FloatingPointTypes.MethodKey(method), FloatType: FloatingPointTypes.FirstOf(
                            [(method as MethodInfo)?.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)])))))
                .Where(member => member.FloatType is not null)
                .Select(member => new MemberHit(member.Key, member.FloatType!)),
        ];
    }

    private static IReadOnlyList<FloatingPointFinding> ScanIl() =>
        [.. Libraries.SelectMany(FloatingPointIlScanner.Scan)];

    private readonly record struct SourceHit(string RelativePath, int Number, string Line, string TrimmedLine);

    private readonly record struct MemberHit(string Key, string FloatType);
}
