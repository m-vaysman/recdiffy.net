using System.Globalization;

namespace RecDiffy.Rules;

internal enum NumberKind
{
    Missing,
    Value,
    Failed,
}

internal readonly record struct NumberRead(NumberKind Kind, decimal Value)
{
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
            NumberKind.Value => value(Value),
            NumberKind.Missing => missing(),
            _ => failed(),
        };
    }
}

internal static class Numbers
{
    public static NumberRead CanBeNumber(this object? raw) => raw switch
    {
        null or "" => NumberRead.Missing,
        decimal value => NumberRead.Of(value),
        int value => NumberRead.Of(value),
        long value => NumberRead.Of(value),
        double value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),
        float value when Rounding.TryFromDouble(value, out var number) => NumberRead.Of(number),
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => NumberRead.Of(parsed),
        _ => NumberRead.Failed,
    };
}
