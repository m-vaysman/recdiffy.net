using System.Globalization;

namespace RecDiffy.Benchmarks;

// Issue #64. Each shape is a seeded input together with the outcome it was built to give.
public enum BenchShape
{
    ExactPairs,
    Unmatched,
    SmallAllocations,
    SplittableBlocks,
    OversizedComponents,
    DisjointTargets,
    WildcardHeavy,
    RepeatedIdentity,
}

// Scheme and Reference are the TradeRef identifier. Both are null when the row has none.
public sealed record BenchTrade(string Id, Side Side, string Instrument, decimal Quantity, decimal Price, decimal NetCash, string? Scheme, string? Reference);

// Matches counts committed groups. Every other field counts rows.
public sealed record BenchOutcome(int Matches, int MatchedRows, int AmbiguousRows, int BlockedRows, int ExhaustedRows, int LeftOnlyRows, int RightOnlyRows)
{
    public static BenchOutcome Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

// Rows is half the total row count. Width is the number of targets that share one block.
public sealed record BenchDataset(BenchShape Shape, int Rows, int Width, IReadOnlyList<BenchTrade> Left, IReadOnlyList<BenchTrade> Right, BenchOutcome Expected);

// The many side is the left side. A group target is one right row, and its allocations are left rows on the same block key.
// Allocation quantities sit in [1,000, 2,000), so a target, the sum of two or more of them, never equals one allocation.
public static class BenchDatasets
{
    public const int Seed = 64;

    private const string Shared = "shared";
    private const string Broker = "broker";

    public static int DefaultWidth(BenchShape shape) => shape switch
    {
        BenchShape.SplittableBlocks => 12,
        BenchShape.DisjointTargets => 32,
        BenchShape.WildcardHeavy or BenchShape.RepeatedIdentity => 4,
        _ => 1,
    };

    public static BenchDataset Create(BenchShape shape, int rows) => Create(shape, rows, DefaultWidth(shape));

    public static BenchDataset Create(BenchShape shape, int rows, int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        if (shape is BenchShape.ExactPairs or BenchShape.Unmatched && width is not 1)
            throw new ArgumentOutOfRangeException(nameof(width), width, "A pair shape has one target per block.");

        var random = new Random(Seed);
        var units = shape switch
        {
            BenchShape.ExactPairs => Pairs(random, rows),
            BenchShape.Unmatched => Unmatched(random, rows),
            BenchShape.SmallAllocations => Blocks(random, rows, width, (2, 4), Claim.None),
            BenchShape.SplittableBlocks => Blocks(random, rows, width, (4, 8), Claim.Distinct),
            BenchShape.OversizedComponents => Blocks(random, rows, width, (25, 40), Claim.None),
            BenchShape.DisjointTargets => Blocks(random, rows, width, (2, 4), Claim.Distinct),
            BenchShape.WildcardHeavy => Blocks(random, rows, width, (2, 3), Claim.Wildcard),
            BenchShape.RepeatedIdentity => Blocks(random, rows, width, (2, 3), Claim.Repeated),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape."),
        };
        var left = Shuffle(random, units.SelectMany(unit => unit.Left));
        var right = Shuffle(random, units.SelectMany(unit => unit.Right));
        var expected = units.Select(unit => unit.Expected).Aggregate(BenchOutcome.Empty, Add);
        return new BenchDataset(shape, rows, width, left, right, expected);
    }

    private static Unit[] Pairs(Random random, int count) =>
        [.. Enumerable.Range(0, count).Select(index => Pair(random, Name("P", index)))];

    // Half the rows pair on the natural key. The rest have no counterpart on the other side.
    private static Unit[] Unmatched(Random random, int rows)
    {
        var pairs = rows / 2;
        var alone = rows - pairs;
        Unit[] matched = [.. Enumerable.Range(0, pairs).Select(index => Pair(random, Name("P", index)))];
        Unit[] leftOnly = [.. Enumerable.Range(0, alone).Select(index => Alone(random, Side.Left, Name("UL", index)))];
        Unit[] rightOnly = [.. Enumerable.Range(0, alone).Select(index => Alone(random, Side.Right, Name("UR", index)))];
        return [.. matched, .. leftOnly, .. rightOnly];
    }

    private static Unit Pair(Random random, string instrument)
    {
        var price = Price(random);
        var quantity = Amount(random, 1, 100_000);
        var cash = Cash(random, quantity, price);
        return new Unit(
            [new BenchTrade("L-" + instrument, Side.Left, instrument, quantity, price, cash, null, null)],
            [new BenchTrade("R-" + instrument, Side.Right, instrument, quantity, price, cash, null, null)],
            BenchOutcome.Empty with { Matches = 1, MatchedRows = 2 });
    }

    private static Unit Alone(Random random, Side side, string instrument)
    {
        var price = Price(random);
        var quantity = Amount(random, 1, 100_000);
        var trade = new BenchTrade((side is Side.Left ? "L-" : "R-") + instrument, side, instrument, quantity, price, Cash(random, quantity, price), null, null);
        return side is Side.Left
            ? new Unit([trade], [], BenchOutcome.Empty with { LeftOnlyRows = 1 })
            : new Unit([], [trade], BenchOutcome.Empty with { RightOnlyRows = 1 });
    }

