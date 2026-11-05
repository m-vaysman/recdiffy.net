using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class CascadeTests
{
    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Instrument", FieldType.Symbol, FieldRole.Key, 0),
        new("TradeDate", FieldType.Date, FieldRole.Key, 0),
        new("Side", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("Price", FieldType.Decimal, FieldRole.Economic, 6),
        new("NetCash", FieldType.Decimal, FieldRole.Economic, 2),
        new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
    ];

    private static readonly PassDeclaration[] Passes =
    [
        new("P1", "match.sharedRef", [], [], [], null, null),
        new("P2", "match.naturalKey", ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"], [], [], null, null),
        new("P3", "match.groupSum", [], ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], ["Quantity", "NetCash"], "left", null),
        new("P4", "match.reducedKey", ["Counterparty", "Instrument", "TradeDate", "Side"], [], [], null, null),
    ];

    [Fact]
    public void Commits_a_shared_reference_before_it_reads_price()
    {
        var report = Run(
            [Trade("OMS2827093", Side.Left, "GSCO", "US1", 1_750_000m, 96.572m, 1m, "SWU69BMJ17DGR1VWAZ3442EEBDA8AA")],
            [Trade("GS-85339531", Side.Right, "GSCO", "US1", 1_750_000m, 99.966m, 1m, "SWU69BMJ17DGR1VWAZ3442EEBDA8AA")]);

        var match = Assert.Single(report.Committed);
        Assert.Equal("P1", match.PassId);
        Assert.Empty(report.Residue);
    }

    [Fact]
    public void Commits_the_natural_key_when_the_shared_references_differ()
    {
        var report = Run(
            [Trade("OMS4152729", Side.Left, "BAML", "US91282CT687", 3_000_000m, 96.078125m, 1m, null)],
            [Trade("BA-18777889", Side.Right, "BAML", "US91282CT687", 3_000_000m, 96.078125m, 1m, null)]);

        Assert.Equal("P2", Assert.Single(report.Committed).PassId);
    }

    [Fact]
    public void Sums_an_allocation_block_on_the_group_pass()
    {
        var report = Run(
            [
                Trade("OMS4044414", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 114m, 250_811m, null),
                Trade("OMS7849940", Side.Left, "JPM", "US00Q8EY5J09", 5_400m, 114m, 615_627m, null),
                Trade("OMS4003839", Side.Left, "JPM", "US00Q8EY5J09", 1_400m, 114m, 159_607m, null),
            ],
            [Trade("JP-48051575", Side.Right, "JPM", "US00Q8EY5J09", 9_000m, 114m, 1_026_045m, null)]);

        var match = Assert.Single(report.Committed);
        Assert.Equal("P3", match.PassId);
        Assert.Equal(3, match.Left.Count);
        Assert.Equal("JP-48051575", match.Right[0].Context.RowKey);
    }

    [Fact]
    public void Commits_a_partial_fill_on_the_reduced_key()
    {
        var report = Run(
            [Trade("OMS3512650", Side.Left, "CITI", "UST77KS39V09", 1_000_000m, 102m, 1_028_884.72m, null)],
            [Trade("CG-43060951", Side.Right, "CITI", "UST77KS39V09", 800_000m, 102m, 823_107.78m, null)]);

        Assert.Equal("P4", Assert.Single(report.Committed).PassId);
    }

    [Fact]
    public void Vetoes_a_reduced_key_when_shared_references_differ()
    {
        var report = Run(
            [Trade("OMS-46", Side.Left, "GSCO", "US9900000020", 27_000_000m, 1m, 1m, "EX-1A904B08BDA71E36")],
            [Trade("ST-A4", Side.Right, "GSCO", "US9900000020", 27_900_000m, 1m, 1m, "EX-9637B58A037EBDFE")]);

        Assert.Empty(report.Committed);
        Assert.Contains(report.Residue, cluster => cluster.Alignment == Results.Alignment.LeftOnly);
        Assert.Contains(report.Residue, cluster => cluster.Alignment == Results.Alignment.RightOnly);
    }

    [Fact]
    public void Reevaluates_a_deferred_pair_after_the_rival_is_consumed()
    {
        var report = Run(
            [
                Trade("OMS4044414", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 114m, 250_811m, null),
                Trade("OMS7849940", Side.Left, "JPM", "US00Q8EY5J09", 5_400m, 114m, 615_627m, null),
                Trade("OMS4003839", Side.Left, "JPM", "US00Q8EY5J09", 1_400m, 114m, 159_607m, null),
                Trade("A", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 113.95m, 250_000m, null),
            ],
            [
                Trade("JP-48051575", Side.Right, "JPM", "US00Q8EY5J09", 9_000m, 114m, 1_026_045m, null),
                Trade("S", Side.Right, "JPM", "US00Q8EY5J09", 2_200m, 113.95m, 250_000m, null),
            ]);

        Assert.Contains(report.Committed, match => match.PassId == "P3");
        Assert.Contains(report.Committed, match => match.PassId == "P2" && match.Left[0].Context.RowKey == "A" && match.Right[0].Context.RowKey == "S");
    }

    [Fact]
    public void Leaves_a_one_to_many_key_ambiguous()
    {
        var report = Run(
            [
                Trade("L-FAFDE5", Side.Left, "CEDAR", "EURUSD", 2_780_000m, 1.093m, 1m, null),
                Trade("L-257F61", Side.Left, "CEDAR", "EURUSD", 2_780_000m, 1.093m, 1m, null),
            ],
            [Trade("R-74AAC1", Side.Right, "CEDAR", "EURUSD", 2_780_000m, 1.093m, 1m, null)]);

        var contest = Assert.Single(report.Residue);
        Assert.Equal(Results.Alignment.Ambiguous, contest.Alignment);
        Assert.Equal(3, contest.Rows.Count);
        Assert.Empty(report.Committed);
    }

    [Fact]
    public void Treats_a_partial_class_as_a_contest()
    {
        var report = Run(
            [
                Trade("OMS4044414", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 114m, 250_811m, null),
                Trade("TWIN", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 114m, 250_811m, null),
                Trade("OMS7849940", Side.Left, "JPM", "US00Q8EY5J09", 5_400m, 114m, 615_627m, null),
                Trade("OMS4003839", Side.Left, "JPM", "US00Q8EY5J09", 1_400m, 114m, 159_607m, null),
            ],
            [Trade("JP-48051575", Side.Right, "JPM", "US00Q8EY5J09", 9_000m, 114m, 1_026_045m, null)]);

        Assert.Empty(report.Committed);
        Assert.Equal(Results.Alignment.Ambiguous, Assert.Single(report.Residue).Alignment);
    }

    [Fact]
    public void Drops_a_vetoed_twin_and_commits_the_block()
    {
        var report = Run(
            [
                Trade("OMS4044414", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 114m, 250_811m, "EX-BLOCK"),
                Trade("TWIN", Side.Left, "JPM", "US00Q8EY5J09", 2_200m, 114m, 250_811m, "EX-OTHER"),
                Trade("OMS7849940", Side.Left, "JPM", "US00Q8EY5J09", 5_400m, 114m, 615_627m, "EX-BLOCK"),
                Trade("OMS4003839", Side.Left, "JPM", "US00Q8EY5J09", 1_400m, 114m, 159_607m, "EX-BLOCK"),
            ],
            [Trade("JP-48051575", Side.Right, "JPM", "US00Q8EY5J09", 9_000m, 114m, 1_026_045m, "EX-BLOCK")]);

        Assert.Equal("P3", Assert.Single(report.Committed).PassId);
        Assert.Contains(report.Residue, cluster => cluster.Alignment == Results.Alignment.LeftOnly && cluster.Rows[0].Context.RowKey == "TWIN");
    }

    [Fact]
    public void Stops_a_block_that_exceeds_the_item_limit()
    {
        var left = Enumerable.Range(0, 25)
            .Select(index => Trade($"L{index}", Side.Left, "JPM", "US1", 100m, 10m, 50m, null))
            .ToArray();
        var report = Run(left, [Trade("R", Side.Right, "JPM", "US1", 200m, 10m, 100m, null)], limits: SearchLimits.Default with { MaxBlockItems = 24 });

        Assert.Empty(report.Committed);
        Assert.Contains(report.Residue, cluster => cluster.Alignment == Results.Alignment.SearchIncomplete && cluster.Code == "BlockTooLarge");
    }

    [Fact]
    public void Keeps_a_solution_incomplete_when_the_node_budget_expires()
    {
        var classes = new ConservedClass[]
        {
            new([0m, 5m], [0]),
            new([0m, 5m], [1]),
        };
        var search = SubsetSearch.Search(classes, [0m, 10m], 8, 3);

        Assert.Equal(SearchState.Incomplete, search.State);
        Assert.NotEmpty(search.DrawnItems);
    }

    [Fact]
    // cascade-seed.txt pins Seed's commit order and each commit's pass, key, and row keys. Residue lines are sorted.
    public void Matches_the_seeded_golden_snapshot()
    {
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Goldens", "cascade-seed.txt")).TrimEnd();
        Assert.Equal(golden, Snapshot(Seed()));
    }

    [Fact]
    public void Keeps_the_same_membership_when_the_rows_are_reversed()
    {
        var (left, right) = SeedRows();
        var forward = Snapshot(Run(left, right));
        var backward = Snapshot(Run([.. left.AsEnumerable().Reverse()], [.. right.AsEnumerable().Reverse()]));
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void Builds_one_index_per_pass()
    {
        var report = Seed();
        Assert.Equal(4, report.Counters.IndexBuilds);
        Assert.True(report.Counters.SubsetSearches <= report.Counters.Components + report.Counters.DirtyResearches);
    }

    [Fact]
    public void Does_not_research_an_untouched_group_block()
    {
        var left = new[]
        {
            Trade("A1", Side.Left, "JPM", "AAA", 2m, 10m, 2m, null),
            Trade("A2", Side.Left, "JPM", "AAA", 3m, 10m, 3m, null),
            Trade("B1", Side.Left, "JPM", "BBB", 1m, 10m, 1m, null),
            Trade("B2", Side.Left, "JPM", "BBB", 4m, 10m, 4m, null),
        };
        var right = new[]
        {
            Trade("AR", Side.Right, "JPM", "AAA", 5m, 10m, 5m, null),
            Trade("BR", Side.Right, "JPM", "BBB", 5m, 10m, 5m, null),
        };
        var report = CascadeMatcher.Run(left, right, [Passes[2]], Fields, SearchLimits.Default);

        Assert.Equal(2, report.Committed.Count);
        Assert.Equal(1, report.Counters.IndexBuilds);
        Assert.Equal(2, report.Counters.SubsetSearches);
        Assert.Equal(0, report.Counters.DirtyResearches);
    }

    [Fact]
    public void Doubling_pair_matches_stays_under_a_quadratic_slope()
    {
        var once = Elapsed(600);
        var twice = Elapsed(1_200);
        Assert.True(twice < once * 3 + TimeSpan.FromMilliseconds(200), $"600 rows took {once}, 1200 rows took {twice}");
    }

    private static TimeSpan Elapsed(int count)
    {
        var left = new ConsolidatedRow[count];
        var right = new ConsolidatedRow[count];
        for (var index = 0; index < count; index++)
        {
            left[index] = Trade($"L{index}", Side.Left, "C", "I" + index, 10m, 100m, 1m, null);
            right[index] = Trade($"R{index}", Side.Right, "C", "I" + index, 10m, 100m, 1m, null);
        }

        var passes = new[] { Passes[1] };
        CascadeMatcher.Run(left, right, passes, Fields, SearchLimits.Default);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        CascadeMatcher.Run(left, right, passes, Fields, SearchLimits.Default);
        watch.Stop();
        return watch.Elapsed;
    }

    private static string Snapshot(MatchReport report)
    {
        var committed = report.Committed.Select(match =>
            $"C|{match.PassId}|{match.Key}|{string.Join(",", match.Left.Select(row => row.Context.RowKey))}|{string.Join(",", match.Right.Select(row => row.Context.RowKey))}");
        var residue = report.Residue
            .Select(cluster => $"R|{cluster.Alignment}|{cluster.PassId}|{string.Join(",", cluster.Rows.Select(row => row.Context.RowKey).OrderBy(key => key, StringComparer.Ordinal))}")
            .OrderBy(line => line, StringComparer.Ordinal);
        return string.Join("\n", committed.Concat(residue));
    }

    private static MatchReport Seed()
    {
        var (left, right) = SeedRows();
        return Run(left, right);
    }

    private static (List<ConsolidatedRow> Left, List<ConsolidatedRow> Right) SeedRows()
    {
        var left = new List<ConsolidatedRow>();
        var right = new List<ConsolidatedRow>();
        for (var index = 0; index < 12; index++)
        {
            var shared = index % 4 == 0 ? $"REF{index:00}" : null;
            left.Add(Trade($"L{index:00}", Side.Left, "C" + (index % 3), "I" + (index % 4), 10m + index, 100m, 1m, shared));
            right.Add(Trade($"R{index:00}", Side.Right, "C" + (index % 3), "I" + (index % 4), 10m + index, 100m, 1m, shared));
        }

        left.Add(Trade("A1", Side.Left, "JPM", "ALLOC", 2_200m, 114m, 250_811m, "EX-A"));
        left.Add(Trade("A2", Side.Left, "JPM", "ALLOC", 5_400m, 114m, 615_627m, null));
        left.Add(Trade("A3", Side.Left, "JPM", "ALLOC", 1_400m, 114m, 159_607m, null));
        right.Add(Trade("AR", Side.Right, "JPM", "ALLOC", 9_000m, 114m, 1_026_045m, "EX-A"));
        left.Add(Trade("ORPHAN", Side.Left, "NONE", "ZZ", 1m, 1m, 1m, null));
        return (left, right);
    }

    private static MatchReport Run(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right, SearchLimits? limits = null) =>
        CascadeMatcher.Run(left, right, Passes, Fields, limits ?? SearchLimits.Default);

    private static ConsolidatedRow Trade(string id, Side side, string party, string instrument, decimal quantity, decimal price, decimal cash, string? tradeRef)
    {
        var row = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        row = Symbol(row, "Counterparty", party);
        row = Symbol(row, "Instrument", instrument);
        row = Symbol(row, "Side", "BUY");
        row = row.Write("TradeDate", CanonicalCell.FromDate(new DateOnly(2025, 6, 11)), "test", null, "2025-06-11");
        row = row.Write("Quantity", CanonicalCell.FromDecimal(quantity), "test", null, null);
        row = row.Write("Price", CanonicalCell.FromDecimal(price), "test", null, null);
        row = row.Write("NetCash", CanonicalCell.FromDecimal(cash), "test", null, null);
        if (tradeRef is not null)
            row = Identifier(row, "TradeRef", "shared", tradeRef);
        return new ConsolidatedRow([new SourceRef(side == Side.Left ? "left" : "right", id, 0)], row, false, null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, value);
    }

    private static RowContext Identifier(RowContext row, string field, string scheme, string value)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, value);
    }
}
