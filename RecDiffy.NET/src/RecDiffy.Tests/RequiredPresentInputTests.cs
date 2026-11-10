using RecDiffy.Rules;

namespace RecDiffy.Tests;

// required.present (RequiredPresentRule and RequiredFields): one RequiredMissing per canonical field that has no value.
public class RequiredPresentInputTests
{
    private static readonly RuleBench Required = new RuleBench("required.present").WithList("fields", "Id", "Quantity", "Memo");

    [Fact]
    public void Fields_that_all_have_a_value_raise_nothing()
    {
        var written = Written(("Id", "T-1"), ("Quantity", "0"), ("Memo", "x"));

        var row = Required.Apply(written);

        Assert.Empty(row.Diagnostics);
        Assert.Same(written.Values, row.Values);
        Assert.Equal(written.Traces, row.Traces);
    }

    [Fact]
    public void Each_missing_field_is_reported_in_declared_order()
    {
        var row = Required.Apply(Written(("Id", ""), ("Quantity", "5"), ("Memo", null)));

        Assert.Equal(
            [("RequiredMissing", DiagnosticSeverity.Invalid, "required.present", "Id", (string?)null), ("RequiredMissing", DiagnosticSeverity.Invalid, "required.present", "Memo", null)],
            row.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText)));
    }

    [Fact]
    public void A_field_no_rule_wrote_is_missing()
    {
        var row = Required.Apply(Written(("Id", "T-1"), ("Quantity", "5")));

        Assert.Equal("Memo", Assert.Single(row.Diagnostics).Field);
    }

    // A raw column of the same name is not a value. Only a canonical field is.
    [Fact]
    public void A_raw_column_does_not_make_a_field_present()
    {
        var row = Required.Apply(RuleBench.Row(("Id", "T-1"), ("Quantity", "5"), ("Memo", "x")));

        Assert.Equal(["Id", "Quantity", "Memo"], row.Diagnostics.Select(diagnostic => diagnostic.Field), StringComparer.Ordinal);
    }

    [Fact]
    public void The_rule_reads_its_fields_and_writes_none()
    {
        var rule = Required.Build();

        Assert.Equal([new FieldId("Id"), new FieldId("Quantity"), new FieldId("Memo")], rule.Reads);
        Assert.Empty(rule.Writes);
        Assert.Equal(RulePhase.Validate, rule.Phase);
    }

    // identifier.map writes Id, decimal.parse writes Quantity and text.copy writes Memo, as a profile's earlier phases would.
    private static RowContext Written(params (string Field, string? Raw)[] cells)
    {
        var raw = RuleBench.Row([.. cells.Select(cell => ("Raw" + cell.Field, (object?)cell.Raw))]);
        RuleBench[] writers =
        [
            new RuleBench("identifier.map").With("from", "RawId").With("to", "Id").With("scheme", "ID"),
            new RuleBench("decimal.parse").With("from", "RawQuantity").With("to", "Quantity"),
            new RuleBench("text.copy").With("from", "RawMemo").With("to", "Memo"),
        ];
        var fields = cells.Select(cell => cell.Field).ToHashSet(StringComparer.Ordinal);
        return writers.Where(writer => fields.Contains(writer.Strings["to"])).Aggregate(raw, (row, writer) => writer.Apply(row));
    }
}
