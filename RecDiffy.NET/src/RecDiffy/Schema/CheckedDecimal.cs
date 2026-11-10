namespace RecDiffy;

// decimal arithmetic throws OverflowException and has no Try form. Each operation here turns that throw into null, so a
// result outside the decimal range becomes an outcome for its row, group or field instead of failing the run (#224).
// A null operand gives null, so a chain of operations stays null once one step leaves the range.
internal static class CheckedDecimal
{
    // Raised on a combined item whose sum or difference leaves the decimal range. The field is unknown on that item.
    public const string Overflow = "AggregateOverflow";

    public static decimal? Add(decimal? left, decimal? right)
    {
        if (left is not { } augend || right is not { } addend)
            return null;
        try
        {
            return augend + addend;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static decimal? Subtract(decimal? left, decimal? right)
    {
        if (left is not { } minuend || right is not { } subtrahend)
            return null;
        try
        {
            return minuend - subtrahend;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static decimal? Multiply(decimal? left, decimal? right)
    {
        if (left is not { } multiplicand || right is not { } multiplier)
            return null;
        try
        {
            return multiplicand * multiplier;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    // A zero divisor is the caller's case to decide. It is not caught here.
    public static decimal? Divide(decimal? left, decimal? right)
    {
        if (left is not { } dividend || right is not { } divisor)
            return null;
        try
        {
            return dividend / divisor;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    // Adds in sequence order, as Enumerable.Sum does, so a total that fits is the same total Sum gives.
    public static decimal? Sum(IEnumerable<decimal> values) =>
        values.Aggregate((decimal?)0m, (total, value) => Add(total, value));
}
