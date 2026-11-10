namespace RecDiffy.Tests;

// #224: CheckedDecimal gives what decimal arithmetic gives when the result fits, and null when it does not.
public class CheckedDecimalTests
{
    [Fact]
    public void A_result_that_fits_is_the_plain_result()
    {
        decimal[] values = [0.1m, 12345678901234567890.123456789m, -3.5m, 79228162514264337593543950335m / 3m];

        Assert.Equal(values.Sum(), CheckedDecimal.Sum(values));
        Assert.Equal(values[1] + values[2], CheckedDecimal.Add(values[1], values[2]));
        Assert.Equal(values[1] - values[2], CheckedDecimal.Subtract(values[1], values[2]));
        Assert.Equal(values[1] * values[2], CheckedDecimal.Multiply(values[1], values[2]));
        Assert.Equal(values[1] / values[2], CheckedDecimal.Divide(values[1], values[2]));
    }

    [Fact]
    public void A_result_past_the_range_is_null()
    {
        Assert.Null(CheckedDecimal.Add(decimal.MaxValue, 1m));
        Assert.Null(CheckedDecimal.Subtract(decimal.MinValue, 1m));
        Assert.Null(CheckedDecimal.Multiply(decimal.MaxValue, 2m));
        Assert.Null(CheckedDecimal.Divide(decimal.MaxValue, 0.5m));
        Assert.Null(CheckedDecimal.Sum([decimal.MaxValue, 1m]));
    }

    // Sum adds in order, as Enumerable.Sum does, so a sum that leaves the range on the way is null even when a later
    // value would bring it back.
    [Fact]
    public void A_sum_that_leaves_the_range_on_the_way_is_null() =>
        Assert.Null(CheckedDecimal.Sum([decimal.MaxValue, 1m, -2m]));

    [Fact]
    public void A_null_operand_gives_null()
    {
        Assert.Null(CheckedDecimal.Add(null, 1m));
        Assert.Null(CheckedDecimal.Subtract(1m, null));
        Assert.Null(CheckedDecimal.Multiply(null, null));
        Assert.Null(CheckedDecimal.Divide(null, 1m));
    }
}
