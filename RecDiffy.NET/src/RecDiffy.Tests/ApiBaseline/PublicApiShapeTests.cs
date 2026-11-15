using RecDiffy.ApiBaseline;
using RecDiffy.Tests.ApiShapes;

namespace RecDiffy.Tests;

// Each test reads a type in PublicApiShapes.cs and asserts its exact lines, so a signature change of that kind in a
// shipped library is known to change its baseline.
public class PublicApiShapeTests
{
    private const string Shapes = "RecDiffy.Tests.ApiShapes";

    private static readonly IReadOnlyList<string> Lines =
        PublicApiSurface.Describe(typeof(Tally).Assembly, [typeof(Tally), typeof(Turn<,>), typeof(Generic), typeof(Named<>)]);

    // The compiler writes a flag for a type parameter that must be a struct, so the flags after it stay in step: the
    // question mark lands on string, not on T.
    [Fact]
    public void A_struct_type_parameter_does_not_shift_the_annotations_after_it()
    {
        Assert.Contains(
            $"{Shapes}.Generic :: method public static System.Collections.Generic.Dictionary<T, string?> Index<T>(T key, string? note, System.Collections.Generic.List<string?> rest) where T : struct",
            Lines,
            StringComparer.Ordinal);
        Assert.Contains(
            $"{Shapes}.Generic :: method public static T? Maybe<T>(System.Collections.Generic.Dictionary<string, T?>? source, System.Collections.Generic.KeyValuePair<string?, T> pair, System.Collections.Generic.KeyValuePair<T?, string?> after) where T : struct",
            Lines,
            StringComparer.Ordinal);
    }

    [Fact]
    public void A_base_type_an_interface_and_a_constraint_carry_their_annotations()
    {
        Assert.Contains(
            $"{Shapes}.Named<T> :: type public abstract class extends System.Collections.Generic.List<string?> implements System.IComparable<string[]?>, System.IEquatable<{Shapes}.Named<T>?> where T : notnull",
            Lines,
            StringComparer.Ordinal);
        Assert.Contains(
            $"{Shapes}.Generic :: method public static void Sorted<T>(T first, System.Collections.Generic.IReadOnlyList<T?> rest) where T : class, System.IComparable<T?>",
            Lines,
            StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Generic :: method public static void Formatted<T>(T value) where T : System.IFormattable?", Lines, StringComparer.Ordinal);
    }

    [Fact]
    public void A_reference_return_says_whether_it_is_readonly()
    {
        Assert.Contains($"{Shapes}.Tally :: method public ref int Slot(int index)", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: method public ref readonly int At(int index)", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: property public ref int Last {{ get; }}", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: property public ref readonly int First {{ get; }}", Lines, StringComparer.Ordinal);
    }

    [Fact]
    public void A_readonly_member_of_a_struct_is_marked()
    {
        Assert.Contains($"{Shapes}.Tally :: type public struct", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: method public readonly int Peek()", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: method public void Add(int amount)", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: property public int Count {{ readonly get; }}", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: property public int Total {{ readonly get; set; }}", Lines, StringComparer.Ordinal);
    }

    [Fact]
    public void Scoped_and_the_way_a_parameter_is_passed_are_written()
    {
        Assert.Contains($"{Shapes}.Tally :: method public static int Sum(scoped System.ReadOnlySpan<int> values)", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: method public static void Fill(scoped ref System.Span<int> target, ref readonly int value, in int other)", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: method [UnscopedRef] public ref int Own()", Lines, StringComparer.Ordinal);
    }

    // A decimal constant is a static readonly field in metadata. Its value is compiled into every caller, like any constant.
    [Fact]
    public void Every_constant_carries_its_value()
    {
        Assert.Contains($"{Shapes}.Tally :: field public const decimal Rate = 1.25", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: field public const string Unit = \"rows\"", Lines, StringComparer.Ordinal);
        Assert.Contains($"{Shapes}.Tally :: field public static readonly decimal Loaded", Lines, StringComparer.Ordinal);
        Assert.Contains("RecDiffy.ProfileProblemCodes :: field public const string DuplicatePass = \"DuplicatePass\"", PublicApiSurface.Describe(typeof(Reconciler).Assembly), StringComparer.Ordinal);
    }

    [Fact]
    public void A_generic_delegate_carries_its_variance()
    {
        Assert.Contains($"{Shapes}.Turn<TInput, TResult> :: type public delegate (in TInput) (out TResult) TResult Invoke(TInput input)", Lines, StringComparer.Ordinal);
    }

    // The whole text of the sample types, so nothing about them is left unasserted.
    [Fact]
    public void The_sample_types_read_as_26_lines()
    {
        Assert.Equal(26, Lines.Count);
        Assert.Equal(Lines.Count, Lines.Distinct(StringComparer.Ordinal).Count());
    }
}
