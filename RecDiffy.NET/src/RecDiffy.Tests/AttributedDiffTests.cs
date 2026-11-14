using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// #218. Spec v3 §6: "A derived field whose delta is fully explained by root fields takes the roots' worst outcome and
// lists them in AttributedTo (§7.4)." §10.1: the same rows and the same profile hash give the same outcomes and deltas.
public class AttributedDiffTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "attributed-diff",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Quantity": { "tolerance": { "relative": 0.0001 } },
              "Accrued": { "attributed": "trading.proRataToQuantity" },
              "NetCash": { "tolerance": { "attributed": "trading.netCash" } }
            } }
          } ]
        }
        """;

    private const string QuantityThenAccrued =
        "{ \"name\": \"Quantity\", \"type\": \"Decimal\", \"scale\": 2, \"role\": \"Economic\" },\n    { \"name\": \"Accrued\", \"type\": \"Decimal\", \"scale\": 2, \"role\": \"Economic\" },";

    private const string AccruedThenQuantity =
        "{ \"name\": \"Accrued\", \"type\": \"Decimal\", \"scale\": 2, \"role\": \"Economic\" },\n    { \"name\": \"Quantity\", \"type\": \"Decimal\", \"scale\": 2, \"role\": \"Economic\" },";

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Asset", row => "Bond").Text("Qty", row => row.Qty)
        .Text("Px", row => "100").Text("Acc", row => row.Acc).Text("Fee", row => "50").Text("Cash", row => row.Cash);

    private static readonly Row WithinLeft = new("L1", "1000000", "4784.72", "1004834.72");
    private static readonly Row WithinRight = new("R1", "1000050", "4784.96", "1004884.96");
    private static readonly Row G0144Left = new("L1", "1000000", "4784.72", "1004834.72");
    private static readonly Row G0144Right = new("R1", "800000", "3827.78", "803877.78");

    // Quantity moves by 50 on 1,000,000, inside its 0.01% tolerance. Accrued moves pro rata (4,784.72 x 1.00005 =
    // 4,784.96), and net cash is reproduced to the cent (1,000,050 + 4,784.96 + 50). Every root is within tolerance.
    [Fact]
    public void Net_cash_explained_by_roots_that_are_all_within_tolerance_is_not_a_break()
    {
        var result = Run(Profile, WithinLeft, WithinRight);

        var entry = Assert.Single(result.Entries);
        Assert.True(entry.Outcome is not FieldOutcome.Break, Describe(result));
    }

    // Net cash takes its roots' worst finished outcome: Accrued is within tolerance once attributed to Quantity.
    [Fact]
    public void Net_cash_takes_the_worst_finished_outcome_of_its_roots()
    {
        var result = Run(Profile, WithinLeft, WithinRight);

        Assert.Equal(
            "Matched/WithinTolerance Accrued:WithinTolerance:0.24:[Quantity], NetCash:WithinTolerance:50.24:[Quantity+Accrued], Quantity:WithinTolerance:50:[]",
            Describe(result));
    }

    // The spec's G0144 shape: quantity 1,000,000 against 800,000, accrued pro rata. Only the order of two field
    // declarations differs between the two profiles, and the profile hash is the same.
    [Fact]
    public void Declaring_Accrued_before_Quantity_does_not_change_the_diff()
    {
        var swapped = Swapped();
        Assert.False(string.Equals(Profile.ReplaceLineEndings("\n"), swapped, StringComparison.Ordinal));
        Assert.Equal(Compile(Profile).Hash, Compile(swapped).Hash);

        var quantityFirst = Describe(Run(Profile, G0144Left, G0144Right));
        var accruedFirst = Describe(Run(swapped, G0144Left, G0144Right));
        Assert.True(string.Equals(quantityFirst, accruedFirst, StringComparison.Ordinal), $"Quantity declared first: {quantityFirst} || Accrued declared first: {accruedFirst}");
    }

    // Either order gives the G0144 diff: Accrued breaks pro rata to Quantity, and net cash names both roots in one order.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_G0144_diff_attributes_accrued_and_net_cash_whatever_the_field_order(bool accruedFirst)
    {
        var result = Run(accruedFirst ? Swapped() : Profile, G0144Left, G0144Right);

        Assert.Equal(
            "Matched/Break Accrued:Break:-956.94:[Quantity], NetCash:Break:-200956.94:[Quantity+Accrued], Quantity:Break:-200000:[]",
            Describe(result));
    }

    private static string Swapped() =>
        Profile.ReplaceLineEndings("\n").Replace(QuantityThenAccrued, AccruedThenQuantity, StringComparison.Ordinal);

    private static string Describe(IReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry => $"{entry.Alignment}/{entry.Outcome} " + string.Join(", ", entry.Diffs
            .OrderBy(diff => diff.Field.Name, StringComparer.Ordinal)
            .Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}:[{string.Join("+", diff.AttributedTo)}]"))));

    private static CompiledProfile Compile(string profile) =>
        Reconciler.Compile(ProfileDocument.Parse(profile), TradingRulePack.AddTo(RuleRegistry.CreateCore()));

    private static IReconciliationResult Run(string profile, Row left, Row right) =>
        Reconciler.Run(Compile(profile), RecordSources.FromObjects("left", [left], Map), RecordSources.FromObjects("right", [right], Map), ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Qty, string Acc, string Cash);
}
