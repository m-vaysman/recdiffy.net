using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

public static class TradingRulePack
{
    public static RuleRegistry AddTo(RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry
            .Register("party.resolveLei", RulePhase.Identify, ["from", "to"], (call, _, _) => new PartyLeiRule(call.Strings["from"], call.Strings["to"]))
            .Register("party.resolveAlias", RulePhase.Identify, ["from", "to"], (call, _, _) => new FallbackPartyAliasRule(call.Strings["from"], call.Strings["to"]))
            .RegisterWithSettings("side.resolve", RulePhase.Derive, ["code", "quantity"], (call, _, settings) =>
                new CultureSideResolveRule(call.Strings["code"], call.Strings["quantity"], call.Strings.GetValueOrDefault("invertWhen"), call.Strings.GetValueOrDefault("perspective") ?? "Reporting_Perspective", call.Strings.GetValueOrDefault("asset") ?? "AssetClass", settings.Culture.NumberFormat))
            .RegisterWithSettings("quantity.absolute", RulePhase.Derive, ["from", "to"], (call, schema, settings) => new ScaledQuantityRule(call.Strings["from"], call.Strings["to"], settings.Culture.NumberFormat, schema.Scale(call.Strings["to"]), settings.Midpoint))
            .RegisterWithSettings("price.from32nds", RulePhase.Parse, ["from", "to"], (call, schema, settings) => new BlankAware32ndsRule(call.Strings["from"], call.Strings["to"], schema.Scale(call.Strings["to"]), settings.Midpoint))
            .Register("price.dirtyToClean", RulePhase.Derive, ["to"], (call, schema, midpoint) => new DirtyToCleanRule(call.Strings["to"], schema.Scale(call.Strings["to"]), midpoint))
            .Register("fees.inferFromNet", RulePhase.Derive, ["to"], (call, schema, midpoint) => new InferFeesFromNetRule(call.Strings["to"], schema.Scale(call.Strings["to"]), midpoint))
            .Register("cash.toPerspective", RulePhase.Derive, ["to", "invertWhen"], (call, _, _) =>
                new CashToPerspectiveRule(call.Strings["to"], call.Strings.GetValueOrDefault("perspective") ?? "Reporting_Perspective", call.Strings["invertWhen"]))
            .RegisterAttribution(new NetCashAttribution())
            .RegisterAttribution(new ProRataToQuantityAttribution());
    }
}
