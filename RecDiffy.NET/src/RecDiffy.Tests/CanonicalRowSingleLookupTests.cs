using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class CanonicalRowSingleLookupTests
{
    [Fact]
    public void Indexer_reads_each_cell_once_and_reports_null_and_missing_fields_as_text()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal))
            .Write("Amount", CanonicalCell.FromDecimal(123.45m), "test", null, null)
            .Write("TradeDate", CanonicalCell.FromDate(new DateOnly(2025, 6, 12)), "test", null, null)
            .Write("Note", CanonicalCell.FromText("hello"), "test", null, null)
            .Write("NullAmount", CanonicalCell.Null(FieldType.Decimal), "test", null, null);

        var canonical = new CanonicalRow(row);

        var amount = canonical["Amount"];
        Assert.Equal(FieldType.Decimal, amount.Type);
        Assert.False(amount.IsNull);
        Assert.Equal(123.45m, amount.Number);
        Assert.Equal(default, amount.Date);
        Assert.Equal(0, amount.Interned);
        Assert.Equal("123.45", amount.Text);
        Assert.False(canonical.IsNull("Amount"));
        Assert.Equal("123.45", canonical.Text("Amount"));

        var date = canonical["TradeDate"];
        Assert.Equal(FieldType.Date, date.Type);
        Assert.False(date.IsNull);
        Assert.Equal(0m, date.Number);
        Assert.Equal(new DateOnly(2025, 6, 12), date.Date);
        Assert.Equal(0, date.Interned);
        Assert.Equal("2025-06-12", date.Text);
        Assert.False(canonical.IsNull("TradeDate"));
        Assert.Equal("2025-06-12", canonical.Text("TradeDate"));

        var text = canonical["Note"];
        Assert.Equal(FieldType.Text, text.Type);
        Assert.False(text.IsNull);
        Assert.Equal(0m, text.Number);
        Assert.Equal(default, text.Date);
        Assert.Equal(0, text.Interned);
        Assert.Equal("hello", text.Text);
        Assert.False(canonical.IsNull("Note"));
        Assert.Equal("hello", canonical.Text("Note"));

        var nullAmount = canonical["NullAmount"];
        Assert.Equal(FieldType.Text, nullAmount.Type);
        Assert.True(nullAmount.IsNull);
        Assert.Equal(0m, nullAmount.Number);
        Assert.Equal(default, nullAmount.Date);
        Assert.Equal(0, nullAmount.Interned);
        Assert.Null(nullAmount.Text);
        Assert.True(canonical.IsNull("NullAmount"));
        Assert.Equal("", canonical.Text("NullAmount"));

        var missing = canonical["Missing"];
        Assert.Equal(FieldType.Text, missing.Type);
        Assert.True(missing.IsNull);
        Assert.Equal(0m, missing.Number);
        Assert.Equal(default, missing.Date);
        Assert.Equal(0, missing.Interned);
        Assert.Null(missing.Text);
        Assert.True(canonical.IsNull("Missing"));
        Assert.Equal("", canonical.Text("Missing"));
    }
}
