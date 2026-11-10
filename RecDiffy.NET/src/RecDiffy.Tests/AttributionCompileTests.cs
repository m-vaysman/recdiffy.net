using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class AttributionCompileTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "attribution-compile",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Accrued": { "attributed": "trading.proRataToQuantity" },
              "NetCash": { "tolerance": { "attributed": "trading.netCash" } }
            } }
          } ]
        }
        """;

    private const string QuantityField = """{ "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },""";

    private const string PriceField = """{ "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },""";

    private const string AccruedField = """{ "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },""";

    private const string FeesField = """{ "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },""";

    private const string AccruedDiff = "\"Accrued\": { \"attributed\": \"trading.proRataToQuantity\" },";

    private static readonly RuleRegistry Trading = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    [Fact]
    public void A_profile_whose_attributions_are_registered_and_whose_roots_are_declared_compiles() =>
        Assert.Equal("attribution-compile", Reconciler.Compile(ProfileDocument.Parse(Profile), Trading).ProfileId);

    [Fact]
    public void An_attribution_name_that_is_not_registered_fails_compile() =>
        Assert.Equal(
            [("UnknownRule", "Path 'p' attributes 'Accrued' by 'trading.proRata', which is not registered.")],
            Errors(Profile.Replace("\"trading.proRataToQuantity\"", "\"trading.proRata\"", StringComparison.Ordinal), Trading));

    [Fact]
    public void A_tolerance_attribution_name_is_matched_ordinally() =>
        Assert.Equal(
            [("UnknownRule", "Path 'p' attributes 'NetCash' by 'trading.NetCash', which is not registered.")],
            Errors(Profile.Replace("\"trading.netCash\"", "\"trading.NetCash\"", StringComparison.Ordinal), Trading));

    [Fact]
    public void The_trading_attributions_are_not_registered_without_the_trading_pack() =>
        Assert.Equal(
            [
                ("UnknownRule", "Path 'p' attributes 'Accrued' by 'trading.proRataToQuantity', which is not registered."),
                ("UnknownRule", "Path 'p' attributes 'NetCash' by 'trading.netCash', which is not registered."),
            ],
            Errors(Profile, RuleRegistry.CreateCore()));

    [Fact]
    public void An_undeclared_Quantity_fails_both_attributions_that_need_it() =>
        Assert.Equal(
            [
                ("UnknownField", "Path 'p' attributes 'Accrued' by 'trading.proRataToQuantity', whose root 'Quantity' is not declared."),
                ("UnknownField", "Path 'p' attributes 'NetCash' by 'trading.netCash', whose root 'Quantity' is not declared."),
            ],
            Errors(Without(Profile, QuantityField), Trading));

    [Fact]
    public void Net_cash_needs_a_declared_Price() =>
        Assert.Equal(
            [("UnknownField", "Path 'p' attributes 'NetCash' by 'trading.netCash', whose root 'Price' is not declared.")],
            Errors(Without(Profile, PriceField), Trading));

    [Fact]
    public void Net_cash_compiles_without_Accrued_or_Fees() =>
        Assert.Equal(
            "attribution-compile",
            Reconciler.Compile(ProfileDocument.Parse(Without(Without(Without(Profile, AccruedField), FeesField), AccruedDiff)), Trading).ProfileId);

    [Fact]
    public void Registering_the_attributions_keeps_the_profile_hash() =>
        Assert.Equal("1bac58ed17988fe707f20c00a16c6cc957cc1bbb955852aa49990b86896aea40", Reconciler.Compile(ProfileDocument.Parse(Profile), Trading).Hash);

    private static string Without(string profile, string text)
    {
        Assert.Contains(text, profile, StringComparison.Ordinal);
        return profile.Replace(text, "", StringComparison.Ordinal);
    }

    private static (string Code, string Message)[] Errors(string profile, RuleRegistry registry) =>
        [.. Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(ProfileDocument.Parse(profile), registry)).Errors.Select(error => (error.Code, error.Message))];
}
