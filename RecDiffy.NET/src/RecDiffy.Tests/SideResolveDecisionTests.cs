using RecDiffy.Rules;

namespace RecDiffy.Tests;

// side.resolve, as the trading pack registers it today (CultureSideResolveRule): the direction codes it knows, the side a
// quantity's sign gives when there is no code, the conflicts it reports, and inversion by perspective.
public class SideResolveDecisionTests
{
    private static readonly RuleBench Resolve = new RuleBench("side.resolve").With("code", "Dir").With("quantity", "Qty");

    [Theory]
    [InlineData("B", "BUY")]
    [InlineData("bot", "BUY")]
    [InlineData(" Buy ", "BUY")]
    [InlineData("S", "SELL")]
    [InlineData("SLD", "SELL")]
    [InlineData("sell", "SELL")]
    [InlineData("P", "PAY_FIXED")]
    [InlineData("pay", "PAY_FIXED")]
    [InlineData("PayFixed", "PAY_FIXED")]
    [InlineData("pay_fixed", "PAY_FIXED")]
    [InlineData("R", "REC_FIXED")]
    [InlineData("Rec", "REC_FIXED")]
    [InlineData("ReceiveFixed", "REC_FIXED")]
    [InlineData("REC_FIXED", "REC_FIXED")]
    public void Each_known_code_resolves_whatever_its_case(string code, string side)
    {
        var row = Resolve.Apply(("Dir", code), ("Qty", "100"));

        Assert.Equal(side, row.Display("Side"));
        Assert.Equal(new TraceStep("side.resolve", "Side", code.Trim(), side), Assert.Single(row.Traces));
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("-5", null, "SELL")]
    [InlineData("5", null, "BUY")]
    [InlineData("-5", "Swap", "PAY_FIXED")]
    [InlineData("5", "Swap", "REC_FIXED")]
    [InlineData("5", "Bond", "BUY")]
    [InlineData("-5", "swap", "SELL")]
    public void With_no_code_the_quantity_sign_and_asset_class_decide(string quantity, string? asset, string side)
    {
        var row = Resolve.Apply(WithAsset(RuleBench.Row(("Dir", ""), ("Qty", quantity)), asset));

        Assert.Equal(side, row.Display("Side"));
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("B", "5", "Swap", "BUY")]
    [InlineData("S", "-5", "Swap", "SELL")]
    [InlineData("S", "5", null, "SELL")]
    [InlineData("P", "5", null, "PAY_FIXED")]
    [InlineData("S", "", null, "SELL")]
    public void A_code_wins_over_the_sign_and_the_asset_class(string code, string quantity, string? asset, string side)
    {
        var row = Resolve.Apply(WithAsset(RuleBench.Row(("Dir", code), ("Qty", quantity)), asset));

        Assert.Equal(side, row.Display("Side"));
        Assert.Empty(row.Diagnostics);
    }

    // A buy or a receive-fixed cannot carry a negative quantity. A sell or pay-fixed may carry either sign.
    [Theory]
    [InlineData("B", "-5", "SideSignConflict")]
    [InlineData("R", "-0.01", "SideSignConflict")]
    [InlineData("", "0", "UndeterminedSide")]
    [InlineData("", "", "UndeterminedSide")]
    [InlineData("", "abc", "UndeterminedSide")]
    [InlineData("X", "5", "UndeterminedSide")]
    [InlineData("BUYX", "5", "UndeterminedSide")]
    public void A_side_that_cannot_be_decided_is_Invalid_and_not_written(string code, string quantity, string expected)
    {
        var row = Resolve.Apply(("Dir", code), ("Qty", quantity));

        Assert.False(row.Values.ContainsKey("Side"));
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal((expected, DiagnosticSeverity.Invalid, "side.resolve", "Side", code), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Fact]
    public void A_missing_code_column_is_no_code() =>
        Assert.Equal("SELL", Resolve.Apply(("Qty", "-5")).Display("Side"));

    [Theory]
    [InlineData("B", "SELL")]
    [InlineData("S", "BUY")]
    [InlineData("P", "REC_FIXED")]
    [InlineData("R", "PAY_FIXED")]
    public void The_side_is_inverted_when_the_perspective_column_names_invertWhen(string code, string side)
    {
        var row = Resolve.With("invertWhen", "DEALER").Apply(("Dir", code), ("Qty", "5"), ("Reporting_Perspective", "DEALER"));

        Assert.Equal(side, row.Display("Side"));
    }

    [Theory]
    [InlineData("CLIENT")]
    [InlineData("dealer")]
    [InlineData("")]
    public void The_side_is_kept_when_the_perspective_is_anything_else(string perspective)
    {
        var row = Resolve.With("invertWhen", "DEALER").Apply(("Dir", "B"), ("Qty", "5"), ("Reporting_Perspective", perspective));

        Assert.Equal("BUY", row.Display("Side"));
    }

    [Fact]
    public void A_declared_perspective_column_replaces_the_default()
    {
        var rule = Resolve.With("invertWhen", "DEALER").With("perspective", "View");

        Assert.Equal("SELL", rule.Apply(("Dir", "B"), ("Qty", "5"), ("View", "DEALER")).Display("Side"));
        Assert.Equal("BUY", rule.Apply(("Dir", "B"), ("Qty", "5"), ("Reporting_Perspective", "DEALER")).Display("Side"));
    }

    // The sign is checked against the code as the file states it, before the perspective inverts the side.
    [Fact]
    public void A_sign_conflict_is_reported_before_inversion()
    {
        var row = Resolve.With("invertWhen", "DEALER").Apply(("Dir", "B"), ("Qty", "-5"), ("Reporting_Perspective", "DEALER"));

        Assert.Equal("SideSignConflict", Assert.Single(row.Diagnostics).Code);
    }

    [Fact]
    public void A_canonical_quantity_stands_in_for_a_missing_raw_column()
    {
        var row = Resolve.Apply(RuleBench.Row(("Dir", "")).Write("Qty", CanonicalCell.FromDecimal(-5m), "decimal.parse", null, "-5"));

        Assert.Equal("SELL", row.Display("Side"));
    }

    [Fact]
    public void A_readable_raw_quantity_wins_over_a_canonical_one()
    {
        var row = Resolve.Apply(RuleBench.Row(("Dir", ""), ("Qty", "5")).Write("Qty", CanonicalCell.FromDecimal(-5m), "decimal.parse", null, "-5"));

        Assert.Equal("BUY", row.Display("Side"));
    }

    [Fact]
    public void A_declared_asset_field_replaces_the_default()
    {
        var row = Resolve.With("asset", "Product").Apply(
            RuleBench.Row(("Dir", ""), ("Qty", "5")).Write("Product", CanonicalCell.FromText("Swap"), "text.copy", "Swap", "Swap"));

        Assert.Equal("REC_FIXED", row.Display("Side"));
        Assert.Equal(new FieldId("Product"), Assert.Single(Resolve.With("asset", "Product").Build().Reads));
    }

    // The asset class is a canonical field, as symbol.map or text.copy writes it before side.resolve runs.
    private static RowContext WithAsset(RowContext row, string? asset) =>
        asset is null ? row : row.Write("AssetClass", CanonicalCell.FromText(asset), "text.copy", asset, asset);
}
