using System.Diagnostics.CodeAnalysis;

namespace RecDiffy.Tests.ApiShapes;

// Types with signatures the shipped libraries do not have today. PublicApiShapeTests reads them to check that a change
// of this kind in a shipped library would change its baseline line.
#pragma warning disable MA0016, MA0095 // The shapes are the point: concrete collections in a signature, and IEquatable without Equals(object).
public struct Tally
{
    public const decimal Rate = 1.25m;

    public const string Unit = "rows";

    public static readonly decimal Loaded = 2.5m;

    private readonly int[] _slots;

    private int _count;

    public Tally(int[] slots) => (_slots, _count) = (slots, 0);

    public readonly int Count => _count;

    public int Total
    {
        readonly get => _count;
        set => _count = value;
    }

    public ref int Last => ref _slots[^1];

    public ref readonly int First => ref _slots[0];

    public readonly int Peek() => _count;

    public void Add(int amount) => _count += amount;

    public ref int Slot(int index) => ref _slots[index];

    public ref readonly int At(int index) => ref _slots[index];

    [UnscopedRef]
    public ref int Own() => ref _count;

    public static int Sum(scoped ReadOnlySpan<int> values) => values.Length;

    public static void Fill(scoped ref Span<int> target, ref readonly int value, in int other) => target.Fill(value + other);
}

public delegate TResult Turn<in TInput, out TResult>(TInput input);

public static class Generic
{
    public static Dictionary<T, string?> Index<T>(T key, string? note, List<string?> rest)
        where T : struct => [];

    public static T? Maybe<T>(Dictionary<string, T?>? source, KeyValuePair<string?, T> pair, KeyValuePair<T?, string?> after)
        where T : struct => null;

    public static void Sorted<T>(T first, IReadOnlyList<T?> rest)
        where T : class, IComparable<T?> => _ = (first, rest);

    public static void Formatted<T>(T value)
        where T : IFormattable? => _ = value;
}

public abstract class Named<T> : List<string?>, IEquatable<Named<T>?>, IComparable<string[]?>
    where T : notnull
{
    public abstract bool Equals(Named<T>? other);

    public abstract int CompareTo(string[]? other);
}
#pragma warning restore MA0016, MA0095
