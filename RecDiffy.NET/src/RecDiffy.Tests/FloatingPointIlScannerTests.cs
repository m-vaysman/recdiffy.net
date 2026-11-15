using System.Runtime.CompilerServices;

namespace RecDiffy.Tests;

// #361: Proves the IL scanner on small methods in this file, so the gate is known to fire before it is trusted to stay quiet.
public class FloatingPointIlScannerTests
{
    private static IReadOnlyList<FloatingPointFinding> Scan(params Type[] types) =>
        FloatingPointIlScanner.Scan(types.SelectMany(type => new[] { type }.Concat(type.GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))));

    private static string[] Found(string method) =>
        [.. Scan(typeof(Fixture)).Where(finding => finding.Method.Contains($".{method}(", StringComparison.Ordinal) || finding.Method.Contains($"<{method}>", StringComparison.Ordinal)).Select(finding => finding.What)];

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
    [InlineData(nameof(Fixture.KeptLocal), "declares a local of type double")]
    [InlineData(nameof(Fixture.UsesStaticField), "reads field")]
    public void The_scanner_reports_floating_point_that_no_source_line_shows(string method, string expected) =>
        Assert.Contains(Found(method), what => what.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void The_scanner_does_not_flag_Linq_Single_or_decimal_arithmetic()
    {
        Assert.Empty(Found(nameof(Fixture.LinqSingle)));
        Assert.Empty(Found(nameof(Fixture.DecimalOnly)));
    }

    [Fact]
    public void The_scanner_reads_constructors_and_lambdas()
    {
        var findings = Scan(typeof(Fixture)).Select(finding => finding.Method + " " + finding.What).ToArray();

        Assert.Contains(findings, text => text.Contains(".ctor(System.Double)", StringComparison.Ordinal));
        Assert.Contains(findings, text => text.Contains("<Lambda>b__", StringComparison.Ordinal) && text.Contains("Math.Round(System.Double)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_method_key_names_the_declaring_type_the_method_and_its_parameter_types()
    {
        var key = FloatingPointTypes.MethodKey(typeof(Fixture).GetMethod(nameof(Fixture.DaysBetween))!);

        Assert.Equal("RecDiffy.Tests.FloatingPointIlScannerTests+Fixture.DaysBetween(System.DateTime, System.DateTime)", key);
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
    public void The_type_check_finds_a_floating_point_type_inside_a_type(Type type, string expected) =>
        Assert.Equal(expected, FloatingPointTypes.Find(type));

    [Theory]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(List<decimal>))]
    [InlineData(typeof(string))]
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
            return y > 1 ? y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        public static double UsesStaticField() => Held;

        public static bool LinqSingle(IEnumerable<int> xs) => xs.Single() > 0;

        public static decimal DecimalOnly(decimal a, decimal b) => decimal.Round((a * 1.5m) / b, 2);

        public static Func<double, decimal> Lambda() => value => (decimal)Math.Round(value);
    }
}