    // A component is one target and its allocations. Width components share a block key, and the last block may hold fewer.
    private static Unit[] Blocks(Random random, int rows, int width, (int Min, int Max) many, Claim claim)
    {
        var sizes = Partition(random, 2 * rows, many.Min + 1, many.Max + 1);
        return [.. sizes.Chunk(width).Select((block, number) => Block(random, Name("B", number), block, claim))];
    }

    // Distinct references keep each component apart. Wildcards and one repeated reference glue the whole block into one.
    private static Unit Block(Random random, string instrument, int[] sizes, Claim claim)
    {
        var price = Price(random);
        Unit[] components = [.. sizes.Select((size, index) => Allocation(random, instrument, index, size - 1, price, claim))];
        var expected = claim is Claim.Distinct
            ? components.Select(component => component.Expected).Aggregate(BenchOutcome.Empty, Add)
            : Searched([.. sizes.Select(size => size - 1)]);
        return new Unit([.. components.SelectMany(component => component.Left)], [.. components.SelectMany(component => component.Right)], expected);
    }

    private static Unit Allocation(Random random, string instrument, int index, int many, decimal price, Claim claim)
    {
        var id = instrument + "-" + index.ToString(CultureInfo.InvariantCulture);
        var (manyRef, targetRef) = claim switch
        {
            Claim.Distinct => (new Ref(Shared, id), new Ref(Shared, id)),
            Claim.Wildcard => (Ref.None, new Ref(Broker, id)),
            Claim.Repeated => (new Ref(Shared, instrument), new Ref(Shared, instrument)),
            _ => (Ref.None, Ref.None),
        };
        decimal[] quantities = [.. Enumerable.Range(0, many).Select(_ => Amount(random, 1_000, 2_000))];
        decimal[] cash = [.. quantities.Select(quantity => Cash(random, quantity, price))];
        BenchTrade[] allocations =
        [
            .. quantities.Select((quantity, part) => new BenchTrade(
                "L-" + id + "-" + part.ToString(CultureInfo.InvariantCulture), Side.Left, instrument, quantity, price, cash[part], manyRef.Scheme, manyRef.Value)),
        ];
        var target = new BenchTrade("R-" + id, Side.Right, instrument, quantities.Sum(), price, cash.Sum(), targetRef.Scheme, targetRef.Value);
        return new Unit(allocations, [target], Searched([many]));
    }

    // One component, holding every target in many and the rows they can draw. GroupIndex.Decide applies both limits
    // before any search. Each target's allocations are then its only subset that conserves quantity and cash.
    private static BenchOutcome Searched(IReadOnlyList<int> many)
    {
        var limits = SearchLimits.Default;
        var targets = many.Count;
        var eligible = many.Sum();
        return targets > limits.MaxTargetsPerComponent || eligible > limits.MaxBlockItems
            ? BenchOutcome.Empty with { BlockedRows = targets + eligible }
            : BenchOutcome.Empty with { Matches = targets, MatchedRows = targets + eligible };
    }

    // Sizes in [min, max] that add up to total. The count is near total over the mean size, and the spare rows are spread at random.
    private static int[] Partition(Random random, int total, int min, int max)
    {
        var fewest = (total + max - 1) / max;
        var most = total / min;
        if (fewest > most)
            throw new ArgumentOutOfRangeException(nameof(total), total, string.Create(CultureInfo.InvariantCulture, $"No sizes from {min} to {max} add up to {total}."));

        var count = Math.Clamp((int)Math.Round(2.0 * total / (min + max), MidpointRounding.ToEven), fewest, most);
        var room = max - min;
        var spare = total - (count * min);
        var sizes = new int[count];
        for (var index = 0; index < count; index++)
        {
            var after = (count - index - 1) * room;
            var extra = random.Next(Math.Max(0, spare - after), Math.Min(room, spare) + 1);
            sizes[index] = min + extra;
            spare -= extra;
        }

        return sizes;
    }

    private static BenchTrade[] Shuffle(Random random, IEnumerable<BenchTrade> trades)
    {
        BenchTrade[] shuffled = [.. trades];
        random.Shuffle(shuffled);
        return shuffled;
    }

    private static BenchOutcome Add(BenchOutcome sum, BenchOutcome next) => new(
        sum.Matches + next.Matches,
        sum.MatchedRows + next.MatchedRows,
        sum.AmbiguousRows + next.AmbiguousRows,
        sum.BlockedRows + next.BlockedRows,
        sum.ExhaustedRows + next.ExhaustedRows,
        sum.LeftOnlyRows + next.LeftOnlyRows,
        sum.RightOnlyRows + next.RightOnlyRows);

    private static string Name(string prefix, int number) => prefix + number.ToString(CultureInfo.InvariantCulture);

    // Cents in [from, to).
    private static decimal Amount(Random random, int from, int to) => random.Next(from * 100, to * 100) / 100m;

    private static decimal Price(Random random) => random.Next(500_000, 1_500_000) / 10_000m;

    // Net cash is quantity times price plus a fee, so a different subset with the same quantity rarely has the same cash.
    private static decimal Cash(Random random, decimal quantity, decimal price) =>
        Math.Round(quantity * price, 2, MidpointRounding.ToEven) + (random.Next(0, 5_000) / 100m);

    private enum Claim
    {
        None,
        Distinct,
        Wildcard,
        Repeated,
    }

    private readonly record struct Ref(string? Scheme, string? Value)
    {
        public static Ref None => default;
    }

    private sealed record Unit(BenchTrade[] Left, BenchTrade[] Right, BenchOutcome Expected);
}
