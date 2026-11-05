using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using RecDiffy.Rules;

namespace RecDiffy;

public sealed record RuleRegistry
{
    internal ImmutableDictionary<string, NormalizationRegistration> Normalization { get; init; } =
        ImmutableDictionary.Create<string, NormalizationRegistration>(StringComparer.Ordinal);

    public static RuleRegistry CreateCore() =>
        new RuleRegistry()
            .Register("decimal.parse", RulePhase.Parse, ["from", "to"], (call, schema, midpoint) =>
                new DecimalParseRule(call.Strings["from"], call.Strings["to"], schema.Scale(call.Strings["to"]), midpoint))
            .Register("date.parse", RulePhase.Parse, ["from", "to"], (call, _, _) =>
                new DateParseRule(call.Strings["from"], call.Strings["to"], call.Lists.TryGetValue("formats", out var formats) ? formats : []))
            // Passthrough stays on unless the token is false. "False" does not turn it off.
            .Register("symbol.map", RulePhase.Identify, ["from", "to"], (call, _, _) =>
                new SymbolMapRule(
                    call.Strings["from"],
                    call.Strings["to"],
                    call.Maps.TryGetValue("map", out var map) ? map : ImmutableDictionary<string, string>.Empty,
                    call.Strings.TryGetValue("passthrough", out var passthrough) ? passthrough is not "false" : true))
            .Register("identifier.map", RulePhase.Identify, ["from", "to", "scheme"], (call, _, _) =>
                new IdentifierMapRule(call.Strings["from"], call.Strings["to"], call.Strings["scheme"], call.Strings.GetValueOrDefault("scope") ?? ""))
            .Register("text.copy", RulePhase.Identify, ["from", "to"], (call, _, _) =>
                new TextCopyRule(call.Strings["from"], call.Strings["to"]))
            .Register("required.present", RulePhase.Validate, ["fields"], (call, _, _) =>
                new RequiredPresentRule(call.Lists["fields"]));

    internal RuleRegistry Register(
        string ruleId,
        RulePhase phase,
        IReadOnlyList<string> required,
        Func<RuleCall, FieldSchema, MidpointMode, INormalizationRule> create) =>
        // A second registration of the same id throws.
        this with { Normalization = Normalization.Add(ruleId, new NormalizationRegistration(ruleId, phase, required, create)) };

    internal bool TryGet(string ruleId, [NotNullWhen(true)] out NormalizationRegistration? registration) =>
        Normalization.TryGetValue(ruleId, out registration);
}

internal sealed record NormalizationRegistration(
    string RuleId,
    RulePhase Phase,
    IReadOnlyList<string> Required,
    Func<RuleCall, FieldSchema, MidpointMode, INormalizationRule> Create);

internal sealed class FieldSchema(IReadOnlyList<FieldDeclaration> fields)
{
    private readonly ImmutableDictionary<string, FieldDeclaration> _fields = Index(fields);

    private static ImmutableDictionary<string, FieldDeclaration> Index(IReadOnlyList<FieldDeclaration> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        // Ordinal names. "Quantity" and "quantity" are different fields.
        return fields.ToImmutableDictionary(field => field.Name, StringComparer.Ordinal);
    }

    public bool TryGet(string name, [NotNullWhen(true)] out FieldDeclaration? field) => _fields.TryGetValue(name, out field);

    public int Scale(string name) => _fields[name].Scale;
}
