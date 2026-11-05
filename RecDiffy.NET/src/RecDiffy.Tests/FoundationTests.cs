using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ProfileCompileTests
{
    [Fact]
    public void Rejects_an_unsupported_format_version()
    {
        var errors = Compile("""
            {
              "formatVersion": 2,
              "profileId": "sample",
              "version": "1.0.0",
              "fields": [],
              "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
            }
            """);

        Assert.Contains(errors, error => error.Code == "UnsupportedFormat");
    }

    [Fact]
    public void Rejects_a_field_read_before_it_is_written()
    {
        var errors = Compile("""
            {
              "formatVersion": 1,
              "profileId": "sample",
              "version": "1.0.0",
              "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
              "normalization": { "left": {
                "validate": [ { "rule": "required.present", "fields": ["Quantity"] } ]
              } },
              "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
            }
            """);

        Assert.Contains(errors, error => error.Code == "FieldReadBeforeWrite");
    }

    [Fact]
    public void Rejects_two_writes_of_the_same_field()
    {
        var errors = Compile("""
            {
              "formatVersion": 1,
              "profileId": "sample",
              "version": "1.0.0",
              "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
              "normalization": { "left": { "parse": [
                { "rule": "decimal.parse", "from": "A", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "B", "to": "Quantity" }
              ] } },
              "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
            }
            """);

        Assert.Contains(errors, error => error.Code == "AmbiguousWrite");
    }

    [Fact]
    public void Allows_disjoint_writes_of_the_same_field()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "sample",
              "version": "1.0.0",
              "fields": [ { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" } ],
              "normalization": { "left": { "parse": [
                { "rule": "decimal.parse", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Price_Quote_Convention", "notEquals": "32NDS_UST" } },
                { "rule": "decimal.parse", "from": "Exec_Price_Rate", "to": "Price", "when": { "field": "Price_Quote_Convention", "equals": "32NDS_UST" } }
              ] } },
              "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        Assert.Equal(2, profile.LeftRules.Count);
    }

    [Fact]
    public void Rejects_a_text_match_key_and_an_oversized_group()
    {
        var errors = Compile("""
            {
              "formatVersion": 1,
              "profileId": "sample",
              "version": "1.0.0",
              "fields": [
                { "name": "Sku", "type": "Identifier", "role": "Key" },
                { "name": "Note", "type": "Text", "role": "Text" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": { "left": { "identify": [
                { "rule": "identifier.map", "from": "Sku", "to": "Sku", "scheme": "SKU" },
                { "rule": "text.copy", "from": "Note", "to": "Note" }
              ], "parse": [
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }
              ] }, "right": { "identify": [
                { "rule": "identifier.map", "from": "Sku", "to": "Sku", "scheme": "SKU" },
                { "rule": "text.copy", "from": "Note", "to": "Note" }
              ], "parse": [
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }
              ] } },
              "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Note"], "conserve": ["Quantity"], "maxGroupSize": 17 }
              ] } ]
            }
            """);

        Assert.Contains(errors, error => error.Code == "TextKey");
        Assert.Contains(errors, error => error.Code == "LimitExceeded");
    }

    private static IReadOnlyList<ProfileError> Compile(string json)
    {
        var exception = Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()));
        return exception.Errors;
    }
}

public class CanonicalTableTests
{
    [Fact]
    public void Rounds_a_midpoint_with_the_profile_mode() =>
        Assert.Equal(1.2m, Rounding.Round(1.25m, 1, MidpointMode.ToEven));

    [Fact]
    public void Keeps_decimal_columns_unboxed()
    {
        var table = RecordTable.FromRows(
            [new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, 2)],
            [new Dictionary<string, CanonicalCell> { ["Quantity"] = CanonicalCell.FromDecimal(1.25m) }]);

        Assert.Equal(1.25m, table.DecimalColumn("Quantity")[0]);
        Assert.False(table.IsNull(0, "Quantity"));
    }

    [Fact]
    public void Parses_a_decimal_without_changing_the_input_row_object()
    {
        var row = RowContext.Create("A", Side.Left, new Dictionary<string, object?> { ["Quantity"] = "1.005" });
        var rule = new DecimalParseRule("Quantity", "Quantity", 2, MidpointMode.AwayFromZero);
        var updated = row;
        rule.Apply(ref updated);

        Assert.True(row.IsNull("Quantity"));
        Assert.Equal(1.01m, updated.Values["Quantity"].Number);
    }

    [Fact]
    public void Interns_the_same_symbol_to_one_id()
    {
        var (once, first) = InternPool.Empty.InternSymbol("BUY");
        var (_, second) = once.InternSymbol("BUY");
        var (_, other) = once.InternSymbol("SELL");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }
}
