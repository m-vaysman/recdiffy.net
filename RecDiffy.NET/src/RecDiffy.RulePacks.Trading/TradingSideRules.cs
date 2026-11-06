using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.RulePacks.Trading;

/// <summary>The trading rule pack's rules for a <see cref="SideBuilder"/>. Each rule lands in the phase <see cref="TradingRulePack"/> registers it under.</summary>
public static class TradingSideRules
{
    /// <summary><c>party.resolveLei</c>: maps an LEI column to a counterparty code through reference data.</summary>
    public static SideBuilder ResolveLei(this SideBuilder side, string from, string to, WhenClause? when = null) =>
        Add(side, RulePhase.Identify, "party.resolveLei", when, ("from", from), ("to", to));

    /// <summary><c>party.resolveAlias</c>: maps a counterparty name or alias column to a code through reference data.</summary>
    public static SideBuilder ResolveAlias(this SideBuilder side, string from, string to, WhenClause? when = null) =>
        Add(side, RulePhase.Identify, "party.resolveAlias", when, ("from", from), ("to", to));

    /// <summary><c>price.from32nds</c>: parses a Treasury price quoted in 32nds.</summary>
    public static SideBuilder From32nds(this SideBuilder side, string from, string to, WhenClause? when = null) =>
        Add(side, RulePhase.Parse, "price.from32nds", when, ("from", from), ("to", to));

    /// <summary>
    /// <c>side.resolve</c>: writes <c>Side</c> from a direction code column and the sign of a quantity column. When the
    /// <paramref name="perspective"/> column equals <paramref name="invertWhen"/>, the side is inverted. <paramref name="asset"/>
    /// is the declared asset-class field (swaps resolve to PAY_FIXED or REC_FIXED). Omitted parameters take the rule's defaults.
    /// </summary>
    public static SideBuilder ResolveSide(
        this SideBuilder side,
        string code,
        string quantity,
        string? perspective = null,
        string? invertWhen = null,
        string? asset = null,
        WhenClause? when = null) =>
        Add(side, RulePhase.Derive, "side.resolve", when, ("code", code), ("quantity", quantity), ("perspective", perspective), ("invertWhen", invertWhen), ("asset", asset));

    /// <summary><c>quantity.absolute</c>: writes the absolute value of a quantity column.</summary>
    public static SideBuilder AbsoluteQuantity(this SideBuilder side, string from, string to, WhenClause? when = null) =>
        Add(side, RulePhase.Derive, "quantity.absolute", when, ("from", from), ("to", to));

    // The first two parameters of every rule here are required; the rest are optional and left out when null.
    private static SideBuilder Add(SideBuilder side, RulePhase phase, string rule, WhenClause? when, params (string Name, string? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(parameters[0].Value, parameters[0].Name);
        ArgumentNullException.ThrowIfNull(parameters[1].Value, parameters[1].Name);
        var strings = parameters
            .Where(parameter => parameter.Value is not null)
            .ToImmutableDictionary(parameter => parameter.Name, parameter => parameter.Value!, StringComparer.Ordinal);
        return side.Rule(phase, rule, strings, when: when);
    }
}

/// <summary>Which trading rule parameters name source columns, for <see cref="ProfileCheck.AgainstHeaders"/>. Every rule's <c>from</c> is a column already.</summary>
public static class TradingColumns
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Parameters { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["side.resolve"] = ["code", "quantity", "perspective"],
        }.ToImmutableDictionary(StringComparer.Ordinal);
}
