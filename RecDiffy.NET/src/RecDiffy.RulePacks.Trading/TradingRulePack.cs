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
            .Register("fees.inferFromNet", RulePhase.Derive, ["to"], (call, schema, midpoint) => new InferFeesFromNetRule(call.Strings["to"], schema.Scale(call.Strings["to"]), midpoint, SignedCash(call)))
            .Register("cash.toPerspective", RulePhase.Derive, ["to", "perspective", "invertWhen"], (call, _, _) =>
                new CashToPerspectiveRule(call.Strings["to"], call.Strings["perspective"], call.Strings["invertWhen"]))
            .RegisterAttribution(new NetCashAttribution())
            .RegisterAttribution(new ProRataToQuantityAttribution());
    }

    // fees.inferFromNet reads unsigned cash unless the profile says "cash": "signed". Any other value is an error, not a default.
    private static bool SignedCash(RuleCall call) =>
        call.Strings.GetValueOrDefault("cash") switch
        {
            null or "unsigned" => false,
            "signed" => true,
            var other => throw new ProfileCompileException([new ProfileError("InvalidParameter", $"fees.inferFromNet cash must be \"signed\" or \"unsigned\", not \"{other}\".")]),
        };
}
