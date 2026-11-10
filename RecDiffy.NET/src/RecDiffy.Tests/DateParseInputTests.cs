using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// date.parse, as the registry builds it today (DeclaredDateRule): which declared format reads a text, which values are
// dates without a format, and how a party's reference-data format replaces the declared ones.
public class DateParseInputTests
{
    private static readonly RuleBench Parse = new RuleBench("date.parse").With("from", "Raw").With("to", "Day");

    [Theory]
    [InlineData("01/02/2025", "dd/MM/yyyy|MM/dd/yyyy", "2025-02-01")]
    [InlineData("01/02/2025", "MM/dd/yyyy|dd/MM/yyyy", "2025-01-02")]
    [InlineData("13/02/2025", "MM/dd/yyyy|dd/MM/yyyy", "2025-02-13")]
    [InlineData("2025-06-11", "dd/MM/yyyy|yyyy-MM-dd", "2025-06-11")]
    public void The_first_declared_format_that_reads_the_text_wins(string raw, string formats, string expected)
    {
        var row = Parse.WithList("formats", formats.Split('|')).Apply(("Raw", raw));

        Assert.Equal(new TraceStep("date.parse", "Day", raw, expected), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("2025/06/11")]
    [InlineData("31/02/2025")]
    [InlineData("29/02/2025")]
    [InlineData("00/01/2025")]
    [InlineData("32/01/2025")]
    [InlineData("11/13/2025")]
    [InlineData("11/06/2025 10:00")]
    [InlineData("11/06/25")]
    [InlineData("tomorrow")]
    public void Text_no_declared_format_reads_is_Invalid(string raw)
    {
        var row = Parse.WithList("formats", "yyyy-MM-dd", "dd/MM/yyyy").Apply(("Raw", raw));

        Assert.True(row.Values["Day"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DateParse", DiagnosticSeverity.Invalid, "date.parse", "Day", raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Fact]
    public void A_leap_day_reads_only_in_a_leap_year()
    {
        var formats = Parse.WithList("formats", "dd/MM/yyyy");

        Assert.Equal(new DateOnly(2024, 2, 29), formats.Apply(("Raw", "29/02/2024")).Values["Day"].Date);
        Assert.Equal("DateParse", Assert.Single(formats.Apply(("Raw", "29/02/2025")).Diagnostics).Code);
    }

    [Fact]
    public void A_typed_date_time_loses_its_time()
    {
        var row = Parse.Apply(("Raw", new DateTime(2025, 6, 11, 23, 59, 59, DateTimeKind.Unspecified)));

        Assert.Equal(new DateOnly(2025, 6, 11), row.Values["Day"].Date);
        Assert.Empty(row.Diagnostics);
    }

    // A double is an OLE Automation serial, as a spreadsheet stores a date. Its fraction is the time of day.
    [Theory]
    [InlineData(45000d, "2023-03-15")]
    [InlineData(45000.999d, "2023-03-15")]
    [InlineData(0d, "1899-12-30")]
    [InlineData(-657434d, "0100-01-01")]
    [InlineData(2958465d, "9999-12-31")]
    public void A_double_is_read_as_a_spreadsheet_serial(double serial, string expected)
    {
        var row = Parse.Apply(("Raw", serial));

        Assert.Equal(expected, Assert.Single(row.Traces).Canonical);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-657435d)]
    [InlineData(2958466d)]
    public void A_double_outside_the_serial_range_is_Invalid(double serial)
    {
        var row = Parse.Apply(("Raw", serial));

        Assert.True(row.Values["Day"].IsNull);
        Assert.Equal("DateParse", Assert.Single(row.Diagnostics).Code);
    }

    // Only a double is a serial. A decimal, an integer or a float holding the same number is not a date.
    [Theory]
    [InlineData("decimal")]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("float")]
    [InlineData("bool")]
    public void A_value_that_is_not_a_date_a_double_or_text_is_Invalid(string kind)
    {
        var raw = kind == "bool" ? RawValue.Of(kind, "true") : RawValue.Of(kind, "45000");

        var row = Parse.Apply(("Raw", raw));

        Assert.True(row.Values["Day"].IsNull);
        Assert.Equal("DateParse", Assert.Single(row.Diagnostics).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_null_or_absent_cell_is_a_silent_null(bool absent)
    {
        var row = absent ? Parse.Apply(("Other", "x")) : Parse.Apply(("Raw", null));

        Assert.True(row.Values["Day"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_partys_reference_format_replaces_the_declared_formats()
    {
        var byParty = Parse.WithList("formats", "yyyy-MM-dd").With("formatBy", "Party");

        Assert.Equal(new DateOnly(2025, 6, 11), byParty.Apply(PartyRow("BETA", "11/06/2025")).Values["Day"].Date);
        Assert.Equal("DateParse", Assert.Single(byParty.Apply(PartyRow("BETA", "2025-06-11")).Diagnostics).Code);
    }

    [Fact]
    public void Text_whose_party_has_no_reference_format_is_Invalid()
    {
        var row = Parse.With("formatBy", "Party").Apply(PartyRow("GAMMA", "11/06/2025"));

        Assert.True(row.Values["Day"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DateParse", "Day", "11/06/2025"), (diagnostic.Code, diagnostic.Field, diagnostic.RawText));
    }

    [Fact]
    public void FormatBy_reads_the_party_field_so_compile_orders_it_first() =>
        Assert.Equal(new FieldId("Party"), Assert.Single(Parse.With("formatBy", "Party").Build().Reads));

    // The party is a canonical symbol, as symbol.map writes it before date.parse runs.
    internal static RowContext PartyRow(string party, object? day)
    {
        var reference = ReferenceDataSet.Create(
            "dates",
            new DateOnly(2025, 1, 1),
            "dates",
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["BETA"] = "dd/MM/yyyy" });
        var row = RuleBench.Row(("Party", party), ("Raw", day)) with { Reference = reference };
        return new RuleBench("symbol.map").With("from", "Party").With("to", "Party").Apply(row);
    }
}
