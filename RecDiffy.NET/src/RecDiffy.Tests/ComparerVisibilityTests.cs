using RecDiffy.Results;

namespace RecDiffy.Tests;

// The comparers are the engine's raw compare step (#408 finding 5). FieldDiffer rounds both values to the field's
// scale, calls a comparer, and then applies the profile's tolerance. A comparer called on its own does neither, so a
// host that could reach one would read a delta inside the tolerance as a break. These tests keep them off the host
// surface. This project sees them only because it is a friend assembly of the core.
public class ComparerVisibilityTests
{
    [Theory]
    [InlineData(typeof(IFieldComparer<>))]
    [InlineData(typeof(DecimalComparer))]
    [InlineData(typeof(DateComparer))]
    public void A_comparer_is_not_visible_to_a_host(Type comparer) =>
        Assert.False(comparer.IsVisible);

    [Fact]
    public void The_decimal_comparer_knows_no_tolerance_so_any_delta_is_a_break()
    {
        var outcome = default(DecimalComparer).Compare(10.00m, 10.01m, out var delta);

        Assert.Equal(FieldOutcome.Break, outcome);
        Assert.Equal(0.01m, delta.Number);
    }

    [Fact]
    public void The_date_comparer_knows_no_tolerance_so_one_day_is_a_break()
    {
        var outcome = default(DateComparer).Compare(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3), out var delta);

        Assert.Equal(FieldOutcome.Break, outcome);
        Assert.Equal(1, delta.Days);
    }
}
