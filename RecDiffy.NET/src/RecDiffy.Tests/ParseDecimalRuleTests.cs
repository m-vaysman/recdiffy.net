using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ParseDecimalRuleTests
{
    [Fact]
    public void Decimal_parse_is_registered_to_the_new_rule() =>
        Assert.Equal(nameof(ParseDecimalRule), Registered(MidpointMode.ToEven).GetType().Name);

    [Theory]
    [InlineData("int", "-100", "-100")]
    [InlineData("long", "-9223372036854775808", "-9223372036854775808")]
    [InlineData("decimal", "100.25", "100.25")]
    [InlineData("double", "2.5", "2.5")]
    [InlineData("float", "2.5", "2.5")]
    [InlineData("text", "-1,000.50", "-1000.50")]
    [InlineData("text", "1,234.5", "1234.5")]
    [InlineData("text", " 42 ", "42")]
    public void A_number_of_every_intake_type_is_read(string kind, string raw, string expected)
    {
        var row = Apply(RawValue.Of(kind, raw), MidpointMode.ToEven);

        Assert.Equal(CanonicalCell.FromDecimal(RawValue.Number(expected)), row.Values["Quantity"]);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("1.005", MidpointMode.AwayFromZero, "1.01")]
    [InlineData("1.005", MidpointMode.ToEven, "1.00")]
    [InlineData("1.015", MidpointMode.ToEven, "1.02")]
    public void A_number_is_rounded_to_the_field_scale_with_the_profile_midpoint(string raw, MidpointMode midpoint, string expected) =>
        Assert.Equal(RawValue.Number(expected), Apply(raw, midpoint).Values["Quantity"].Number);

    [Theory]
    [InlineData("1,5")]
    [InlineData("12,34")]
    [InlineData("1,50")]
    [InlineData("1234,5")]
    [InlineData("1,2345")]
    public void A_decimal_comma_is_Invalid_not_a_thousands_separator(string raw)
    {
        var row = Apply(raw, MidpointMode.ToEven);

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DecimalParse", DiagnosticSeverity.Invalid, "decimal.parse", "Quantity"), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field));
    }

    [Theory]
    [InlineData("text", "abc")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-01-02")]
    [InlineData("double", "NaN")]
    [InlineData("double", "Infinity")]
    public void An_unreadable_value_is_null_and_Invalid(string kind, string raw)
    {
        var row = Apply(RawValue.Of(kind, raw), MidpointMode.ToEven);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    public void A_blank_value_is_a_silent_null(string kind)
    {
        var row = Apply(kind is "null" ? null : "", MidpointMode.ToEven);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("1,5", "15")]
    [InlineData("1234,5", "12345")]
    public void A_decimal_comma_no_longer_matches_the_digits_it_was_read_as(string left, string right)
    {
        var map = new SourceMap<(string Id, string Qty)>().Text("Id", row => row.Id).Text("Qty", row => row.Qty);

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string Id, string Qty)>("left", [("1", left)], map),
            RecordSources.FromObjects<(string Id, string Qty)>("right", [("1", right)], map),
            ReferenceDataSet.Empty);

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment is Alignment.Matched);
        var invalid = Assert.Single(result.Rows, row => row.Validity is InputValidity.Invalid);
        Assert.Equal("left", invalid.Row.Source);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic is { Code: "DecimalParse", RuleId: "decimal.parse" });
    }

    private static INormalizationRule Registered(MidpointMode midpoint)
    {
        var call = new RuleCall(
            "decimal.parse",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = "Quantity" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
        var schema = new FieldSchema([new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, 2)]);
        return RuleRegistry.CreateCore().TryGet("decimal.parse", out var registration)
            ? registration.Create(call, schema, midpoint)
            : throw new InvalidOperationException("decimal.parse is not registered.");
    }

    private static RowContext Apply(object? quantity, MidpointMode midpoint)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = quantity });
        Registered(midpoint).Apply(ref row);
        return row;
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "decimal-comma",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "paths": [
            {
              "id": "all",
              "route": { "rule": "route.all" },
              "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Id" ] } ]
            }
          ]
        }
        """;
}
