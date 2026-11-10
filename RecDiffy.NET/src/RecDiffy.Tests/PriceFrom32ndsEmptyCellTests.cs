using System.Globalization;
using RecDiffy.RulePacks.Trading;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// An empty price cell is blank, as it is for decimal.parse, date.parse and quantity.absolute: a null price with no
// diagnostic. price.from32nds reads its cell through WhenMatcher.Text, which keeps "" where a null cell gives null, and
// only a null is blank to Treasury32nds.Apply. So "" is PriceParse, and a FromObjects row with an empty price is quarantined (#274).
public class PriceFrom32ndsEmptyCellTests
{
    private const string Skip = "#274: Treasury32nds.Apply treats only a null cell as blank, so an empty price cell is PriceParse.";

    [Fact(Skip = Skip)]
    public void An_empty_price_cell_is_a_silent_null()
    {
        var row = new RuleBench("price.from32nds").With("from", "Px").With("to", "Price").Apply(("Px", ""));

        Assert.True(row.Values["Price"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact(Skip = Skip)]
    public void A_row_with_an_empty_price_is_not_quarantined()
    {
        var registry = TradingRulePack.AddTo(RuleRegistry.CreateCore());
        var built = ProfileBuilder.Create("price-blank", "1")
            .Culture(CultureInfo.InvariantCulture)
            .RowKeys("Id", "Id")
            .Key("Id", FieldType.Identifier)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Left(side => side.MapIdentifier("Id", "Id", "ID").From32nds("Px", "Price"))
            .Right(side => side.MapIdentifier("Id", "Id", "ID").From32nds("Px", "Price"))
            .Path("all", path => path.NaturalKey("P1", "Id"))
            .Build(registry);
        var map = new SourceMap<(string Id, string Px)>().Text("Id", row => row.Id).Text("Px", row => row.Px);

        var result = Reconciler.Run(
            Reconciler.Compile(built.Document ?? throw new InvalidOperationException("The profile did not build."), registry),
            RecordSources.FromObjects<(string Id, string Px)>("left", [("T1", "")], map),
            RecordSources.FromObjects<(string Id, string Px)>("right", [("T1", "")], map),
            ReferenceDataSet.Empty);

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }
}
