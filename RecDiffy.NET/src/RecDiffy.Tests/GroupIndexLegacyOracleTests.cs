using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #41, #407. GroupIndex builds a block's components in one pass and stores a block's decisions once. LegacyGroupIndex
// is the index as it was before, kept in this project as the oracle. Over generated blocks the two must agree on
// everything a caller can see, at every step of the commit loop: the ready proposals in order, the proposal that
// commits next, the residue with its rows in order, and the counters. That covers component membership, the order of
// components and of targets inside them, which rows each target may draw, and every outcome.
public class GroupIndexLegacyOracleTests
{
    private static readonly Dictionary<string, FieldDeclaration> Schema = new(StringComparer.Ordinal)
    {
        ["Block"] = new("Block", FieldType.Symbol, FieldRole.Key, 0),
        ["TradeRef"] = new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
        ["Qty"] = new("Qty", FieldType.Decimal, FieldRole.Economic, 2),
    };

    // Tight limits block components the default limits search. Starved limits search them and run out of nodes.
    private static readonly SearchLimits Tight = new(3, 6, 3, 40);

    private static readonly SearchLimits Starved = SearchLimits.Default with { MaxNodesPerTarget = 4 };

    // Rows with no reference and rows with a broker reference are counted per hundred rows.
    private static readonly (string Name, string Limits, int Rows, int NoReference, int Broker)[] All =
    [
        ("mixed", "default", 60, 30, 15),
        ("mixed, tight limits", "tight", 60, 30, 15),
        ("every row has a shared reference", "default", 60, 0, 0),
        ("every row has a shared reference, tight limits", "tight", 60, 0, 0),
        ("no row has a reference", "default", 40, 100, 0),
        ("no row has a reference, tight limits", "tight", 40, 100, 0),
        ("broker references only", "tight", 40, 0, 100),
        ("few rows without a reference", "default", 90, 4, 4),
        ("few rows without a reference, tight limits", "tight", 90, 4, 4),
        ("mixed, starved of nodes", "starved", 60, 30, 15),
        ("every row has a shared reference, starved of nodes", "starved", 60, 0, 0),
        ("no row has a reference, starved of nodes", "starved", 24, 100, 0),
    ];

