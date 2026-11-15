using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace RecDiffy.Tests;

// #361: Proves the IL scanner on small methods in this file, so the gate is known to fire before it is trusted to stay quiet.
public class FloatingPointIlScannerTests
{
    private static readonly string DoorKey = FloatingPointTypes.MethodKey(typeof(Fixture).GetMethod(nameof(Fixture.Door))!);

    private static IReadOnlyList<FloatingPointFinding> Scan(params string[] doors) =>
        FloatingPointIlScanner.Scan(
            new[] { typeof(Fixture) }.Concat(typeof(Fixture).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)),
            doors.ToHashSet(StringComparer.Ordinal));

    private static string[] Found(string method) =>
        [.. Scan(DoorKey)
            .Where(finding => finding.Kind is FindingKind.Use && (finding.Method.Contains($".{method}(", StringComparison.Ordinal) || finding.Method.Contains($"<{method}>", StringComparison.Ordinal)))
            .Select(finding => finding.What)];

    [Theory]
    [InlineData(nameof(Fixture.SqrtOfInt), "System.Math.Sqrt(System.Double)")]
    [InlineData(nameof(Fixture.PowerOfTen), "System.Math.Pow(System.Double, System.Double)")]
    [InlineData(nameof(Fixture.DaysBetween), "System.TimeSpan.get_TotalDays()")]
    [InlineData(nameof(Fixture.FromRecord), "System.Data.IDataRecord.GetDouble(System.Int32)")]
    [InlineData(nameof(Fixture.ToDoubleAndBack), "System.Decimal.op_Explicit(System.Decimal) : System.Double")]
    [InlineData(nameof(Fixture.FromDouble), "System.Decimal.op_Explicit(System.Double) : System.Decimal")]
    [InlineData(nameof(Fixture.Constant), "ldc.r8")]
    [InlineData(nameof(Fixture.FloatConstant), "ldc.r4")]
    [InlineData(nameof(Fixture.WidenedInt), "conv.r8")]
    [InlineData(nameof(Fixture.TypeTest), "isinst System.Double")]
    [InlineData(nameof(Fixture.UsesStaticField), "reads field")]
    [InlineData(nameof(Fixture.Average), "System.Linq.Enumerable.Average(System.Collections.Generic.IEnumerable`1[System.Int32])")]
    [InlineData(nameof(Fixture.FloatTest), "isinst System.Single")]
    [InlineData(nameof(Fixture.SingleRoot), "System.MathF.Sqrt(System.Single)")]
    public void The_scanner_reports_floating_point_that_no_source_line_shows(string method, string expected) =>
        Assert.Contains(Found(method), what => what.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void The_scanner_reports_a_local_of_a_floating_point_type()
    {
        var locals = Scan(DoorKey)
            .Where(finding => finding.Kind is FindingKind.Local && finding.Method.Contains($".{nameof(Fixture.KeptLocal)}(", StringComparison.Ordinal))
            .Select(finding => finding.What);

        Assert.Contains(locals, what => what == "declares a local of type double");
    }

    [Theory]
    [InlineData(nameof(Fixture.ConvertObject), "System.Convert.ToDecimal(System.Object, System.IFormatProvider)")]
    [InlineData(nameof(Fixture.ChangeType), "System.Convert.ChangeType(System.Object, System.Type, System.IFormatProvider)")]
    [InlineData(nameof(Fixture.ConvertibleCall), "System.IConvertible.ToDecimal(System.IFormatProvider)")]
    [InlineData(nameof(Fixture.DynamicCall), "Microsoft.CSharp.RuntimeBinder.")]
    [InlineData(nameof(Fixture.NFloatMath), "System.Runtime.InteropServices.NFloat")]
    [InlineData(nameof(Fixture.ComplexMath), "System.Numerics.Complex")]
    public void The_scanner_reports_runtime_conversions_and_the_other_floating_point_types(string method, string expected) =>
        Assert.Contains(Found(method), what => what.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void The_scanner_does_not_flag_decimal_conversions_that_have_no_object_argument()
    {
        Assert.Empty(Found(nameof(Fixture.LinqSingle)));
        Assert.Empty(Found(nameof(Fixture.DecimalOnly)));
        Assert.Empty(Found(nameof(Fixture.ConvertText)));
        Assert.Empty(Found(nameof(Fixture.ExponentText)));
    }

    [Fact]
    public void A_call_to_a_door_is_a_door_finding_and_not_a_use()
    {
        var findings = Scan(DoorKey).Where(finding => finding.Method.Contains(nameof(Fixture.CallsDoor), StringComparison.Ordinal)).ToArray();

        Assert.Contains(findings, finding => finding.Kind is FindingKind.Door && finding.What == "calls " + DoorKey);
        Assert.DoesNotContain(findings, finding => finding.Kind is FindingKind.Use);
    }

    [Fact]
    public void A_method_group_of_a_door_is_a_door_finding()
    {
        var findings = Scan(DoorKey).Where(finding => finding.Method.Contains(nameof(Fixture.DoorAsDelegate), StringComparison.Ordinal)).ToArray();

        Assert.Contains(findings, finding => finding.Kind is FindingKind.Door && finding.What == "takes the address of " + DoorKey);
    }

    [Fact]
    public void Without_the_door_listed_a_call_to_it_is_not_reported()
    {
        var findings = Scan().Where(finding => finding.Method.Contains(nameof(Fixture.CallsDoor), StringComparison.Ordinal));

        Assert.Empty(findings);
    }

    [Fact]
    public void The_scanner_reads_constructors_and_lambdas()
    {
        var findings = Scan(DoorKey).Select(finding => finding.Method + " " + finding.What).ToArray();

        Assert.Contains(findings, text => text.Contains(".ctor(System.Double)", StringComparison.Ordinal));
        Assert.Contains(findings, text => text.Contains("<Lambda>b__", StringComparison.Ordinal) && text.Contains("Math.Round(System.Double)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_method_key_names_the_declaring_type_the_method_and_its_parameter_types()
    {
        var method = typeof(Fixture).GetMethod(nameof(Fixture.DaysBetween))!;

        Assert.Equal("RecDiffy.Tests.FloatingPointIlScannerTests+Fixture.DaysBetween(System.DateTime, System.DateTime)", FloatingPointTypes.MethodKey(method));
        Assert.Equal("RecDiffy.Tests.FloatingPointIlScannerTests+Fixture.DaysBetween(System.DateTime, System.DateTime) : System.Decimal", FloatingPointTypes.MemberKey(method));
    }

    [Fact]
    public void A_key_on_a_constructed_generic_type_names_no_assembly_version()
    {
        var key = FloatingPointTypes.MethodKey(typeof(List<double>).GetMethod(nameof(List<double>.Add))!);

        Assert.Equal("System.Collections.Generic.List`1[System.Double].Add(System.Double)", key);
    }

    [Fact]
    public void A_field_member_key_carries_the_field_type()
    {
        var field = typeof(Fixture).GetField("Held", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal("RecDiffy.Tests.FloatingPointIlScannerTests+Fixture.Held : System.Double", FloatingPointTypes.FieldMemberKey(field));
    }

    [Theory]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(float), "float")]
    [InlineData(typeof(Half), "Half")]
    [InlineData(typeof(double?), "double")]
    [InlineData(typeof(double[]), "double")]
    [InlineData(typeof(List<float>), "float")]
    [InlineData(typeof(Dictionary<string, Half[]>), "Half")]
    [InlineData(typeof(Func<int, double>), "double")]
    [InlineData(typeof(NFloat), "NFloat")]
    [InlineData(typeof(NFloat?), "NFloat")]
    [InlineData(typeof(Complex), "Complex")]
    [InlineData(typeof(Vector2), "Vector2")]
    [InlineData(typeof(Vector4), "Vector4")]
    [InlineData(typeof(Matrix4x4), "Matrix4x4")]
    [InlineData(typeof(Quaternion), "Quaternion")]
    [InlineData(typeof(Vector<int>), "Vector<T>")]
    [InlineData(typeof(Vector128<int>), "Vector128<T>")]
    [InlineData(typeof(List<Complex>), "Complex")]
    public void The_type_check_finds_a_floating_point_type_inside_a_type(Type type, string expected) =>
        Assert.Equal(expected, FloatingPointTypes.Find(type));

    [Fact]
    public void The_type_check_reports_every_floating_point_type_of_a_member()
    {
        Assert.True(new[] { "double", "float" }.SequenceEqual(FloatingPointTypes.AllIn([typeof(double), typeof(int), typeof(float), typeof(double[])]), StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(List<decimal>))]
    [InlineData(typeof(string))]
    [InlineData(typeof(BigInteger))]
    public void The_type_check_ignores_other_types(Type type) =>
        Assert.Null(FloatingPointTypes.Find(type));

    [Fact]
    public void The_type_check_sees_through_pointers_and_by_ref()
    {
        Assert.Equal("double", FloatingPointTypes.Find(typeof(double).MakePointerType()));
        Assert.Equal("float", FloatingPointTypes.Find(typeof(float).MakeByRefType()));
    }

    internal sealed class Fixture
    {
        private static readonly double Held = 1.5d;

        internal Fixture(double value) => Seed = (decimal)Math.Round(value);

        internal decimal Seed { get; }

        public static decimal SqrtOfInt(decimal x) => (decimal)Math.Sqrt((int)x);

        public static decimal PowerOfTen(int scale)
        {
            var y = Math.Pow(10, -scale);
            return (decimal)y;
        }

        public static decimal DaysBetween(DateTime a, DateTime b) => (decimal)(b - a).TotalDays;

        public static decimal FromRecord(System.Data.IDataRecord record) => (decimal)record.GetDouble(0);

        public static decimal ToDoubleAndBack(decimal x) => (decimal)(double)x;

        public static decimal FromDouble(double x) => (decimal)x;

        public static double Constant() => 2.5d;

        public static float FloatConstant() => 2.5f;

        public static decimal WidenedInt(int x) => (decimal)Math.Round((double)x);

        public static bool TypeTest(object? value) => value is double;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string KeptLocal(int x)
        {
            double y = x;
            return y > 1 ? y.ToString("R", CultureInfo.InvariantCulture) : "";
        }

        public static double UsesStaticField() => Held;

        public static bool LinqSingle(IEnumerable<int> xs) => xs.Single() > 0;

        public static decimal DecimalOnly(decimal a, decimal b) => decimal.Round((a * 1.5m) / b, 2);

        public static Func<double, decimal> Lambda() => value => (decimal)Math.Round(value);

        public static decimal Average(IEnumerable<int> xs) => (decimal)xs.Average();

        public static decimal FloatTest(object o) => o is float f ? (decimal)f : 0m;

        public static decimal SingleRoot(int n) => (decimal)MathF.Sqrt(n);

        public static decimal ConvertObject(object raw) => Convert.ToDecimal(raw, CultureInfo.InvariantCulture);

        public static decimal ChangeType(object raw) => (decimal)Convert.ChangeType(raw, typeof(decimal), CultureInfo.InvariantCulture);

        public static decimal ConvertibleCall(IConvertible raw) => raw.ToDecimal(CultureInfo.InvariantCulture);

        public static decimal DynamicCall(object raw)
        {
            dynamic boxed = raw;
            return (decimal)(boxed * 1);
        }

        public static decimal NFloatMath(decimal x) => decimal.CreateChecked(NFloat.CreateChecked(x) / NFloat.CreateChecked(3));

        public static decimal ComplexMath(decimal x) => decimal.CreateChecked(Complex.CreateChecked(x) / Complex.CreateChecked(3));

        public static decimal ConvertText(string text) => Convert.ToDecimal(text, CultureInfo.InvariantCulture);

        public static decimal ExponentText(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        public static decimal Door(object raw) => raw is decimal value ? value : 0m;

        public static decimal CallsDoor(object raw) => Door(raw);

        public static Func<object, decimal> DoorAsDelegate() => Door;
    }
}
