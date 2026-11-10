using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// Builds a rule by its registered name the way compile does: through the core registry and the trading pack, with one
// side's culture and the profile's midpoint, and with the rule's "to" field declared at Scale. Then applies it to a row.
internal sealed record RuleBench(string RuleId)
{
    private static readonly RuleRegistry Registry = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    public ImmutableDictionary<string, string> Strings { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public ImmutableDictionary<string, IReadOnlyList<string>> Lists { get; init; } = ImmutableDictionary.Create<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public ImmutableDictionary<string, IReadOnlyDictionary<string, string>> Maps { get; init; } =
        ImmutableDictionary.Create<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public MidpointMode Midpoint { get; init; } = MidpointMode.ToEven;

    public int Scale { get; init; } = 2;

    public RuleBench With(string name, string value) => this with { Strings = Strings.SetItem(name, value) };

    public RuleBench WithList(string name, params string[] values) => this with { Lists = Lists.SetItem(name, values) };

    public RuleBench WithMap(string name, params (string Key, string Value)[] pairs) =>
        this with { Maps = Maps.SetItem(name, pairs.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)) };

    public RuleBench In(string culture) => this with { Culture = CultureInfo.GetCultureInfo(culture) };

    public INormalizationRule Build()
    {
        var call = new RuleCall(RuleId, null, Strings, Lists, Maps);
        FieldDeclaration[] fields = Strings.TryGetValue("to", out var to) ? [new FieldDeclaration(to, FieldType.Decimal, FieldRole.Economic, Scale)] : [];
        var settings = new RuleSettings(Midpoint, Culture, StrictDateFormats: true);
        return Registry.TryGet(RuleId, out var registration)
            ? registration.CreateWithSettings(call, new FieldSchema(fields), settings)
            : throw new InvalidOperationException($"{RuleId} is not registered.");
    }

    public RowContext Apply(params (string Column, object? Value)[] cells) => Apply(Row(cells));

    public RowContext Apply(RowContext row)
    {
        var next = row;
        Build().Apply(ref next);
        return next;
    }

    public static RowContext Row(params (string Column, object? Value)[] cells) =>
        RowContext.Create("L1", Side.Left, cells.ToDictionary(cell => cell.Column, cell => cell.Value, StringComparer.Ordinal));
}
