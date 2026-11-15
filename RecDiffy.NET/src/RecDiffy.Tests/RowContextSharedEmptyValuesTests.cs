using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #423 item 3: every row starts from one shared empty value table, so a write to one row never leaks into another.
public class RowContextSharedEmptyValuesTests
{
    private static RowContext Row(string key) =>
        RowContext.Create(key, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));

    [Fact]
    public void Two_rows_start_from_the_same_empty_values_table()
    {
        var first = Row("L1");
        var second = Row("L2");

        Assert.Same(first.Values, second.Values);
    }

    [Fact]
    public void Writing_to_one_row_leaves_other_rows_empty()
    {
        var first = Row("L1");
        var second = Row("L2");

        var written = first.Write("Price", CanonicalCell.FromDecimal(10m), "test", "10", "10");
        var third = Row("L3");

        Assert.Empty(second.Values);
        Assert.Empty(third.Values);
        Assert.Single(written.Values);
    }

    [Fact]
    public void The_shared_table_compares_keys_ordinally()
    {
        var row = Row("L1").Write("Price", CanonicalCell.FromDecimal(10m), "test", "10", "10");

        Assert.False(row.Values.ContainsKey("price"));
    }
}
