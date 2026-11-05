using RecDiffy.Engine;

namespace RecDiffy.RulePacks.Trading;

internal sealed class SameAggregator : IAggregator
{
    public void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure)
    {
        failure = null;
        CanonicalValue? first = null;
        foreach (var member in members)
        {
            if (!member.Values.TryGetValue(field, out var value))
                value = new CanonicalValue(state.TypeOf(field), true, 0m, default, 0);
            if (first is null)
            {
                first = value;
                continue;
            }
            if (!Same(first.Value, value))
            {
                failure = "PackageInconsistent";
                return;
            }
        }
        if (first is CanonicalValue chosen)
            target.Values[field] = chosen;
    }

    private static bool Same(CanonicalValue left, CanonicalValue right) =>
        left.IsNull == right.IsNull && left.Number == right.Number && left.Date == right.Date && left.Interned == right.Interned;
}

internal sealed class SumAggregator : IAggregator
{
    public void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure)
    {
        failure = null;
        decimal sum = 0m;
        var any = false;
        foreach (var member in members)
        {
            if (member.Values.TryGetValue(field, out var value) && !value.IsNull)
            {
                sum += value.Number;
                any = true;
            }
        }
        target.Values[field] = any
            ? new CanonicalValue(FieldType.Decimal, false, Rounding.Round(sum, state.ScaleOf(field), state.Midpoint), default, 0)
            : new CanonicalValue(FieldType.Decimal, true, 0m, default, 0);
    }
}

internal sealed class FromRoleAggregator : IAggregator
{
    private readonly string _role;
    public FromRoleAggregator(string role) => _role = role;
    public void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure)
    {
        failure = null;
        var member = members.FirstOrDefault(row => Role(row, state) == _role);
        if (member is null)
        {
            failure = "IncompletePackage";
            return;
        }
        if (member.Values.TryGetValue(field, out var value))
            target.Values[field] = value;
    }

    internal static string? Role(MutableRow row, RunState state) =>
        row.Values.TryGetValue("LegRole", out var role) && !role.IsNull ? state.Pool.Symbol(role.Interned) : null;
}

internal sealed class DifferenceAggregator : IAggregator
{
    private readonly string _left;
    private readonly string _right;
    public DifferenceAggregator(string left, string right) { _left = left; _right = right; }
    public void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure)
    {
        failure = null;
        var left = members.FirstOrDefault(row => FromRoleAggregator.Role(row, state) == _left);
        var right = members.FirstOrDefault(row => FromRoleAggregator.Role(row, state) == _right);
        if (left is null || right is null || !left.Values.TryGetValue(field, out var a) || !right.Values.TryGetValue(field, out var b) || a.IsNull || b.IsNull)
        {
            failure = "IncompletePackage";
            return;
        }
        target.Values[field] = new CanonicalValue(FieldType.Decimal, false, Rounding.Round(a.Number - b.Number, state.ScaleOf(field), state.Midpoint), default, 0);
    }
}

internal sealed class PackageByLinkFactory : IPackageFactory
{
    public string RuleId => "package.byLink";
    public PackageBinding Bind(RuleArguments args, RuleRegistry registry)
    {
        var aggregates = new Dictionary<string, IAggregator>(StringComparer.Ordinal);
        foreach (var pair in args.Map("aggregate"))
            aggregates[pair.Key] = Create(pair.Value, registry);
        return new PackageBinding
        {
            RuleId = RuleId,
            LinkField = args.Required("link"),
            RoleField = args.Required("role"),
            RequiredRoles = args.List("requiredRoles"),
            Aggregates = aggregates,
        };
    }

    private static IAggregator Create(string expression, RuleRegistry registry)
    {
        var open = expression.IndexOf('(');
        if (open < 0)
            return registry.Aggregators[expression].Create([]);
        var name = expression[..open];
        var body = expression[(open + 1)..^1];
        var args = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return registry.Aggregators[name].Create(args);
    }
}

internal sealed class SameFactory : IAggregatorFactory
{
    public string Name => "Same";
    public IAggregator Create(IReadOnlyList<string> args) => new SameAggregator();
}

internal sealed class SumFactory : IAggregatorFactory
{
    public string Name => "Sum";
    public IAggregator Create(IReadOnlyList<string> args) => new SumAggregator();
}

internal sealed class FromRoleFactory : IAggregatorFactory
{
    public string Name => "FromRole";
    public IAggregator Create(IReadOnlyList<string> args) => new FromRoleAggregator(args[0]);
}

internal sealed class DifferenceFactory : IAggregatorFactory
{
    public string Name => "DifferenceByRole";
    public IAggregator Create(IReadOnlyList<string> args) => new DifferenceAggregator(args[0], args[1]);
}

public static class TradingPack
{
    public static RuleRegistry AddTradingRules(this RuleRegistry registry) => TradingRules.AddTrading(registry);
}

internal static class TradingRules
{
    public static RuleRegistry AddTrading(this RuleRegistry registry)
    {
        registry.Add(new PartyResolveLeiFactory());
        registry.Add(new PartyResolveAliasFactory());
        registry.Add(new InstrumentResolveFactory());
        registry.Add(new TradeRefClassifyFactory());
        registry.Add(new PriceFrom32ndsFactory());
        registry.Add(new DirtyPriceFactory());
        registry.Add(new SideResolveFactory());
        registry.Add(new QuantityAbsoluteFactory());
        registry.Add(new FeesFromNetFactory());
        registry.Add(new CashPerspectiveFactory());
        registry.AddLifecycle(new NewCancCorrFactory());
        registry.AddPackage(new PackageByLinkFactory());
        registry.AddAggregator(new SameFactory());
        registry.AddAggregator(new SumFactory());
        registry.AddAggregator(new FromRoleFactory());
        registry.AddAggregator(new DifferenceFactory());
        return registry;
    }
}
