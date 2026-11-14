using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main: group-sum limits at their exact boundaries. Spec §4.7: a component with more than
// maxBlockItems (24) eligible items or maxTargetsPerComponent (8) targets is not searched and is SearchIncomplete
// (BlockTooLarge); §12.4: "Block of 25 eligible items: SearchIncomplete (BlockTooLarge)". Item cash values are distinct
// powers of two, so every target has exactly one conserving subset and the outcome is decided by the limit alone.
public class AdversarialSearchLimitTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-search-limits",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Block", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Block", "to": "Block" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Block", "to": "Block" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Block"], "conserve": ["Quantity", "NetCash"] } ] } ]
        }
        """;

    private static readonly SourceMap<(string Id, decimal Qty, decimal Cash)> Map = new SourceMap<(string Id, decimal Qty, decimal Cash)>()
        .Text("Id", row => row.Id).Text("Block", _ => "B").Decimal("Qty", row => row.Qty).Decimal("Cash", row => row.Cash);

    [Theory]
    [InlineData(24, false)]
    [InlineData(25, true)]
    public void A_component_is_searched_up_to_maxBlockItems_and_blocked_above_it(int items, bool tooLarge)
    {
        var left = Enumerable.Range(0, items).Select(index => ($"L{index}", 1m, Bit(index))).ToArray();
        var right = new[] { ("R0", 3m, Bit(0) + Bit(5) + Bit(9)) };

        Assert.Equal(tooLarge ? Expected(left, right, "BlockTooLarge") : Committed(left, right, [0, 5, 9]), Describe(Run(left, right)));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void A_component_is_searched_up_to_maxTargetsPerComponent_and_blocked_above_it(int targets, bool tooLarge)
    {
        var left = Enumerable.Range(0, targets * 2).Select(index => ($"L{index}", 1m, Bit(index))).ToArray();
        var right = Enumerable.Range(0, targets).Select(index => ($"R{index}", 2m, Bit(2 * index) + Bit(2 * index + 1))).ToArray();

        var result = Describe(Run(left, right));

        Assert.Equal(tooLarge ? Expected(left, right, "BlockTooLarge") : string.Join("\n", right.Select((target, index) => $"Matched {target.Item1}+L{2 * index}+L{2 * index + 1}").Order(StringComparer.Ordinal)), result);
    }

    private static decimal Bit(int index) => 1L << index;

    private static IReconciliationResult Run((string, decimal, decimal)[] left, (string, decimal, decimal)[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static string Describe(IReconciliationResult result) =>
        string.Join("\n", result.Entries.Select(entry => entry.Alignment switch
        {
            Alignment.Matched => $"Matched {string.Join("+", entry.Right.Select(member => member.RowKey).Concat(entry.Left.Select(member => member.RowKey).Order(StringComparer.Ordinal)))}",
            _ => $"{entry.Alignment} {entry.Explanation.Trail.FirstOrDefault()?.Detail} rows={(entry.Left.Length + entry.Right.Length).ToString(CultureInfo.InvariantCulture)}",
        }).Order(StringComparer.Ordinal));

    private static string Expected((string, decimal, decimal)[] left, (string, decimal, decimal)[] right, string code) =>
        $"SearchIncomplete {code} rows={(left.Length + right.Length).ToString(CultureInfo.InvariantCulture)}";

    private static string Committed((string, decimal, decimal)[] left, (string, decimal, decimal)[] right, int[] chosen) =>
        string.Join("\n", new[] { $"Matched {right[0].Item1}+{string.Join("+", chosen.Select(index => left[index].Item1))}" }
            .Concat(left.Where((_, index) => chosen.Contains(index) is false).Select(_ => "LeftOnly  rows=1"))
            .Order(StringComparer.Ordinal));
}
