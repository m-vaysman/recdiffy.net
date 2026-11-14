using System.Collections.Immutable;

namespace RecDiffy.Tests;

// Issue #407: ComponentPartition replaces the left fold that grouped one block's targets. The fold is reproduced
// here, private to this file, so every case compares the one-pass partition against the exact behaviour it
// replaces: the same components, with the same targets, in the same emission order.
public class ComponentPartitionTests
{
    private static readonly string[] Values = ["A", "B", "C", "D", "gone", "other"];

    [Fact]
    public void Two_targets_sharing_a_value_the_many_side_does_not_hold_stay_apart()
    {
        var split = Split([Claim("gone"), Claim("gone")], [Claim("A")]);

        Assert.Equal([[0], [1]], split);
    }

    [Fact]
    public void One_many_side_wildcard_puts_every_target_in_one_component()
    {
        var split = Split([Claim("A"), Claim("B"), Claim("A"), Claim("gone")], [Wildcard(), Claim("A")]);

        Assert.Equal([[0, 1, 2, 3]], split);
    }

    [Fact]
    public void A_one_side_wildcard_glues_every_value_the_many_side_holds_and_leaves_the_absent_value_alone()
    {
        var split = Split([Claim("A"), Wildcard(), Claim("B"), Claim("gone")], [Claim("A"), Claim("B")]);

        Assert.Equal([[0, 1, 2], [3]], split);
    }

    [Fact]
    public void A_wildcard_target_joins_nothing_when_no_live_component_holds_a_value_the_many_side_has()
    {
        var split = Split([Claim("gone"), Wildcard(), Claim("gone")], [Claim("A")]);

        Assert.Equal([[0], [1], [2]], split);
    }

    [Fact]
    public void Repeated_identities_form_one_component_holding_every_target()
    {
        var split = Split([Claim("A"), Claim("A"), Claim("A")], [Claim("A"), Claim("A"), Claim("A")]);

        Assert.Equal([[0, 1, 2]], split);
    }

    [Fact]
    public void Disjoint_values_stay_one_component_each_in_first_touch_order()
    {
        var split = Split([Claim("C"), Claim("A"), Claim("B")], [Claim("A"), Claim("B"), Claim("C")]);

        Assert.Equal([[0], [1], [2]], split);
    }

    [Fact]
    public void A_wildcard_target_absorbs_every_live_component_and_appends_itself_last()
    {
        var split = Split([Claim("C"), Claim("B"), Claim("A"), Wildcard()], [Claim("A"), Claim("B"), Claim("C")]);

        Assert.Equal([[0, 1, 2, 3]], split);
    }

    [Fact]
    public void A_value_target_after_a_wildcard_joins_the_glued_component()
    {
        var split = Split([Wildcard(), Claim("B"), Claim("B")], [Claim("A"), Claim("B")]);

        Assert.Equal([[0, 1, 2]], split);
    }

    [Fact]
    public void A_block_with_no_many_side_row_makes_every_target_its_own_component()
    {
        var split = Split([Claim("A"), Wildcard(), Claim("A")], []);

        Assert.Equal([[0], [1], [2]], split);
    }

    [Fact]
    public void A_block_with_no_targets_partitions_to_nothing()
    {
        Assert.Empty(Split([], [Claim("A")]));
    }

    [Fact]
    public void The_partition_stops_when_the_run_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ComponentPartition.Split([Claim("A"), Claim("B")], Census([Claim("A"), Claim("B")]), source.Token));
    }

    // Every shape the claim rules allow, over a seeded sweep, compared against the fold it replaces.
    [Fact]
    public void Seeded_blocks_partition_exactly_like_the_fold()
    {
        for (var seed = 0; seed < 2_000; seed++)
        {
            var random = new Random(seed);
            var many = ManyClaims(random);
            var claims = TargetClaims(random);

            var split = ComponentPartition.Split(claims, Census(many), CancellationToken.None);
            var folded = Fold(claims, Census(many));

            Assert.True(
                Same(folded, split),
                $"seed {seed}: the fold gave [{string.Join("], [", folded.Select(Show))}] and the partition gave [{string.Join("], [", split.Select(Show))}].");
        }
    }

    private static MatchKeys.SharedClaim[] ManyClaims(Random random) =>
        [.. Enumerable.Range(0, random.Next(0, 5)).Select(_ => random.Next(4) is 0 ? Wildcard() : Claim(Values[random.Next(Values.Length)]))];

    private static MatchKeys.SharedClaim[] TargetClaims(Random random) =>
        [.. Enumerable.Range(0, random.Next(1, 25)).Select(_ => random.Next(4) is 0 ? Wildcard() : Claim(Values[random.Next(Values.Length)]))];

    private static IReadOnlyList<int[]> Split(MatchKeys.SharedClaim[] claims, MatchKeys.SharedClaim[] many) =>
        ComponentPartition.Split(claims, Census(many), CancellationToken.None);

    private static Census Census(MatchKeys.SharedClaim[] many) =>
        RecDiffy.Census.Collect([.. Enumerable.Range(0, many.Length)], index => many[index], CancellationToken.None);

    // The fold this change replaces, kept as it was: every target scans every component so far and the merged
    // component copies the targets of each one it absorbs.
    private static List<int[]> Fold(MatchKeys.SharedClaim[] claims, Census census)
    {
        var components = ImmutableList<Draft>.Empty;
        for (var target = 0; target < claims.Length; target++)
            components = Merge(components, target, claims[target], census);

        return [.. components.Select(component => component.Targets)];
    }

    private static ImmutableList<Draft> Merge(ImmutableList<Draft> components, int target, MatchKeys.SharedClaim claim, Census census)
    {
        var joined = components.Where(component => Intersects(component, claim, census)).ToArray();
        var kept = components.Where(component => joined.Contains(component) is false);
        var values = joined.Aggregate(
            claim.Wildcard || claim.Value is null ? ImmutableHashSet.Create<string>(StringComparer.Ordinal) : ImmutableHashSet.Create(StringComparer.Ordinal, claim.Value),
            (set, component) => set.Union(component.Values));
        var merged = new Draft(
            [.. joined.SelectMany(component => component.Targets), target],
            claim.Wildcard || joined.Any(component => component.WildcardTarget),
            values);
        return [.. kept, merged];
    }

    private static bool Intersects(Draft draft, MatchKeys.SharedClaim claim, Census census) =>
        NonEmpty(claim, census) && Occupied(draft, census) && (draft.WildcardTarget || claim.Wildcard || census.Wildcards.Length > 0 || (claim.Value is { } value && draft.Values.Contains(value) && census.Has(value)));

    private static bool Occupied(Draft draft, Census census) =>
        (draft.WildcardTarget && census.Many.Length > 0) || census.Wildcards.Length > 0 || draft.Values.Any(census.Has);

    private static bool NonEmpty(MatchKeys.SharedClaim claim, Census census) =>
        claim.Wildcard ? census.Many.Length > 0 : census.Wildcards.Length > 0 || (claim.Value is { } value && census.Has(value));

    private static string Show(int[] targets) => string.Join(", ", targets);

    // int[] compares by reference, so the component lists are compared target by target.
    private static bool Same(IReadOnlyList<int[]> left, IReadOnlyList<int[]> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First.SequenceEqual(pair.Second));

    private static MatchKeys.SharedClaim Claim(string value) => new(false, value);

    private static MatchKeys.SharedClaim Wildcard() => new(true, null);

    private sealed record Draft(int[] Targets, bool WildcardTarget, ImmutableHashSet<string> Values);
}
