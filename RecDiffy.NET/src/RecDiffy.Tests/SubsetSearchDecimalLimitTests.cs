using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class SubsetSearchDecimalLimitTests
{
    private const string Group = """
        {
          "formatVersion": 1,
          "profileId": "subset-search-decimal-limit",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty"], "conserve": ["Quantity"] } ] } ]
        }
        """;

    private const string Max = "79228162514264337593543950335";
    private const string Big = "70000000000000000000000000000";
    private const string BigEven = "70000000000000000000000000008";

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Cpty", row => row.Cpty)
        .Text("Qty", row => row.Qty);

    [Theory]
    [InlineData(Max, "-1305.50", "1")]
    [InlineData(Max, "-2853.50", "484.88")]
    [InlineData(Max, "-0.50", "1")]
    public void A_group_search_over_a_huge_and_a_negative_quantity_returns_a_result(string huge, string negative, string target)
    {
        var result = Run([new Row("L1", "JPM", huge), new Row("L2", "JPM", negative)], [new Row("R1", "JPM", target)]);

        Assert.All(result.Entries, entry => Assert.True(entry.Left.Length + entry.Right.Length < 2));
    }

    [Theory]
    [InlineData("11")]
    [InlineData("10")]
    [InlineData("8.5")]
    public void A_group_search_does_not_group_rows_whose_quantities_do_not_add_up(string target)
    {
        Row[] left = [new("L1", "JPM", Big), new("L2", "JPM", "4"), new("L3", "JPM", "3"), new("L4", "JPM", "0.50"), new("L5", "JPM", "-1")];

        var result = Run(left, [new Row("R1", "JPM", target)]);

        Assert.True(result.Entries.All(entry => entry.Left.Length + entry.Right.Length < 2), Describe(result));
    }

    [Fact]
    public void A_group_search_does_not_treat_a_rounded_away_fraction_as_zero()
    {
        Row[] left = [new("L1", "JPM", BigEven), new("L2", "JPM", "0.50")];

        var result = Run(left, [new Row("R1", "JPM", BigEven)]);

        Assert.True(result.Entries.All(entry => entry.Left.Length + entry.Right.Length < 2), Describe(result));
    }

    [Theory]
    [InlineData(true, "7", true)]
    [InlineData(true, "11", false)]
    [InlineData(false, "7", true)]
    [InlineData(false, "11", false)]
    public void Only_a_true_sum_is_grouped_with_or_without_a_huge_quantity(bool includeHuge, string target, bool grouped)
    {
        Row[] small = [new("L2", "JPM", "4"), new("L3", "JPM", "3"), new("L4", "JPM", "0.50"), new("L5", "JPM", "-1")];
        Row[] left = includeHuge ? [new("L1", "JPM", Big), .. small] : small;

        var result = Run(left, [new Row("R1", "JPM", target)]);

        Assert.True(grouped == result.Entries.Any(entry => entry.Left.Length >= 2), Describe(result));
    }

    private static string Describe(IReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Composition}/{entry.Outcome} left[{Keys(entry.Left)}] right[{Keys(entry.Right)}] diffs[{string.Join(", ", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"))}]"));

    private static string Keys(IReadOnlyList<SourceRef> rows) => string.Join(",", rows.Select(row => row.RowKey));

    private static IReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Group), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private sealed record Row(string Id, string Cpty, string Qty);
}
