using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class ProfileCheckTests
{
    private static readonly string[] Left = ["Id", "Sku", "Quantity", "Trade_Date", "Dir", "Region"];

    [Fact]
    public void An_abbreviated_column_suggests_the_full_header()
    {
        var missing = Check(Profile(side => side.ParseDecimal("Qty", "Quantity")), right: ["Id", "Sku", "Quantity"]);

        var column = Assert.Single(missing);
        Assert.Equal(new MissingColumn(Side.Right, "Qty", "normalization.right.parse[0].from", "Quantity"), column);
        Assert.Equal("'Qty' not found in right; did you mean 'Quantity'?", column.Message);
    }

    [Fact]
    public void Case_spaces_and_punctuation_do_not_stop_a_suggestion()
    {
        var missing = Check(Profile(side => side.ParseDate("trade date", "Day", "yyyy-MM-dd")), right: ["Id", "Sku", "Trade_Date"]);

        Assert.Equal("Trade_Date", Assert.Single(missing).Suggestion);
    }

    [Fact]
    public void A_small_misspelling_suggests_the_header()
    {
        var missing = Check(Profile(side => side.ParseDecimal("Quantiy", "Quantity")), right: ["Id", "Sku", "Quantity"]);

        Assert.Equal("Quantity", Assert.Single(missing).Suggestion);
    }

    [Fact]
    public void Nothing_close_gives_no_suggestion()
    {
        var missing = Check(Profile(side => side.ParseDecimal("Notional", "Quantity")), right: ["Id", "Sku", "Price"]);

        var column = Assert.Single(missing);
        Assert.Null(column.Suggestion);
        Assert.Equal("'Notional' not found in right.", column.Message);
    }

    [Fact]
    public void Headers_match_exactly_as_the_engine_reads_them()
    {
        var missing = Check(Profile(side => side.ParseDecimal("quantity", "Quantity")), right: ["Id", "Sku", "Quantity"]);

        Assert.Equal("Quantity", Assert.Single(missing).Suggestion);
    }

    [Fact]
    public void The_row_key_is_checked()
    {
        var missing = Check(Profile(side => side), right: ["ID", "Sku"]);

        Assert.Equal(new MissingColumn(Side.Right, "Id", "sources.right.rowKey", "ID"), Assert.Single(missing));
    }

    [Fact]
    public void A_when_field_is_a_column_unless_it_is_a_declared_field()
    {
        var missing = Check(
            Profile(side => side.ParseDecimal("Quantity", "Quantity", Condition.Equal("Venue", "XLON")).ParseDecimal("Quantity", "Quantity", Condition.Equal("Sku", "A"))),
            right: ["Id", "Sku", "Quantity"]);

        Assert.Equal(new MissingColumn(Side.Right, "Venue", "normalization.right.parse[0].when.field", null), Assert.Single(missing));
    }

    [Fact]
    public void A_column_read_twice_is_reported_once_at_its_first_reader_in_phase_order()
    {
        var missing = Check(Profile(side => side.ParseDecimal("Qty", "Quantity").CopyText("Qty", "Note")), right: ["Id", "Sku"]);

        Assert.Equal("normalization.right.identify[1].from", Assert.Single(missing).Location);
    }

    [Fact]
    public void Each_side_is_checked_against_its_own_headers_in_profile_order()
    {
        var document = Built(Builder()
            .Left(side => side.MapSymbol("Sku", "Sku").ParseDecimal("Quantity", "Quantity"))
            .Right(side => side.MapSymbol("Code", "Sku").ParseDecimal("Amt", "Quantity")));

        var missing = ProfileCheck.AgainstHeaders(document, ["Id", "Sku"], ["Id", "Sku", "Quantity"]);

        Assert.Equal(
            [new(Side.Left, "Quantity", "normalization.left.parse[0].from", null), new(Side.Right, "Code", "normalization.right.identify[0].from", null), new(Side.Right, "Amt", "normalization.right.parse[0].from", null)],
            missing);
    }

    // Only "from" is a column for every rule. side.resolve reads its code, quantity and perspective columns, which the
    // trading pack names; without them the check cannot know those parameters are columns.
    [Fact]
    public void A_rule_pack_names_its_column_parameters()
    {
        var document = Built(Builder()
            .Key("Side", FieldType.Symbol)
            .Right(side => side.MapSymbol("Sku", "Sku").ParseDecimal("Quantity", "Quantity").ResolveSide(code: "Direction", quantity: "Qty", perspective: "View", invertWhen: "DEALER", asset: "Sku")));
        string[] right = ["Id", "Sku", "Quantity", "Dir"];

        Assert.Empty(ProfileCheck.AgainstHeaders(document, Left, right));
        Assert.Equal(
            [new(Side.Right, "Direction", "normalization.right.derive[0].code", "Dir"), new(Side.Right, "Qty", "normalization.right.derive[0].quantity", "Quantity"), new(Side.Right, "View", "normalization.right.derive[0].perspective", null)],
            ProfileCheck.AgainstHeaders(document, Left, right, TradingColumns.Parameters));
    }

    [Fact]
    public void Ties_go_to_the_ordinally_smaller_header()
    {
        var missing = Check(Profile(side => side.ParseDecimal("Amt", "Quantity")), right: ["Id", "Sku", "AmtB", "AmtA"]);

        Assert.Equal("AmtA", Assert.Single(missing).Suggestion);
    }

    private static IReadOnlyList<MissingColumn> Check(ProfileDocument document, string[] right) =>
        ProfileCheck.AgainstHeaders(document, Left, right);

    private static ProfileDocument Profile(Func<SideBuilder, SideBuilder> right) =>
        Built(Builder().Field("Day", FieldType.Date, FieldRole.Economic).Field("Note", FieldType.Text, FieldRole.Text).Right(side => right(side.MapSymbol("Sku", "Sku"))));

    private static ProfileBuilder Builder() =>
        ProfileBuilder.Create("check", "1.0.0")
            .RowKeys(left: "Id", right: "Id")
            .Key("Sku", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 2)
            .Path("all", path => path.NaturalKey("P1", "Sku"));

    private static ProfileDocument Built(ProfileBuilder builder) =>
        builder.Build() is { Succeeded: true } result ? result.Document : throw new InvalidOperationException("The test profile does not build.");
}
