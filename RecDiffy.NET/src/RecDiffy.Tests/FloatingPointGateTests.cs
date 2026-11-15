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
            .Select(hit => $"{hit.Key} ({hit.FloatTypes})")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These members declare floating-point types (double, float, Half, NFloat, Complex, vectors) and are not allowlisted:" + Environment.NewLine +
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
            .Where(finding => finding.Kind is FindingKind.Unresolved)
            .Select(finding => $"{finding.Method}: {finding.What}")
            .ToArray();

        Assert.True(unresolved.Length == 0,
            "The IL gate could not read these tokens, so it cannot vouch for the method:" + Environment.NewLine + string.Join(Environment.NewLine, unresolved));
    }

    [Fact]
    public void No_method_body_uses_floating_point_without_an_allowlist_entry()
    {
        var pinned = FloatingPointAllowlist.IlMethods.ToDictionary(entry => entry.Method, StringComparer.Ordinal);

        var offenders = UsesBeyondPins(pinned).Concat(LocalsOutsideTheList(pinned)).ToArray();

        Assert.True(offenders.Length == 0,
            "These method bodies use floating point beyond what the allowlist pins:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine + MoneyAndQuantityAreDecimalOnly);
    }

    [Fact]
    public void Every_allowlisted_method_body_still_uses_exactly_what_it_pins()
    {
        var found = IlFindings.Value
            .Where(finding => finding.Kind is FindingKind.Use or FindingKind.Local)
            .GroupBy(finding => finding.Method, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var problems = FloatingPointAllowlist.IlMethods
            .SelectMany(entry => found.TryGetValue(entry.Method, out var findings)
                ? entry.Uses
                    .Select(pin => (Pin: pin, Actual: findings.Count(finding => finding.Kind is FindingKind.Use && finding.What == pin.What)))
                    .Where(item => item.Actual != item.Pin.Count)
                    .Select(item => $"{entry.Method} {item.Pin.What}: pinned {item.Pin.Count.ToInvariant()}, found {item.Actual.ToInvariant()}")
                : [$"{entry.Method}: no floating-point use in the body any more; remove the entry"])
            .ToArray();

        Assert.True(problems.Length == 0,
            "These allowlist pins no longer match the method bodies; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void No_method_calls_a_conversion_door_unless_it_is_an_allowed_caller()
    {
        var allowed = FloatingPointAllowlist.ConversionDoors
            .SelectMany(door => door.Callers.Select(caller => (door.Method, Caller: caller.Method)))
            .ToHashSet();

        var offenders = IlFindings.Value
            .Where(finding => finding.Kind is FindingKind.Door)
            .Select(finding => (Finding: finding, Door: FloatingPointAllowlist.ConversionDoors.First(door => finding.What.EndsWith(" " + door.Method, StringComparison.Ordinal)).Method))
            .Where(item => allowed.Contains((item.Door, item.Finding.Method)) is false)
            .Select(item => $"{item.Finding.Method} {item.Finding.What}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These methods call a conversion door (it turns a boxed double or float into a decimal) and are not listed as allowed callers:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders) + Environment.NewLine + MoneyAndQuantityAreDecimalOnly);
    }

    [Fact]
    public void Every_conversion_door_exists_and_every_allowed_caller_still_calls_it()
    {
        var keys = FloatingPointIlScanner.MethodKeys(Libraries.SelectMany(assembly => assembly.GetTypes()));

        var calls = IlFindings.Value
            .Where(finding => finding.Kind is FindingKind.Door)
            .Select(finding => (finding.Method, finding.What))
            .ToArray();

        var problems = FloatingPointAllowlist.ConversionDoors
            .SelectMany(door => (keys.Contains(door.Method) ? [] : new[] { $"door does not exist: {door.Method}" })
                .Concat(door.Callers
                    .Where(caller => calls.Any(call => call.Method == caller.Method && call.What.EndsWith(" " + door.Method, StringComparison.Ordinal)) is false)
                    .Select(caller => $"{caller.Method} no longer calls {door.Method}")))
            .ToArray();

        Assert.True(problems.Length == 0,
            "These conversion door entries are stale; remove or update them:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_allowlists_have_no_duplicate_entries_and_every_entry_has_a_reason()
    {
        var duplicates = new[]
        {
            Duplicates("line", FloatingPointAllowlist.Lines.Select(entry => $"{entry.RelativePath}: {entry.LineText}")),
            Duplicates("member", FloatingPointAllowlist.Members.Select(entry => entry.Key)),
            Duplicates("method body", FloatingPointAllowlist.IlMethods.Select(entry => entry.Method)),
            Duplicates("pinned use", FloatingPointAllowlist.IlMethods.SelectMany(entry => entry.Uses.Select(use => $"{entry.Method} {use.What}"))),
            Duplicates("conversion door", FloatingPointAllowlist.ConversionDoors.Select(door => door.Method)),
            Duplicates("door caller", FloatingPointAllowlist.ConversionDoors.SelectMany(door => door.Callers.Select(caller => $"{door.Method} <- {caller.Method}"))),
        }.SelectMany(found => found);

        var withoutReason = FloatingPointAllowlist.Lines.Select(entry => (Name: entry.LineText, entry.Reason))
            .Concat(FloatingPointAllowlist.Members.Select(entry => (Name: entry.Key, entry.Reason)))
            .Concat(FloatingPointAllowlist.IlMethods.Select(entry => (Name: entry.Method, entry.Reason)))
            .Concat(FloatingPointAllowlist.ConversionDoors.Select(door => (Name: door.Method, door.Reason)))
            .Concat(FloatingPointAllowlist.ConversionDoors.SelectMany(door => door.Callers.Select(caller => (Name: caller.Method, caller.Reason))))
            .Where(entry => string.IsNullOrWhiteSpace(entry.Reason))
            .Select(entry => $"no reason: {entry.Name}");

        var problems = duplicates.Concat(withoutReason).ToArray();

        Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_allowlist_count_is_at_least_one()
    {
        var wrong = FloatingPointAllowlist.Lines.Where(entry => entry.Count < 1).Select(entry => entry.LineText)
            .Concat(FloatingPointAllowlist.IlMethods.SelectMany(entry => entry.Uses.Where(use => use.Count < 1).Select(use => $"{entry.Method} {use.What}")))
            .ToArray();

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
    [InlineData("var n = new System.Double();")]
    [InlineData("var n = new Double();")]
    [InlineData("decimal[] xs = [..0.1 > 0 ? [amount] : []];")]
    [InlineData("var r = xs[..0.5];")]
    [InlineData("NFloat n;")]
    [InlineData("var n = System.Runtime.InteropServices.NFloat.Zero;")]
    [InlineData("var c = System.Numerics.Complex.One;")]
    [InlineData("Complex c = default;")]
    [InlineData("Vector3 v;")]
    [InlineData("var m = System.Numerics.Matrix4x4.Identity;")]
    [InlineData("var q = (Single)1;")]
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
    [InlineData("var n = nameof(Single);")]
    [InlineData("var n = nameof(Enumerable.Single);")]
    [InlineData("var row = rows.Single<(string, decimal)>();")]
    [InlineData("var rows2 = items.Select(Enumerable.Single);")]
    [InlineData("var row = Single(rows);")]
    [InlineData("var row = Single<int>(rows);")]
    [InlineData("var a = 1..5;")]
    [InlineData("var h = x.Half;")]
    [InlineData("var d = x.Double.Y;")]
    [InlineData("var s = $$\"\"\"{{x:a}b}} 1.5 double\"\"\";")]
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
    // The key carries the field or return type, and every floating-point type of the member is reported.
    private static IReadOnlyList<MemberHit> ScanMembers()
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return
        [
            .. Libraries
                .SelectMany(assembly => assembly.GetTypes())
                .SelectMany(type => type.GetFields(declared)
                    .Select(field => (Key: FloatingPointTypes.FieldMemberKey(field), Found: FloatingPointTypes.AllIn([field.FieldType])))
                    .Concat(type.GetMethods(declared).Cast<MethodBase>().Concat(type.GetConstructors(declared))
                        .Select(method => (Key: FloatingPointTypes.MemberKey(method), Found: FloatingPointTypes.AllIn(
                            [(method as MethodInfo)?.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)])))))
                .Where(member => member.Found.Any())
                .Select(member => new MemberHit(member.Key, string.Join(", ", member.Found))),
        ];
    }

    private static IReadOnlyList<FloatingPointFinding> ScanIl()
    {
        var doors = FloatingPointAllowlist.ConversionDoors.Select(door => door.Method).ToHashSet(StringComparer.Ordinal);

        return [.. Libraries.SelectMany(assembly => FloatingPointIlScanner.Scan(assembly, doors))];
    }

    private static int AllowedCount(IReadOnlyDictionary<string, FloatingPointIlAllow> pinned, string method, string what) =>
        pinned.TryGetValue(method, out var entry) ? entry.Uses.Where(pin => pin.What == what).Sum(pin => pin.Count) : 0;

    private static IEnumerable<string> UsesBeyondPins(IReadOnlyDictionary<string, FloatingPointIlAllow> pinned) =>
        IlFindings.Value
            .Where(finding => finding.Kind is FindingKind.Use)
            .GroupBy(finding => (finding.Method, finding.What))
            .Select(group => (group.Key.Method, group.Key.What, Found: group.Count(), Allowed: AllowedCount(pinned, group.Key.Method, group.Key.What)))
            .Where(item => item.Found > item.Allowed)
            .Select(item => $"{item.Method} {item.What} (found {item.Found.ToInvariant()}, allowed {item.Allowed.ToInvariant()})");

    // A local differs between Debug and Release, so it is not pinned; it still has to sit in a listed method.
    private static IEnumerable<string> LocalsOutsideTheList(IReadOnlyDictionary<string, FloatingPointIlAllow> pinned) =>
        IlFindings.Value
            .Where(finding => finding.Kind is FindingKind.Local && pinned.ContainsKey(finding.Method) is false)
            .Select(finding => $"{finding.Method} {finding.What}: the method is not allowlisted")
            .Distinct(StringComparer.Ordinal);

    private readonly record struct SourceHit(string RelativePath, int Number, string Line, string TrimmedLine);

    private readonly record struct MemberHit(string Key, string FloatTypes);
}
