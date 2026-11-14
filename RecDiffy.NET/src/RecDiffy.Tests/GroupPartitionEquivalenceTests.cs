using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// Issue #407. The one-pass partition replaces the fold that built a block's components, and the whole point is that
// nothing a caller can see changes. These cases drive the group-sum pass through the public engine on the three
// shapes whose component construction changed, and require the same answer under row order, machine culture, and the
// shared-reference claim the block was built to exercise.
public class GroupPartitionEquivalenceTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "group-partition",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "TradeRef", "type": "Identifier", "role": "Identity" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Book", "to": "Book" }, { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "Ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Book", "to": "Book" }, { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "Ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.groupSum", "many": "left", "block": [ "Book" ], "conserve": [ "Quantity" ] } ] } ]
        }
        """;

    private static readonly ProfileDocument Document = ProfileDocument.Parse(Profile);

    private static readonly SourceMap<Trade> Map = new SourceMap<Trade>()
        .Text("Id", trade => trade.Id)
        .Text("Book", trade => "B1")
        .Text("Ref", trade => trade.Ref)
        .Text("Qty", trade => trade.Qty);

    [Theory]
    [InlineData("shared", 8)]
    [InlineData("broker", 8)]
    [InlineData("absent", 8)]
    [InlineData("shared", 64)]
    [InlineData("broker", 32)]
    public void A_block_gives_the_same_result_in_any_row_order_and_under_any_machine_culture(string claim, int width)
    {
        var left = Allocations(claim, width);
        var right = Targets(claim, width);
        var expected = Describe(Run(left, right, CultureInfo.InvariantCulture));

        Assert.Equal(expected, Describe(Run(left, right, CultureInfo.GetCultureInfo("de-DE"))));
        Assert.Equal(expected, Describe(Run(left, right, CultureInfo.GetCultureInfo("tr-TR"))));
        Assert.Equal(expected, Describe(Run([.. Enumerable.Reverse(left)], [.. Enumerable.Reverse(right)], CultureInfo.InvariantCulture)));
    }

    // A wildcard target draws every many-side row, so the whole block is one component and no target can claim its
    // own rows: it is ambiguous, and nothing commits. This is the block shape the group pass must not break apart.
    [Fact]
    public void A_wildcard_target_against_a_block_of_allocations_contests_rather_than_matching()
    {
        var result = Run(Allocations("broker", 8), Targets("broker", 8), CultureInfo.InvariantCulture);

        Assert.Equal(0, Matches(result));
        Assert.All(result.Entries, entry => Assert.Equal(Alignment.Ambiguous, entry.Alignment));
    }

    // Two targets that name the same allocation value stay two components, so each is its own match against its own
    // allocations rather than the two sharing a component.
    [Fact]
    public void Two_targets_sharing_one_reference_each_match_against_their_own_allocations()
    {
        var result = Run(Allocations("shared", 8), Targets("shared", 8), CultureInfo.InvariantCulture);

        Assert.Equal(8, Matches(result));
        Assert.All(result.Entries.Where(entry => entry.Alignment is Alignment.Matched), entry => Assert.Single(entry.Right));
    }

    private static IReconciliationResult Run(IReadOnlyList<Trade> left, IReadOnlyList<Trade> right, CultureInfo culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            return Reconciler.Run(
                Reconciler.Compile(Document, RuleRegistry.CreateCore()),
                RecordSources.FromObjects("left", left, Map),
                RecordSources.FromObjects("right", right, Map),
                ReferenceDataSet.Empty);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static int Matches(IReconciliationResult result) => result.Entries.Count(entry => entry.Alignment is Alignment.Matched);

    // Each entry as its alignment, outcome and diffs under the set of rows it covers. The rows inside an entry keep
    // their input order, which is not a property of the partition, so the description sorts them and sorts the
    // entries; what remains is the outcome the rows produce.
    private static string Describe(IReconciliationResult result) => string.Join(
        " | ",
        result.Entries
            .Select(entry =>
                $"{(entry.Composition == Composition.None ? "-" : entry.Composition.ToString())}/{entry.Alignment}/{entry.Outcome} "
                + $"[{string.Join(",", entry.Left.Select(row => row.RowKey).Order(StringComparer.Ordinal))}] "
                + $"[{string.Join(",", entry.Right.Select(row => row.RowKey).Order(StringComparer.Ordinal))}] "
                + string.Join(",", entry.Diffs.Select(diff => $"{diff.Field}:{diff.Outcome}").Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal));

    // Each allocation holds a part of one target's quantity. Shared claims the allocation itself, broker is a
    // wildcard on the target side, and absent names a value the many side does not hold.
    private static Trade[] Allocations(string claim, int targets)
    {
        var parts = claim switch
        {
            "shared" => 2,
            _ => 3,
        };

        return [.. Enumerable.Range(0, targets).SelectMany(target => Enumerable.Range(0, parts).Select(part => new Trade(
            $"L{target}-{part}",
            "1.00",
            claim switch
            {
                "shared" => $"REF{target}",
                _ => null,
            })))];
    }

    private static Trade[] Targets(string claim, int targets) => [.. Enumerable.Range(0, targets).Select(target => new Trade(
        $"R{target}",
        (claim switch
        {
            "shared" => 2,
            _ => 3,
        }).ToInvariant(),
        claim switch
        {
            "shared" => $"REF{target}",
            "broker" => $"BRK{target}",
            _ => "ABSENT",
        }))];

    public sealed record Trade(string Id, string Qty, string? Ref);
}
