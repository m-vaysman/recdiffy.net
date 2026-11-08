using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

public static class TradingRulePack
{
    public static RuleRegistry AddTo(RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry
            .Register("party.resolveLei", RulePhase.Identify, ["from", "to"], (call, _, _) => new PartyLeiRule(call.Strings["from"], call.Strings["to"]))
            .Register("party.resolveAlias", RulePhase.Identify, ["from", "to"], (call, _, _) => new PartyAliasRule(call.Strings["from"], call.Strings["to"]))
            .RegisterWithSettings("side.resolve", RulePhase.Derive, ["code", "quantity"], (call, _, settings) =>
                new CultureSideResolveRule(call.Strings["code"], call.Strings["quantity"], call.Strings.GetValueOrDefault("invertWhen"), call.Strings.GetValueOrDefault("perspective") ?? "Reporting_Perspective", call.Strings.GetValueOrDefault("asset") ?? "AssetClass", settings.Culture.NumberFormat))
            .RegisterWithSettings("quantity.absolute", RulePhase.Derive, ["from", "to"], (call, schema, settings) => new ScaledQuantityRule(call.Strings["from"], call.Strings["to"], settings.Culture.NumberFormat, schema.Scale(call.Strings["to"]), settings.Midpoint))
            .RegisterWithSettings("price.from32nds", RulePhase.Parse, ["from", "to"], (call, schema, settings) => new Scaled32ndsRule(call.Strings["from"], call.Strings["to"], schema.Scale(call.Strings["to"]), settings.Midpoint));
    }
}
