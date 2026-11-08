using System.Globalization;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// A one-key profile with a decimal and a date on each side. run, left and right splice reading declarations into the
// profile root and into sources.left and sources.right.
internal static class CultureProfile
{
    public static string Json(string run = "", string left = "", string right = "", string formats = "\"dd/MM/yyyy\"") => $$"""
        {
          "formatVersion": 1,
          "profileId": "declared-reading",
          "version": "1.0.0",
          {{run}}
          "sources": { "left": { "rowKey": "Id"{{left}} }, "right": { "rowKey": "Id"{{right}} } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Day", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "date.parse", "from": "Day", "to": "Day", "formats": [ {{formats}} ] } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "date.parse", "from": "Day", "to": "Day", "formats": [ {{formats}} ] } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Id" ] } ] } ]
        }
        """;

    public static CompiledProfile Compile(string json, CultureInfo server) =>
        ProfileCompiler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore(), server).Profile
            ?? throw new InvalidOperationException("The profile did not compile.");

    public static ReconciliationResult Run(CompiledProfile profile, (string Id, string Qty, string Day) left, (string Id, string Qty, string Day) right)
    {
        var map = new SourceMap<(string Id, string Qty, string Day)>().Text("Id", row => row.Id).Text("Qty", row => row.Qty).Text("Day", row => row.Day);
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects<(string Id, string Qty, string Day)>("left", [left], map),
            RecordSources.FromObjects<(string Id, string Qty, string Day)>("right", [right], map),
            ReferenceDataSet.Empty);
    }

    public static SideRules Rules(string culture, bool strictDateFormats = true) =>
        new(new RuleSettings(MidpointMode.ToEven, CultureInfo.GetCultureInfo(culture), strictDateFormats));
}

// Builds a registered rule the way compile does, with one side's settings.
internal sealed class SideRules(RuleSettings settings)
{
    public INormalizationRule Build(string ruleId, IReadOnlyDictionary<string, string> strings, IReadOnlyDictionary<string, IReadOnlyList<string>>? lists = null)
    {
        var call = new RuleCall(
            ruleId,
            null,
            strings,
            lists ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
        var schema = new FieldSchema(
        [
            new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
            new FieldDeclaration("Parsed", FieldType.Decimal, FieldRole.Economic, 4),
            new FieldDeclaration("Day", FieldType.Date, FieldRole.Economic, 0),
        ]);
        var registry = TradingRulePack.AddTo(RuleRegistry.CreateCore());
        return registry.TryGet(ruleId, out var registration)
            ? registration.CreateWithSettings(call, schema, settings)
            : throw new InvalidOperationException($"{ruleId} is not registered.");
    }
}
