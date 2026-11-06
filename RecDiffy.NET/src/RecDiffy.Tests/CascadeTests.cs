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
    public void Does_not_join_values_that_only_match_across_the_key_separator()
    {
        const string separator = "\u001f";
        var report = Run(
            [
                Trade("L-split", Side.Left, "A" + separator + "B", "C", 1m, 1m, 1m, null),
                Trade("L-slash", Side.Left, "A\\", "B", 3m, 1m, 1m, null),
                Trade("L-same", Side.Left, "A" + separator + "B", "C", 2m, 1m, 1m, null),
            ],
            [
                Trade("R-other", Side.Right, "A", "B" + separator + "C", 1m, 1m, 1m, null),
                Trade("R-slash", Side.Right, "A", "\\B", 3m, 1m, 1m, null),
                Trade("R-same", Side.Right, "A" + separator + "B", "C", 2m, 1m, 1m, null),
            ]);

        var match = Assert.Single(report.Committed);
        Assert.Equal("L-same", match.Left[0].Context.RowKey);
        Assert.Equal("R-same", match.Right[0].Context.RowKey);
        Assert.Equal(
            ["L-slash", "L-split", "R-other", "R-slash"],
            report.Residue.SelectMany(cluster => cluster.Rows).Select(row => row.Context.RowKey).OrderBy(key => key, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Does_not_hash_crossed_source_and_row_keys_to_the_same_group()
    {
        const string separator = "\u001f";
        var crossed = ResultBuilder.Identify([new SourceRef("a" + separator + "b", "c", 0)]);
        var other = ResultBuilder.Identify([new SourceRef("a", "b" + separator + "c", 0)]);
        var same = ResultBuilder.Identify([new SourceRef("a" + separator + "b", "c", 0)]);

        Assert.NotEqual(crossed, other);
        Assert.Equal(crossed, same);
    }

    [Fact]
    public void Keeps_a_carriage_return_from_collapsing_into_the_newline_join()
    {
        var broken = Delimited.Join('\n', ["a\r", "b"]);
        var plain = Delimited.Join('\n', ["a", "b"]);

        Assert.NotEqual(plain, broken.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparer.Ordinal);
    }

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
    public void Keeps_each_target_separate_when_the_many_side_is_empty()
    {
        var report = Group([], [Row("R1", Side.Right, null), Row("R2", Side.Right, null), Row("R3", Side.Right, null)], Blocked);

        Assert.Equal([["R1"], ["R2"], ["R3"]], BlockedKeys(report));
    }

    [Fact]
    public void Glues_every_target_when_a_many_side_row_is_a_wildcard()
    {
        var report = Group([Row("L", Side.Left, null)], [Row("R1", Side.Right, "A"), Row("R2", Side.Right, "B")], Blocked);

        Assert.Equal([["R1", "R2", "L"]], BlockedKeys(report));
    }

    [Fact]
    public void Lets_a_wildcard_target_bridge_nonempty_buckets_and_leaves_an_empty_bucket_apart()
    {
        var report = Group(
            [Row("LA", Side.Left, "A"), Row("LB", Side.Left, "B")],
            [Row("RA", Side.Right, "A"), Row("RB", Side.Right, "B"), Row("RW", Side.Right, null), Row("RZ", Side.Right, "Z")],
            Blocked);

        Assert.Equal([["RA", "RB", "RW", "LA", "LB"], ["RZ"]], BlockedKeys(report));
    }

    [Fact]
    public void Treats_the_same_value_under_different_schemes_as_one_bucket()
    {
        var report = Group(
            [Row("L", Side.Left, "X", "CUSIP")],
            [Row("R", Side.Right, "X", "ISIN")],
            Blocked);

        Assert.Equal([["R", "L"]], BlockedKeys(report));
    }

    [Fact]
    public void Keeps_different_values_apart_when_nothing_is_a_wildcard()
    {
        var report = Group(
            [Row("LA", Side.Left, "A"), Row("LB", Side.Left, "B")],
            [Row("RA", Side.Right, "A"), Row("RB", Side.Right, "B")],
            Blocked);

        Assert.Equal([["RA", "LA"], ["RB", "LB"]], BlockedKeys(report));
    }

    [Fact]
    public void Treats_a_broker_many_side_row_and_a_broker_target_as_wildcards()
    {
        var many = Group([Row("L", Side.Left, "NOPE", "broker")], [Row("R1", Side.Right, "A"), Row("R2", Side.Right, "B")], Blocked);
        var target = Group(
            [Row("LA", Side.Left, "A"), Row("LB", Side.Left, "B")],
            [Row("RW", Side.Right, "WHATEVER", "broker")],
            Blocked);

        Assert.Equal([["R1", "R2", "L"]], BlockedKeys(many));
        Assert.Equal([["RW", "LA", "LB"]], BlockedKeys(target));
    }

    [Fact]
    public void Leaves_targets_with_an_empty_bucket_in_separate_components()
    {
        var report = Group(
            [Row("LB", Side.Left, "B")],
            [Row("RA1", Side.Right, "A"), Row("RA2", Side.Right, "A")],
            Blocked);

        Assert.Equal([["RA1"], ["RA2"]], BlockedKeys(report));
    }

    [Fact]
    public void Does_not_search_a_component_over_either_limit_and_keeps_residue_order()
    {
        var left = Enumerable.Range(0, 4).Select(index => Row($"L{index}", Side.Left, null, quantity: 1m, cash: 1m)).ToArray();
        var report = Group(left, [Row("R", Side.Right, null, quantity: 4m, cash: 4m)], SearchLimits.Default with { MaxBlockItems = 3 });

        Assert.Empty(report.Committed);
        Assert.Equal(0, report.Counters.SubsetSearches);
        Assert.Equal([["R", "L0", "L1", "L2", "L3"]], BlockedKeys(report));
    }

    [Fact]
    public void Commits_the_small_components_inside_a_block_that_is_over_the_raw_limit()
    {
        var report = Group(
            [
                Row("A1", Side.Left, "A", quantity: 2m, cash: 2m),
                Row("A2", Side.Left, "A", quantity: 3m, cash: 3m),
                Row("B1", Side.Left, "B", quantity: 1m, cash: 1m),
                Row("B2", Side.Left, "B", quantity: 4m, cash: 4m),
            ],
            [
                Row("RA", Side.Right, "A", quantity: 5m, cash: 5m),
                Row("RB", Side.Right, "B", quantity: 5m, cash: 5m),
            ],
            SearchLimits.Default with { MaxBlockItems = 3 });

        Assert.Equal(2, report.Committed.Count);
        Assert.Equal(["A1", "A2"], report.Committed.Single(match => match.Right[0].Context.RowKey == "RA").Left.Select(row => row.Context.RowKey).OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(["B1", "B2"], report.Committed.Single(match => match.Right[0].Context.RowKey == "RB").Left.Select(row => row.Context.RowKey).OrderBy(key => key, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void Blocks_the_whole_component_when_a_wildcard_merges_it_over_the_limit()
    {
        var left = Enumerable.Range(0, 4).Select(index => Row($"L{index}", Side.Left, null)).ToArray();
        var report = Group(left, [Row("R1", Side.Right, "A"), Row("R2", Side.Right, "B")], SearchLimits.Default with { MaxBlockItems = 3 });

        Assert.Empty(report.Committed);
        Assert.Equal(0, report.Counters.SubsetSearches);
        Assert.Equal([["R1", "R2", "L0", "L1", "L2", "L3"]], BlockedKeys(report));
    }

    [Fact]
    public void Throws_when_the_block_is_built_after_cancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            CascadeMatcher.Run([Row("L", Side.Left, null)], [Row("R", Side.Right, null)], [Passes[2]], Fields, SearchLimits.Default, source.Token));
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

    private static readonly SearchLimits Blocked = SearchLimits.Default with { MaxTargetsPerComponent = 0 };

    private static MatchReport Run(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right, SearchLimits? limits = null) =>
        CascadeMatcher.Run(left, right, Passes, Fields, limits ?? SearchLimits.Default);

    private static MatchReport Group(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right, SearchLimits limits) =>
        CascadeMatcher.Run(left, right, [Passes[2]], Fields, limits);

    private static string[][] BlockedKeys(MatchReport report) =>
        [.. report.Residue.Where(cluster => cluster.Code == "BlockTooLarge").Select(cluster => cluster.Rows.Select(row => row.Context.RowKey).ToArray())];

    private static ConsolidatedRow Row(string id, Side side, string? tradeRef, string scheme = "shared", decimal quantity = 1m, decimal cash = 1m) =>
        Trade(id, side, "JPM", "US1", quantity, 10m, cash, tradeRef, scheme);

    private static ConsolidatedRow Trade(string id, Side side, string party, string instrument, decimal quantity, decimal price, decimal cash, string? tradeRef, string scheme = "shared")
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
            row = Identifier(row, "TradeRef", scheme, tradeRef);
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
