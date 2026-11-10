using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

public sealed record RuleRegistry
{
    internal ImmutableDictionary<string, NormalizationRegistration> Normalization { get; init; } =
        ImmutableDictionary.Create<string, NormalizationRegistration>(StringComparer.Ordinal);

    public static RuleRegistry CreateCore() =>
        new RuleRegistry()
            .RegisterWithSettings("decimal.parse", RulePhase.Parse, ["from", "to"], (call, schema, settings) =>
                new CultureDecimalRule(call.Strings["from"], call.Strings["to"], schema.Scale(call.Strings["to"]), settings.Midpoint, settings.Culture.NumberFormat))
            .RegisterWithSettings("date.parse", RulePhase.Parse, ["from", "to"], (call, _, settings) =>
                new DeclaredDateRule(
                    call.Strings["from"],
                    call.Strings["to"],
                    call.Lists.TryGetValue("formats", out var formats) ? formats : [],
                    call.Strings.GetValueOrDefault("formatBy"),
                    settings.StrictDateFormats ? CultureDateParsing.Strict(settings.Culture) : settings.Culture))
            // Only the ordinal string "false" turns passthrough off. A missing parameter leaves it on.
            .Register("symbol.map", RulePhase.Identify, ["from", "to"], (call, _, _) =>
                new InvariantSymbolMapRule(
                    call.Strings["from"],
                    call.Strings["to"],
                    call.Maps.TryGetValue("map", out var map) ? map : ImmutableDictionary<string, string>.Empty,
                    call.Strings.TryGetValue("passthrough", out var passthrough) ? passthrough is not "false" : true))
            // A missing scope is an empty scope. The two share one intern bucket.
            .Register("identifier.map", RulePhase.Identify, ["from", "to", "scheme"], (call, _, _) =>
                new InvariantIdentifierMapRule(call.Strings["from"], call.Strings["to"], call.Strings["scheme"], call.Strings.GetValueOrDefault("scope") ?? ""))
            .Register("text.copy", RulePhase.Identify, ["from", "to"], (call, _, _) =>
                new InvariantTextCopyRule(call.Strings["from"], call.Strings["to"]))
            .Register("required.present", RulePhase.Validate, ["fields"], (call, _, _) =>
                new RequiredPresentRule(call.Lists["fields"]));

    internal RuleRegistry Register(
        string ruleId,
        RulePhase phase,
        IReadOnlyList<string> required,
        Func<RuleCall, FieldSchema, MidpointMode, INormalizationRule> create) =>
        RegisterWithSettings(ruleId, phase, required, (call, schema, settings) => create(call, schema, settings.Midpoint));

    internal RuleRegistry RegisterWithSettings(
        string ruleId,
        RulePhase phase,
        IReadOnlyList<string> required,
        Func<RuleCall, FieldSchema, RuleSettings, INormalizationRule> create) =>
        this with { Normalization = Normalization.Add(ruleId, new NormalizationRegistration(ruleId, phase, required, create)) };

    internal bool TryGet(string ruleId, [NotNullWhen(true)] out NormalizationRegistration? registration) =>
        Normalization.TryGetValue(ruleId, out registration);
}

// What a side's rules are built with: the profile's midpoint, and the side's culture and date strictness, resolved once at compile.
internal sealed record RuleSettings(MidpointMode Midpoint, CultureInfo Culture, bool StrictDateFormats)
{
    public static RuleSettings Invariant(MidpointMode midpoint) => new(midpoint, CultureInfo.InvariantCulture, true);
}

internal sealed record NormalizationRegistration(
    string RuleId,
    RulePhase Phase,
    IReadOnlyList<string> Required,
    Func<RuleCall, FieldSchema, RuleSettings, INormalizationRule> CreateWithSettings)
{
    public INormalizationRule Create(RuleCall call, FieldSchema schema, MidpointMode midpoint) =>
        CreateWithSettings(call, schema, RuleSettings.Invariant(midpoint));
}

internal sealed class FieldSchema(IReadOnlyList<FieldDeclaration> fields)
{
    private readonly ImmutableDictionary<string, FieldDeclaration> _fields = Index(fields);

    private static ImmutableDictionary<string, FieldDeclaration> Index(IReadOnlyList<FieldDeclaration> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.ToImmutableDictionary(field => field.Name, StringComparer.Ordinal);
    }

    public bool TryGet(string name, [NotNullWhen(true)] out FieldDeclaration? field) => _fields.TryGetValue(name, out field);

    public int Scale(string name) => _fields[name].Scale;
}
