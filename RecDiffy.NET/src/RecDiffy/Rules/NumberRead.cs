using System.Globalization;

namespace RecDiffy.Rules;

internal enum NumberKind
{
    Missing,
    Value,
    Failed,
}

internal readonly record struct NumberRead
{
    private readonly decimal _value;

    // Missing and Failed both store zero. Kind is the only signal that separates them.
    private NumberRead(NumberKind kind, decimal value)
    {
        Kind = kind;
        _value = value;
    }

    public NumberKind Kind { get; }

    public static NumberRead Missing { get; } = new(NumberKind.Missing, 0m);

    public static NumberRead Failed { get; } = new(NumberKind.Failed, 0m);

    public static NumberRead Of(decimal value) => new(NumberKind.Value, value);

    public T Convert<T>(Func<decimal, T> value, Func<T> missing, Func<T> failed)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(missing);
        ArgumentNullException.ThrowIfNull(failed);
        return Kind switch
        {
            NumberKind.Value => value(_value),
            NumberKind.Missing => missing(),
            _ => failed(),
        };
    }
}

internal static class Numbers
{
    // A double that does not fit in decimal is Failed. It is not parsed again as text.
    public static NumberRead CanBeNumber(this object? raw) => raw switch
    {
        null or "" => NumberRead.Missing,
        decimal value => NumberRead.Of(value),
        int value => NumberRead.Of(value),
        long value => NumberRead.Of(value),
        double value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),
        float value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),
        string text when GroupsThousands(text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => NumberRead.Of(parsed),
        _ => NumberRead.Failed,
    };

    // A comma is a thousands separator only in groups of three. "1,5" fails the parse instead of becoming 15.
    private static bool GroupsThousands(string text)
    {
        var groups = text.Split('.')[0].Trim().Trim('+', '-').Trim().Split(',');
        return groups is [_] || (groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(group => group.Length == 3));
    }
}
