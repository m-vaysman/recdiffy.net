using System.Text.Json.Nodes;

namespace RecDiffy.BrowserDemo.Demo;

/// <summary>Writes the profile for a setup: one field per chosen pair, a natural-key pass, and the tolerances.</summary>
public static class ProfileJson
{
    public static string KeyField(int index) => $"key{(index + 1).ToInvariant()}";

    public static string CompareField(int index) => $"compare{(index + 1).ToInvariant()}";

    public static string Build(RecSetup setup, LoadedFile left, LoadedFile right)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var keys = setup.CompleteKeys.Select((pair, index) => (Field: KeyField(index), Pair: pair)).ToList();
        var compares = setup.CompleteCompares.Select((pair, index) => (Field: CompareField(index), Pair: pair)).ToList();

        var fields = keys.Select(key => Field(key.Field, "Symbol", "Key", scale: null))
            .Concat(compares.Select(compare => compare.Pair.Number
                ? Field(compare.Field, "Decimal", "Economic", Scale(compare.Pair, left, right))
                : Field(compare.Field, "Symbol", "Economic", scale: null)));

        var tolerances = new JsonObject(compares
            .Where(compare => compare.Pair is { Number: true, Tolerance: > 0m })
            .Select(compare => KeyValuePair.Create<string, JsonNode?>(
                compare.Field,
                new JsonObject { ["tolerance"] = new JsonObject { ["absolute"] = compare.Pair.Tolerance } })));

        var profile = new JsonObject
        {
            ["formatVersion"] = 1,
            ["profileId"] = "browser-demo",
            ["version"] = "1.0.0",
            ["culture"] = "en-US",
            ["sources"] = new JsonObject { ["left"] = new JsonObject(), ["right"] = new JsonObject() },
            ["fields"] = new JsonArray([.. fields]),
            ["normalization"] = new JsonObject
            {
                ["left"] = Side(keys, compares, pair => pair.Left),
                ["right"] = Side(keys, compares, pair => pair.Right),
            },
            ["paths"] = new JsonArray(new JsonObject
            {
                ["id"] = "main",
                ["route"] = new JsonObject { ["rule"] = "route.all" },
                ["passes"] = new JsonArray(new JsonObject
                {
                    ["id"] = "key",
                    ["pass"] = "match.naturalKey",
                    ["key"] = new JsonArray([.. keys.Select(key => (JsonNode)JsonValue.Create(key.Field))]),
                }),
                ["diff"] = new JsonObject { ["fields"] = tolerances },
            }),
        };

        return profile.ToJsonString();
    }

    private static JsonObject Field(string name, string type, string role, int? scale)
    {
        var field = new JsonObject { ["name"] = name, ["type"] = type, ["role"] = role };
        if (scale is { } decimals)
        {
            field["scale"] = decimals;
        }

        return field;
    }

    private static int Scale(ColumnPair pair, LoadedFile left, LoadedFile right) =>
        new[] { left.Decimals(pair.Left), right.Decimals(pair.Right), pair.Tolerance.Scale }.Max();

    private static JsonObject Side(
        IReadOnlyList<(string Field, ColumnPair Pair)> keys,
        IReadOnlyList<(string Field, ColumnPair Pair)> compares,
        Func<ColumnPair, string> column)
    {
        var symbols = keys.Concat(compares.Where(compare => !compare.Pair.Number))
            .Select(entry => Rule("symbol.map", column(entry.Pair), entry.Field));
        var numbers = compares.Where(compare => compare.Pair.Number)
            .Select(entry => Rule("decimal.parse", column(entry.Pair), entry.Field));

        return new JsonObject
        {
            ["identify"] = new JsonArray([.. symbols]),
            ["parse"] = new JsonArray([.. numbers]),
        };
    }

    private static JsonObject Rule(string rule, string from, string to) =>
        new JsonObject { ["rule"] = rule, ["from"] = from, ["to"] = to };
}
