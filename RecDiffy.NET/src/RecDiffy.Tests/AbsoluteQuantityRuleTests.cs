using System.Data;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class AbsoluteQuantityRuleTests
{
    [Fact]
    public void Quantity_absolute_is_registered_to_the_new_rule() =>
        Assert.Equal("AbsoluteQuantityRule", Registered().GetType().Name);

    [Theory]
    [InlineData("int", "-100", "100")]
    [InlineData("long", "-100", "100")]
    [InlineData("decimal", "-100.25", "100.25")]
    [InlineData("double", "-2.5", "2.5")]
    [InlineData("float", "-2.5", "2.5")]
    [InlineData("text", "-1,000.50", "1000.50")]
    [InlineData("int", "7", "7")]
    [InlineData("int", "-2147483648", "2147483648")]
    [InlineData("long", "-9223372036854775808", "9223372036854775808")]
    public void A_raw_quantity_of_every_intake_type_becomes_its_absolute_value(string kind, string raw, string expected)
    {
        var row = Apply(Row(RawValue.Of(kind, raw)));

        Assert.Equal(CanonicalCell.FromDecimal(RawValue.Number(expected)), row.Values["Quantity"]);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("absent")]
    public void A_blank_or_absent_quantity_is_a_silent_null(string raw)
    {
        var row = Apply(raw switch
        {
            "null" => Row(null),
            "empty" => Row(""),
            _ => RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal)),
        });

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("text", "abc")]
    [InlineData("text", "1,000.5x")]
    [InlineData("text", "1,5")]
    [InlineData("text", "1234,5")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-01-02")]
    [InlineData("double", "NaN")]
    public void An_unreadable_quantity_is_null_and_Invalid(string kind, string raw)
    {
        var row = Apply(Row(RawValue.Of(kind, raw)));

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DecimalParse", DiagnosticSeverity.Invalid, "quantity.absolute", "Quantity"), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field));
    }

    [Theory]
    [InlineData("null", "")]
    [InlineData("text", "abc")]
    public void An_earlier_rules_value_is_used_when_the_raw_cell_cannot_be_read(string kind, string raw)
    {
        var row = Apply(Row(RawValue.Of(kind, raw)).Write("Qty", CanonicalCell.FromDecimal(-5m), "decimal.parse", "-5", "-5"));

        Assert.Equal(CanonicalCell.FromDecimal(5m), row.Values["Quantity"]);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("date")]
    public void An_earlier_value_that_is_not_a_number_is_not_read_as_zero(string kind)
    {
        var earlier = kind switch
        {
            "text" => CanonicalCell.FromText("abc"),
            _ => CanonicalCell.FromDate(new DateOnly(2026, 1, 2)),
        };

        var row = Apply(Row("abc").Write("Qty", earlier, "text.copy", "abc", "abc"));

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }

    [Theory]
    [InlineData(typeof(byte), "255")]
    [InlineData(typeof(short), "-32768")]
    [InlineData(typeof(uint), "4294967295")]
    [InlineData(typeof(ulong), "18446744073709551615")]
    public void Other_integer_columns_from_a_data_reader_are_read(Type type, string value)
    {
        using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Qty", type);
#pragma warning restore IDISP004
        table.Rows.Add("1", Convert.ChangeType(value, type, CultureInfo.InvariantCulture));

        var result = Run(RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()));

        Assert.Equal(decimal.Abs(RawValue.Number(value)), result.Canonical(Assert.Single(result.Rows).Row)["Quantity"].Number);
    }

    [Theory]
    [InlineData("int", "-100")]
    [InlineData("long", "-9223372036854775808")]
    [InlineData("decimal", "-100.25")]
    [InlineData("double", "-2.5")]
    [InlineData("float", "-2.5")]
    [InlineData("text", "-1,000.50")]
    [InlineData("text", "1,5")]
    [InlineData("text", "1234,5")]
    [InlineData("text", "")]
    [InlineData("null", "")]
    [InlineData("text", "abc")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-01-02")]
    [InlineData("double", "NaN")]
    public void Every_raw_value_is_read_the_same_way_as_decimal_parse_reads_it(string kind, string raw)
    {
        var value = RawValue.Of(kind, raw);
        var parsed = Row(value);
        DecimalParse().Apply(ref parsed);
        var absolute = Apply(Row(value));

        Assert.Equal(parsed.Values["Parsed"].IsNull, absolute.Values["Quantity"].IsNull);
        Assert.Equal(decimal.Abs(parsed.Values["Parsed"].Number), absolute.Values["Quantity"].Number);
        Assert.Equal(
            parsed.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity)),
            absolute.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity)));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    public void A_whole_number_quantity_from_a_data_reader_is_read(Type type)
    {
        using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Qty", type);
#pragma warning restore IDISP004
        table.Rows.Add("1", Convert.ChangeType(-100, type, CultureInfo.InvariantCulture));

        var result = Run(RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()));

        Assert.Equal(100m, result.Canonical(Assert.Single(result.Rows).Row)["Quantity"].Number);
    }

    [Fact]
    public void An_unreadable_quantity_makes_its_row_Invalid_in_a_run()
    {
        var map = new SourceMap<(string Id, string Qty)>().Text("Id", row => row.Id).Text("Qty", row => row.Qty);

        var result = Run(RecordSources.FromObjects<(string Id, string Qty)>("left", [("1", "abc")], map));

        var row = Assert.Single(result.Rows);
        Assert.Equal(InputValidity.Invalid, row.Validity);
        Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "DecimalParse", RuleId: "quantity.absolute" });
    }

    private static INormalizationRule Registered()
    {
        var call = new RuleCall(
            "quantity.absolute",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = "Quantity" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
        return TradingRulePack.AddTo(RuleRegistry.CreateCore()).TryGet("quantity.absolute", out var registration)
            ? registration.Create(call, new FieldSchema([]), MidpointMode.ToEven)
            : throw new InvalidOperationException("quantity.absolute is not registered.");
    }

    private static INormalizationRule DecimalParse()
    {
        var call = new RuleCall(
            "decimal.parse",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = "Parsed" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
        return RuleRegistry.CreateCore().TryGet("decimal.parse", out var registration)
            ? registration.Create(call, new FieldSchema([new FieldDeclaration("Parsed", FieldType.Decimal, FieldRole.Economic, 4)]), MidpointMode.ToEven)
            : throw new InvalidOperationException("decimal.parse is not registered.");
    }

    private static RowContext Row(object? quantity) =>
        RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = quantity });

    private static RowContext Apply(RowContext row)
    {
        Registered().Apply(ref row);
        return row;
    }

    private static ReconciliationResult Run(RecordSource left) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            left,
            RecordSources.FromObjects("right", Array.Empty<string>(), new SourceMap<string>()),
            ReferenceDataSet.Empty);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "quantity",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": { "left": { "derive": [ { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" } ] } },
          "paths": [ { "id": "all", "route": { "rule": "route.all" } } ]
        }
        """;
}
