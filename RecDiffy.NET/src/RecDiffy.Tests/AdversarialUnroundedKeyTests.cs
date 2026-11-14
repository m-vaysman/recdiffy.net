using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Spec §7.3: every Decimal field has a scale and values are rounded with the profile's
// midpoint rule. quantity.absolute and price.from32nds write their value unrounded; a match key prints it with
// "F<scale>", which rounds half away from zero, while decimal.parse on the other side has already rounded ToEven.
public class AdversarialUnroundedKeyTests
{
    private const string QuantityProfile = """
        {
          "formatVersion": 1,
          "profileId": "adv-unrounded-quantity",
          "version": "1.0.0",
          "culture": "",
          "rulePacks": ["trading@1"],
          "rounding": { "midpoint": "ToEven" },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "derive": [ { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Ref", "Quantity"] } ] } ]
        }
        """;

    private const string PriceProfile = """
        {
          "formatVersion": 1,
          "profileId": "adv-unrounded-price",
          "version": "1.0.0",
          "culture": "",
          "rulePacks": ["trading@1"],
          "rounding": { "midpoint": "ToEven" },
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Price", "type": "Decimal", "scale": 4, "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Px", "to": "Price" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "price.from32nds", "from": "Px", "to": "Price" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Ref", "Price"] } ] } ]
        }
        """;

    // Left reads 100.125 at scale 2 ToEven: 100.12. Right's quantity.absolute keeps -100.125 as 100.125.
    [Fact]
    public void An_absolute_quantity_is_rounded_to_its_field_scale_before_it_is_keyed()
    {
        var result = Run(QuantityProfile, "Qty", "100.125", "-100.125");

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    // Left reads 100.03125 at scale 4 ToEven: 100.0312. Right's 32nds "100-01" is 100.03125, unrounded.
    [Fact]
    public void A_32nds_price_is_rounded_to_its_field_scale_and_the_profile_midpoint_before_it_is_keyed()
    {
        var result = Run(PriceProfile, "Px", "100.03125", "100-01");

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    private static IReconciliationResult Run(string profile, string column, string left, string right)
    {
        var map = new SourceMap<(string Id, string Value)>().Text("Id", row => row.Id).Text("Ref", row => row.Id).Text(column, row => row.Value);
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RecDiffy.RulePacks.Trading.TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromObjects<(string Id, string Value)>("left", [("T1", left)], map),
            RecordSources.FromObjects<(string Id, string Value)>("right", [("T1", right)], map),
            ReferenceDataSet.Empty);
    }
}
