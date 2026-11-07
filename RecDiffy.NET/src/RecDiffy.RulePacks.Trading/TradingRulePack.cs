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
            .Register("side.resolve", RulePhase.Derive, ["code", "quantity"], (call, _, _) =>
                new SideResolveRule(call.Strings["code"], call.Strings["quantity"], call.Strings.GetValueOrDefault("invertWhen"), call.Strings.GetValueOrDefault("perspective") ?? "Reporting_Perspective", call.Strings.GetValueOrDefault("asset") ?? "AssetClass"))
            .Register("quantity.absolute", RulePhase.Derive, ["from", "to"], (call, _, _) => new AbsoluteQuantityRule(call.Strings["from"], call.Strings["to"]))
            .Register("price.from32nds", RulePhase.Parse, ["from", "to"], (call, _, _) => new Treasury32ndsRule(call.Strings["from"], call.Strings["to"]));
    }
}
