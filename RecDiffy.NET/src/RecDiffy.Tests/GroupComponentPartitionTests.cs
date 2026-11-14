using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #407, for #41. Component membership is the fold's result: an absent value does not glue, a many-side
// wildcard glues every target, and a one-side wildcard glues only the values the many side has.
// These rows are built here, so the file does not need the benchmark project.
public class GroupComponentPartitionTests
{
    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
    ];

    private static readonly PassDeclaration[] Passes =
    [
        new("G", "match.groupSum", [], ["Counterparty"], ["Quantity"], "left", null),
    ];

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    public void Disjoint_targets_stay_separate_and_each_one_commits(int width)
    {
        var (left, right) = Disjoint(width);
        var built = Build(left, right);

        Assert.Equal(width, built.Counters.Components);
        var report = Run(left, right);
        Assert.Equal(width, report.Committed.Count);
        Assert.All(report.Committed, match =>
        {
            var target = Assert.Single(match.Right).Context.RowKey;
            var suffix = target[1..];
            Assert.Equal(
                new[] { "L" + suffix + "-0", "L" + suffix + "-1" },
                match.Left.Select(row => row.Context.RowKey).Order(StringComparer.Ordinal).ToArray());
        });
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(64, 0)]
    public void Repeated_identity_is_one_component_and_commits_until_the_target_limit(int width, int matches)
    {
        var (left, right) = Repeated(width);
        var built = Build(left, right);

        Assert.Equal(1, built.Counters.Components);
        Assert.Equal(matches, Run(left, right).Committed.Count);
        if (matches is 0)
        {
            var blocked = Assert.Single(built.Index.Residue());
            Assert.Equal(("BlockTooLarge", left.Length + right.Length), (blocked.Code, blocked.Rows.Count));
        }
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(64, 0)]
    public void A_wildcard_heavy_block_is_one_component_and_commits_until_the_target_limit(int width, int matches)
    {
        var (left, right) = Wildcard(width);
        var built = Build(left, right);

        Assert.Equal(1, built.Counters.Components);
        Assert.Equal(matches, Run(left, right).Committed.Count);
        if (matches is 0)
        {
            var blocked = Assert.Single(built.Index.Residue());
            Assert.Equal("BlockTooLarge", blocked.Code);
        }
    }

    [Fact]
    public void Two_targets_with_the_same_absent_value_are_two_components()
    {
        ConsolidatedRow[] left = [Row("LA", Side.Left, 1m, "shared", "A"), Row("LB", Side.Left, 2m, "shared", "A")];
        ConsolidatedRow[] right = [Row("T1", Side.Right, 3m, "shared", "Z"), Row("T2", Side.Right, 3m, "shared", "Z")];

        var built = Build(left, right);

        Assert.Equal(2, built.Counters.Components);
        Assert.Empty(Run(left, right).Committed);
    }

    [Fact]
    public void One_many_side_wildcard_puts_every_target_in_one_component()
    {
        ConsolidatedRow[] left = [Row("L0", Side.Left, 1m, null, null), Row("L1", Side.Left, 2m, "shared", "A")];
        ConsolidatedRow[] right = [Row("TA", Side.Right, 4m, "shared", "A"), Row("TB", Side.Right, 8m, "shared", "B")];

        Assert.Equal(1, Build(left, right).Counters.Components);
    }

    [Fact]
    public void A_one_side_wildcard_glues_census_values_and_leaves_an_absent_value_alone()
    {
        ConsolidatedRow[] left = [Row("LA", Side.Left, 1m, "shared", "A"), Row("LB", Side.Left, 2m, "shared", "B"), Row("LX", Side.Left, 4m, "shared", "X")];
        ConsolidatedRow[] right =
        [
            Row("TA", Side.Right, 1m, "shared", "A"),
            Row("TB", Side.Right, 2m, "shared", "B"),
            Row("TA2", Side.Right, 1m, "shared", "A"),
            Row("TW", Side.Right, 4m, "broker", "W"),
            Row("TZ", Side.Right, 8m, "shared", "Z"),
        ];
        var limits = SearchLimits.Default with { MaxTargetsPerComponent = 2 };

        var built = Build(left, right, limits);

        Assert.Equal(2, built.Counters.Components);
        var blocked = Assert.Single(built.Index.Residue());
        Assert.Equal("BlockTooLarge", blocked.Code);
        Assert.Equal(["TB", "TA", "TA2", "TW", "LB", "LA", "LX"], Keys(blocked));
    }

    [Fact]
    public void A_repeated_identity_component_emits_the_census_value_once()
    {
        ConsolidatedRow[] left = [Row("L0", Side.Left, 1m, "shared", "A"), Row("L1", Side.Left, 2m, "shared", "A"), Row("L2", Side.Left, 4m, "shared", "B")];
        ConsolidatedRow[] right = [Row("R0", Side.Right, 3m, "shared", "A"), Row("R1", Side.Right, 3m, "shared", "A"), Row("R2", Side.Right, 3m, "shared", "A")];
        var blockedLimits = SearchLimits.Default with { MaxTargetsPerComponent = 2 };
        var dedupedLimits = SearchLimits.Default with { MaxBlockItems = 2 };

        var blocked = Build(left, right, blockedLimits);
        var deduped = Build(left, right, dedupedLimits);

        Assert.Equal(1, blocked.Counters.Components);
        Assert.Equal(["R0", "R1", "R2", "L0", "L1"], Keys(Assert.Single(blocked.Index.Residue())));
        Assert.Equal(1, deduped.Counters.Components);
        Assert.DoesNotContain(deduped.Index.Residue(), cluster => cluster.Code == "BlockTooLarge");
    }

    [Fact]
    public void The_partition_reads_each_target_once()
    {
        var targets = Enumerable.Range(0, 20).ToArray();
        var reads = new List<int>();

        var groups = ComponentPartition.Partition(
            targets,
            index =>
            {
                reads.Add(index);
                return new MatchKeys.SharedClaim(false, index % 2 is 0 ? "A" : "Z");
            },
            manySideWildcard: false,
            manySideHasRows: true,
            value => value is "A",
            CancellationToken.None);

        Assert.Equal(targets, reads);
        Assert.Equal(Fold(targets, [.. targets.Select(index => new MatchKeys.SharedClaim(false, index % 2 is 0 ? "A" : "Z"))], false, true, value => value is "A"), Describe(groups));
    }

    [Fact]
    public void The_partition_stops_between_target_claims_when_the_run_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        var reads = 0;

        Assert.Throws<OperationCanceledException>(() => ComponentPartition.Partition(
            Enumerable.Range(0, 10).ToArray(),
            _ =>
            {
                reads++;
                if (reads == 3)
                    source.Cancel();
                return new MatchKeys.SharedClaim(true, null);
            },
            manySideWildcard: false,
            manySideHasRows: true,
            _ => false,
            source.Token));
        Assert.Equal(3, reads);
    }

    [Fact]
    public void Every_short_claim_sequence_matches_the_fold()
    {
        MatchKeys.SharedClaim[] kinds = [new(true, null), new(false, "A"), new(false, "B"), new(false, "Z")];
        (bool ManyWildcard, bool ManyRows)[] modes = [(false, false), (false, true), (true, true)];
        foreach (var length in Enumerable.Range(0, 8))
        {
            var space = 1;
            for (var power = 0; power < length; power++)
                space *= kinds.Length;
            for (var code = 0; code < space; code++)
            {
                var targets = new int[length];
                var claims = new MatchKeys.SharedClaim[length];
                var cursor = code;
                for (var index = 0; index < length; index++)
                {
                    targets[index] = index;
                    claims[index] = kinds[cursor % kinds.Length];
                    cursor /= kinds.Length;
                }

                foreach (var (manyWildcard, manyRows) in modes)
                {
                    var has = (Func<string, bool>)(manyRows ? value => value is "A" or "B" : _ => false);
                    var reads = 0;
                    var actual = ComponentPartition.Partition(
                        targets,
                        index =>
                        {
                            reads++;
                            return claims[index];
                        },
                        manyWildcard,
                        manyRows,
                        has,
                        CancellationToken.None);
                    var expected = Fold(targets, claims, manyWildcard, manyRows, has);
                    var actualText = Describe(actual);
                    Assert.Equal(length, reads);
                    Assert.True(
                        expected == actualText,
                        "length " + length.ToInvariant() + " code " + code.ToInvariant()
                        + " manyWildcard " + (manyWildcard ? "yes" : "no")
                        + " manyRows " + (manyRows ? "yes" : "no")
                        + ": fold " + expected + " partition " + actualText);
                }
            }
        }
    }

    private static (ConsolidatedRow[] Left, ConsolidatedRow[] Right) Disjoint(int width) =>
        (
            [.. Enumerable.Range(0, width).SelectMany(index => new[]
            {
                Row("L" + index.ToString(CultureInfo.InvariantCulture) + "-0", Side.Left, 1m, "shared", "R" + index.ToString(CultureInfo.InvariantCulture)),
                Row("L" + index.ToString(CultureInfo.InvariantCulture) + "-1", Side.Left, 2m, "shared", "R" + index.ToString(CultureInfo.InvariantCulture)),
            })],
            [.. Enumerable.Range(0, width).Select(index =>
                Row("R" + index.ToString(CultureInfo.InvariantCulture), Side.Right, 3m, "shared", "R" + index.ToString(CultureInfo.InvariantCulture)))]
        );

    private static (ConsolidatedRow[] Left, ConsolidatedRow[] Right) Repeated(int width) =>
        (
            [.. Enumerable.Range(0, width).SelectMany(index =>
            {
                var (first, second, _) = Amounts(width, index);
                return new[]
                {
                    Row("L" + index.ToString(CultureInfo.InvariantCulture) + "-0", Side.Left, first, "shared", "SAME"),
                    Row("L" + index.ToString(CultureInfo.InvariantCulture) + "-1", Side.Left, second, "shared", "SAME"),
                };
            })],
            [.. Enumerable.Range(0, width).Select(index =>
            {
                var (_, _, target) = Amounts(width, index);
                return Row("R" + index.ToString(CultureInfo.InvariantCulture), Side.Right, target, "shared", "SAME");
            })]
        );

    private static (ConsolidatedRow[] Left, ConsolidatedRow[] Right) Wildcard(int width) =>
        (
            [.. Enumerable.Range(0, width).SelectMany(index =>
            {
                var (first, second, _) = Amounts(width, index);
                return new[]
                {
                    Row("L" + index.ToString(CultureInfo.InvariantCulture) + "-0", Side.Left, first, null, null),
                    Row("L" + index.ToString(CultureInfo.InvariantCulture) + "-1", Side.Left, second, null, null),
                };
            })],
            [.. Enumerable.Range(0, width).Select(index =>
            {
                var (_, _, target) = Amounts(width, index);
                return Row("R" + index.ToString(CultureInfo.InvariantCulture), Side.Right, target, "broker", "B" + index.ToString(CultureInfo.InvariantCulture));
            })]
        );

    // Width above the target limit is blocked before the search, so the amounts only have to be decimals.
    // At the limit, powers of two keep each target's two rows the only subset that sums to it.
    private static (decimal First, decimal Second, decimal Target) Amounts(int width, int index) =>
        width > SearchLimits.Default.MaxTargetsPerComponent
            ? (1m, 2m, 3m)
            : (Bit(2 * index), Bit(2 * index + 1), Bit(2 * index) + Bit(2 * index + 1));

    private static decimal Bit(int bit)
    {
        var value = 1m;
        for (var shift = 0; shift < bit; shift++)
            value *= 2m;
        return value;
    }

    private static (GroupIndex Index, MatchCounters Counters) Build(ConsolidatedRow[] left, ConsolidatedRow[] right, SearchLimits? limits = null)
    {
        var cap = limits ?? SearchLimits.Default;
        ActiveItem[] items = [.. left.Concat(right).Select((row, index) => new ActiveItem(index, row.Context.Side, row))];
        var schema = Fields.ToDictionary(field => field.Name, field => field, StringComparer.Ordinal);
        var workspace = new MatchWorkspace(
            "G",
            items,
            items.Select(item => item.Index).ToImmutableHashSet(),
            schema,
            cap,
            "TradeRef",
            cap.MaxGroupSize,
            CancellationToken.None);
        return GroupIndex.Create("G", ["Counterparty"], ["Quantity"], Side.Left, cap.MaxGroupSize, workspace, new MatchCounters());
    }

    private static MatchReport Run(ConsolidatedRow[] left, ConsolidatedRow[] right) =>
        CascadeMatcher.Run(left, right, Passes, Fields, SearchLimits.Default, CancellationToken.None);

    private static string[] Keys(ResidueCluster cluster) => [.. cluster.Rows.Select(row => row.Context.RowKey)];

    private static string Describe(int[][] groups) =>
        string.Join(";", groups.Select(group => string.Join(",", group.Select(index => index.ToInvariant()))));

    private static string Fold(int[] targets, MatchKeys.SharedClaim[] claims, bool manyWildcard, bool manyRows, Func<string, bool> has)
    {
        var components = new List<FoldDraft>();
        for (var index = 0; index < targets.Length; index++)
        {
            var claim = claims[index];
            var joined = components.Where(component => Intersects(component, claim, manyWildcard, manyRows, has)).ToArray();
            var kept = components.Where(component => Array.IndexOf(joined, component) < 0).ToList();
            var values = new HashSet<string>(StringComparer.Ordinal);
            if (claim is { Wildcard: false, Value: { } own })
                values.Add(own);
            foreach (var component in joined)
                foreach (var value in component.Values)
                    values.Add(value);
            kept.Add(new FoldDraft(
                [.. joined.SelectMany(component => component.Targets), targets[index]],
                claim.Wildcard || joined.Any(component => component.Wildcard),
                values));
            components = kept;
        }

        return Describe([.. components.Select(component => component.Targets)]);
    }

    private static bool Intersects(FoldDraft draft, MatchKeys.SharedClaim claim, bool manyWildcard, bool manyRows, Func<string, bool> has) =>
        NonEmpty(claim, manyWildcard, manyRows, has)
        && Occupied(draft, manyWildcard, manyRows, has)
        && (draft.Wildcard || claim.Wildcard || manyWildcard || (claim.Value is { } value && draft.Values.Contains(value) && has(value)));

    private static bool Occupied(FoldDraft draft, bool manyWildcard, bool manyRows, Func<string, bool> has) =>
        (draft.Wildcard && manyRows) || manyWildcard || draft.Values.Any(has);

    private static bool NonEmpty(MatchKeys.SharedClaim claim, bool manyWildcard, bool manyRows, Func<string, bool> has) =>
        claim.Wildcard ? manyRows : manyWildcard || (claim.Value is { } value && has(value));

    private sealed record FoldDraft(int[] Targets, bool Wildcard, HashSet<string> Values);

    private static ConsolidatedRow Row(string id, Side side, decimal? quantity, string? scheme, string? reference)
    {
        var start = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (symbols, party) = start.Pool.InternSymbol("JPM");
        var withParty = start.WithPool(symbols).Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, party), "test", null, "JPM");
        var withQuantity = quantity is { } number
            ? withParty.Write("Quantity", CanonicalCell.FromDecimal(number), "test", null, null)
            : withParty;
        var context = scheme is null || reference is null ? withQuantity : WithReference(withQuantity, scheme, reference);
        return new ConsolidatedRow([new SourceRef(side is Side.Left ? "left" : "right", id, 0)], context, false, null, null);
    }

    private static RowContext WithReference(RowContext row, string scheme, string reference)
    {
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", reference);
        return row.WithPool(pool).Write("TradeRef", CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, reference);
    }
}