    public static TheoryData<string, string, int, int, int> Shapes
    {
        get
        {
            var shapes = new TheoryData<string, string, int, int, int>();
            foreach (var shape in All)
                shapes.Add(shape.Name, shape.Limits, shape.Rows, shape.NoReference, shape.Broker);
            return shapes;
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_index_agrees_with_the_index_before_the_change_at_every_commit(string name, string searchLimits, int rows, int noReference, int broker)
    {
        var limits = Limits(searchLimits);
        var commits = 0;
        var open = 0;
        for (var seed = 0; seed < 40; seed++)
        {
            foreach (var many in new[] { Side.Left, Side.Right })
            {
                var workspace = Workspace(Items(seed, rows, many, noReference, broker), limits);
                var now = GroupIndex.Create("G", ["Block"], ["Qty"], many, limits.MaxGroupSize, workspace, new MatchCounters());
                var was = LegacyGroupIndex.Create("G", ["Block"], ["Qty"], many, limits.MaxGroupSize, workspace, new MatchCounters());
                (PassIndex Index, MatchCounters Counters) current = (now.Index, now.Counters);
                (PassIndex Index, MatchCounters Counters) legacy = (was.Index, was.Counters);
                var step = 0;
                while (true)
                {
                    var where = $"{name}, seed {seed.ToInvariant()}, many {many}, after {step.ToInvariant()} commits";
                    Assert.True(Snapshot(legacy.Index) == Snapshot(current.Index), $"{where}:\nbefore\n{Snapshot(legacy.Index)}\nnow\n{Snapshot(current.Index)}");
                    Assert.True(legacy.Counters == current.Counters, $"{where}: counters before {legacy.Counters}, now {current.Counters}");
                    if (current.Index.NextReady() is not { } next)
                        break;
                    IReadOnlyList<int> taken = [.. next.Left, .. next.Right];
                    current = current.Index.Release(taken, current.Counters);
                    legacy = legacy.Index.Release(taken, legacy.Counters);
                    step++;
                }

                commits += step;
                open += current.Index.Residue().Count;
            }
        }

        // A shape that never commits and never leaves anything open would compare two empty indexes.
        Assert.True(commits + open > 0, $"{name}: no commit and no residue in any generated block.");
    }

    // Every kind of decision must come up somewhere, or the comparison above says nothing about it.
    [Fact]
    public void The_generated_blocks_reach_commits_contests_blocked_and_exhausted_searches()
    {
        var seen = All
            .SelectMany(shape => Enumerable.Range(0, 40).Select(seed => (Limits: Limits(shape.Limits), Items: Items(seed, shape.Rows, Side.Left, shape.NoReference, shape.Broker))))
            .Select(run => GroupIndex.Create("G", ["Block"], ["Qty"], Side.Left, run.Limits.MaxGroupSize, Workspace(run.Items, run.Limits), new MatchCounters()).Index)
            .SelectMany(index => index.Residue().Select(cluster => $"{cluster.Alignment} {cluster.Code}").Concat(index.ReadyProposals.Count > 0 ? ["Ready"] : []))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Ambiguous ", "Ready", "SearchIncomplete BlockTooLarge", "SearchIncomplete BudgetExhausted"], seen);
    }

    private static SearchLimits Limits(string name) => name switch
    {
        "tight" => Tight,
        "starved" => Starved,
        _ => SearchLimits.Default,
    };

    private static string Snapshot(PassIndex index) =>
        string.Join('\n',
        [
            $"next: {(index.NextReady() is { } next ? Proposal(next) : "none")}",
            .. index.ReadyProposals.Select(proposal => "ready " + Proposal(proposal)),
            .. index.Residue().Select(cluster => $"open {cluster.Alignment} {cluster.Code} key={cluster.Key}: {string.Join(' ', cluster.Rows.Select(row => row.Context.RowKey))}"),
        ]);

    private static string Proposal(MatchProposal proposal) =>
        $"{proposal.Key}: {string.Join(' ', proposal.Left)} / {string.Join(' ', proposal.Right)}";

    private static MatchWorkspace Workspace(ActiveItem[] items, SearchLimits limits) =>
        new("G", items, items.Select(item => item.Index).ToImmutableHashSet(), Schema, limits, "TradeRef", limits.MaxGroupSize, CancellationToken.None);

    // Three blocks, three shared references and quantities of 1 to 3 on the many side, so targets of 2 to 6 have
    // one answer, several, or none, and components range from one target to the whole block.
    private static ActiveItem[] Items(int seed, int count, Side many, int noReference, int broker)
    {
        var random = new Random((seed * 15_485_863) + count + noReference + (broker * 3));
        return [.. Enumerable.Range(0, count).Select(index => Item(index, random, many, noReference, broker))];
    }

    private static ActiveItem Item(int index, Random random, Side many, int noReference, int broker)
    {
        var side = random.Next(4) is 0 ? Other(many) : many;
        var id = $"{(side is Side.Left ? "L" : "R")}{index.ToInvariant()}";
        var row = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = random.Next(25) is 0 ? row : Symbol(row, "Block", "B" + random.Next(3).ToInvariant());
        var reference = random.Next(100);
        row = reference < noReference ? row
            : reference < noReference + broker ? Identifier(row, "broker", "X" + random.Next(3).ToInvariant())
            : Identifier(row, random.Next(5) is 0 ? "isin" : "shared", "R" + random.Next(3).ToInvariant());
        var quantity = side == many ? 1 + random.Next(3) : 2 + random.Next(5);
        row = random.Next(20) is 0 ? row : row.Write("Qty", CanonicalCell.FromDecimal(quantity), "test", null, null);
        return new ActiveItem(index, side, new ConsolidatedRow([new SourceRef(side is Side.Left ? "left" : "right", id, index)], row, false, null, null));
    }

    private static Side Other(Side side) => side is Side.Left ? Side.Right : Side.Left;

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, value);
    }

    private static RowContext Identifier(RowContext row, string scheme, string value)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", value);
        return row.WithPool(pool).Write("TradeRef", CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, value);
    }
}
