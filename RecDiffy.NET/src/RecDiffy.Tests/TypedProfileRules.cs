using System.Text.Json.Nodes;

namespace RecDiffy.Tests;

// Reads the identify rules of one side of a profile, so a test can check the tables a typed reconciliation writes.
internal static class TypedProfileRules
{
    internal sealed record Identify(string Rule, string To, IReadOnlyDictionary<string, string?> Map, string? Passthrough);

    public static IReadOnlyList<Identify> Of(ProfileDocument profile, string side)
    {
        var root = JsonNode.Parse(ProfileWriter.ToJson(profile)) as JsonObject ?? throw new InvalidOperationException("The profile is not a JSON object.");
        var rules = root["normalization"]?[side]?["identify"] as JsonArray ?? throw new InvalidOperationException($"The profile has no identify rules for '{side}'.");
        return
        [
            .. rules.OfType<JsonObject>().Select(rule => new Identify(
                (string?)rule["rule"] ?? "",
                (string?)rule["to"] ?? "",
                rule["map"] is JsonObject map
                    ? map.ToDictionary(entry => entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal)
                    : new Dictionary<string, string?>(StringComparer.Ordinal),
                rule["passthrough"]?.ToString())),
        ];
    }

    // The table the side holds for the field called Side.
    public static IReadOnlyDictionary<string, string?> SideTable(ProfileDocument profile, string side) =>
        Of(profile, side).Single(rule => rule.To == "Side").Map;

    public static Dictionary<string, string?> Table(params (string Spelling, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Spelling, entry => (string?)entry.Value, StringComparer.Ordinal);
}
