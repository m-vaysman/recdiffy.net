namespace RecDiffy.RulePacks.Trading;

public static class TradingRulePack
{
    // The trading pack's scope is the registration hook.
    public static RuleRegistry AddTo(RuleRegistry registry) => registry;
}
