using System.Collections.Immutable;
using System.Globalization;

namespace RecDiffy.Tests;

// #41, #407. ComponentPartition builds a block's components in one pass. The fold it replaced is kept here, as it was
// in GroupIndex on dev at 705cf92, as the oracle: for generated blocks the two give the same components, in the same
// order, with their targets in the same order. The named cases below are the ones a plain group-by-value gets wrong.
public class ComponentPartitionOracleTests
{
    private static readonly MatchKeys.SharedClaim Wild = new(true, null);

    // Values A to D are on the many side when the block says so; X and Y never are.
    private static readonly string[] Values = ["A", "B", "C", "D", "X", "Y"];

    public static TheoryData<int, int, int, int> Mixes => new()
    {
        // wildcard targets per 100, many-side wildcards per 100, many-side rows, targets
        { 0, 0, 12, 40 },
        { 5, 0, 12, 40 },
        { 30, 0, 12, 40 },
        { 100, 0, 12, 12 },
        { 0, 10, 12, 40 },
        { 20, 10, 12, 40 },
        { 0, 100, 6, 20 },
        { 20, 0, 0, 20 },
        { 0, 0, 0, 20 },
        { 10, 0, 1, 60 },
        { 3, 0, 30, 200 },
    };

    [Theory]
    [MemberData(nameof(Mixes))]
    public void The_one_pass_partition_gives_the_components_of_the_fold_in_the_same_order(int wildTargets, int wildMany, int manyRows, int targets)
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random((seed * 104_729) + wildTargets + (wildMany * 7) + manyRows);
            var many = Enumerable.Range(0, manyRows).Select(_ => Claim(random, wildMany, 4)).ToArray();
            var claims = Enumerable.Range(0, random.Next(targets + 1)).Select(_ => Claim(random, wildTargets, Values.Length)).ToArray();
            var present = many.Select(claim => claim.Value).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var anyMany = many.Length > 0;
            var anyManyWildcard = many.Any(claim => claim.Wildcard);

            var expected = Fold(claims, anyMany, anyManyWildcard, present.Contains);
            var actual = ComponentPartition.Of(claims, anyMany, anyManyWildcard, present.Contains);

            Assert.True(Text(expected) == Text(actual), $"Seed {seed.ToInvariant()}, claims {string.Join(' ', claims.Select(Name))}, many {string.Join(' ', many.Select(Name))}:\nfold      {Text(expected)}\none pass  {Text(actual)}");
        }
    }

    [Fact]
    public void Two_targets_with_a_value_the_many_side_does_not_hold_stay_two_components()
    {
        var components = ComponentPartition.Of([Value("X"), Value("X"), Value("A"), Value("A")], true, false, value => value is "A");

        Assert.Equal("[0] [1] [2 3]", Text(components));
    }

    [Fact]
    public void One_many_side_wildcard_puts_every_target_in_one_component()
    {
        var components = ComponentPartition.Of([Value("X"), Wild, Value("A"), Value("B")], true, true, value => value is "A");

        Assert.Equal("[0 1 2 3]", Text(components));
    }

    [Fact]
    public void A_wildcard_target_glues_the_targets_that_can_draw_a_row_and_leaves_the_rest_alone()
    {
        var components = ComponentPartition.Of([Value("A"), Value("X"), Value("B"), Wild], true, false, value => value is "A" or "B");

        Assert.Equal("[1] [0 2 3]", Text(components));
    }

    [Fact]
    public void A_wildcard_target_against_an_empty_many_side_glues_nothing()
    {
        var components = ComponentPartition.Of([Wild, Value("A"), Wild], false, false, _ => false);

        Assert.Equal("[0] [1] [2]", Text(components));
    }

    // B's component was last touched before A's, so it is listed first when the wildcard target gathers them, and a
    // target that comes after the wildcard target joins at the end.
    [Fact]
    public void Components_are_listed_by_their_last_target_and_gathered_in_that_order()
    {
        var before = ComponentPartition.Of([Value("A"), Value("B"), Value("A")], true, false, _ => true);
        var after = ComponentPartition.Of([Value("A"), Value("B"), Value("A"), Wild, Value("C"), Value("X")], true, false, value => value is not "X");

        Assert.Equal("[1] [0 2]", Text(before));
        Assert.Equal("[1 0 2 3 4] [5]", Text(after));
    }

    private static MatchKeys.SharedClaim Value(string value) => new(false, value);

    // One claim in forty is the odd one a row cannot make but the type allows: not a wildcard, and no value.
    private static MatchKeys.SharedClaim Claim(Random random, int wildPerHundred, int values) =>
        random.Next(100) < wildPerHundred ? Wild
        : random.Next(40) is 0 ? new(false, null)
        : Value(Values[random.Next(values)]);

    private static string Name(MatchKeys.SharedClaim claim) => claim.Wildcard ? "*" : claim.Value ?? "-";

    private static string Text(IEnumerable<int[]> components) =>
        string.Join(' ', components.Select(targets => "[" + string.Join(' ', targets.Select(target => target.ToString(CultureInfo.InvariantCulture))) + "]"));

    // From here down: the fold, as GroupIndex.Couple, Merge, Intersects, Occupied and NonEmpty had it. The census is
    // reduced to the three questions the fold asked of it.
    private sealed record Draft(int[] Targets, bool WildcardTarget, ImmutableHashSet<string> Values);

    private sealed record Census(bool AnyMany, bool AnyWildcard, Func<string, bool> Has);

    private static int[][] Fold(MatchKeys.SharedClaim[] claims, bool anyMany, bool anyManyWildcard, Func<string, bool> has)
    {
        var census = new Census(anyMany, anyManyWildcard, has);
        return [.. Enumerable.Range(0, claims.Length)
            .Aggregate(ImmutableList<Draft>.Empty, (components, target) => Merge(components, target, claims[target], census))
            .Select(draft => draft.Targets)];
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
        NonEmpty(claim, census) && Occupied(draft, census) && (draft.WildcardTarget || claim.Wildcard || census.AnyWildcard || (claim.Value is { } value && draft.Values.Contains(value) && census.Has(value)));

    private static bool Occupied(Draft draft, Census census) =>
        (draft.WildcardTarget && census.AnyMany) || census.AnyWildcard || draft.Values.Any(census.Has);

    private static bool NonEmpty(MatchKeys.SharedClaim claim, Census census) =>
        claim.Wildcard ? census.AnyMany : census.AnyWildcard || (claim.Value is { } value && census.Has(value));
}
