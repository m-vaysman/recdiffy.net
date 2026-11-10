using System.Globalization;

namespace RecDiffy;

internal enum SearchState
{
    None,
    Unique,
    Contest,
    Incomplete,
}

internal sealed record ConservedClass(decimal[] Values, int[] Items);

internal readonly record struct TargetSearch(SearchState State, int[] DrawnItems);

// The walk mutates a count buffer and restores it. A 250,000-node budget has to stay a node budget.
internal static class SubsetSearch
{
    // The suffix bound uses the first conserved value and assumes every later item can be taken.
    // A group-size cap makes that an overestimate, so the bound prunes less. A bound past the decimal range is held at
    // MaxValue, still an overestimate. The bound is read only when every value is non-negative, so it only grows (#224).
    // The inputs are checked before the walk starts (#245 review). The target and every class hold one value per conserved
    // field, and every class holds at least one item. No classes is a search with no group, not an error.
    public static TargetSearch Search(IReadOnlyList<ConservedClass> classes, decimal[] target, int maxGroup, int maxNodes, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(target);
        if (target is [])
            throw new ArgumentException("The target holds no conserved value.", nameof(target));
        if (classes.Select(cls => Problem(cls, target.Length)).FirstOrDefault(problem => problem is not null) is { } problem)
            throw new ArgumentException(problem, nameof(classes));
        cancellation.ThrowIfCancellationRequested();
        if (classes is [])
            return new TargetSearch(SearchState.None, []);
        var counts = new int[classes.Count];
        var sums = new decimal[target.Length];
        var suffix = new decimal[classes.Count + 1];
        for (var index = classes.Count - 1; index >= 0; index--)
            suffix[index] = CheckedDecimal.Add(suffix[index + 1], CheckedDecimal.Multiply(classes[index].Values[0], classes[index].Items.Length)) ?? decimal.MaxValue;
        var nonNegative = classes.All(cls => cls.Values[0] >= 0m);
        var walk = new Walk(classes, target, maxGroup, maxNodes, counts, sums, suffix, nonNegative, cancellation);
        walk.Go(0, 0);
        return walk.Finish();
    }

    private static string? Problem(ConservedClass? cls, int width) => cls switch
    {
        null => "A class is null.",
        { Values: null } => "A class has no conserved values.",
        { Items: null or [] } => "A class has no items.",
        { Values.Length: var length } when length != width => string.Create(CultureInfo.InvariantCulture, $"A class holds {length} conserved values and the target holds {width}."),
        _ => null,
    };

    private sealed class Walk(
        IReadOnlyList<ConservedClass> classes,
        decimal[] target,
        int maxGroup,
        int maxNodes,
        int[] counts,
        decimal[] sums,
        decimal[] suffix,
        bool nonNegative,
        CancellationToken cancellation)
    {
        private int _nodes;
        private bool _stop;
        private bool _incomplete;
        private bool _contest;
        private int[]? _solution;
        private readonly List<int> _drawn = [];

        // Counts descend. The suffix test breaks out of the count loop, because a smaller count cannot close a gap
        // the maximum remainder already missed.
        public void Go(int index, int size)
        {
            cancellation.ThrowIfCancellationRequested();
            _nodes++;
            if (_nodes > maxNodes)
            {
                _incomplete = true;
                _stop = true;
                return;
            }

            if (index == classes.Count)
            {
                // One row is not a group, even when its conserved values equal the target.
                if (size >= 2 && size <= maxGroup && Conserved())
                    Record();
                return;
            }

            var cls = classes[index];
            var room = cls.Items.Length;
            for (var k = room; k >= 0 && !_stop; k--)
            {
                var next = size + k;
                if (next > maxGroup)
                    continue;
                // A count whose sums leave the decimal range is not a group, so the walk skips it (#224).
                if (Fits(cls, k) is false)
                    continue;
                Add(cls, k);
                if (nonNegative && sums[0] > target[0])
                {
                    Add(cls, -k);
                    continue;
                }

                if (nonNegative && CheckedDecimal.Add(sums[0], suffix[index + 1]) is { } reach && reach < target[0])
                {
                    Add(cls, -k);
                    break;
                }

                counts[index] = k;
                Go(index + 1, next);
                Add(cls, -k);
            }

            counts[index] = 0;
        }

        // Incomplete wins over a contest. A walk that ran out of nodes and also found two solutions is Incomplete.
        public TargetSearch Finish() => new(
            _incomplete ? SearchState.Incomplete : _contest ? SearchState.Contest : _solution is null ? SearchState.None : SearchState.Unique,
            [.. _drawn.Distinct()]);

        private bool Conserved()
        {
            for (var field = 0; field < target.Length; field++)
            {
                if (sums[field] != target[field])
                    return false;
            }

            return true;
        }

        private void Record()
        {
            var whole = counts.Select((count, index) => count is 0 || count == classes[index].Items.Length).All(held => held);
            _drawn.AddRange(classes.SelectMany((cls, index) => counts[index] > 0 ? cls.Items : []));
            // Taking part of a class, or finding a second solution, is a contest and stops the walk.
            if (!whole || _solution is not null)
            {
                _contest = true;
                _stop = true;
                return;
            }

            _solution = [.. counts];
        }

        private bool Fits(ConservedClass cls, int count)
        {
            for (var field = 0; field < sums.Length; field++)
            {
                if (CheckedDecimal.Add(sums[field], CheckedDecimal.Multiply(cls.Values[field], count)) is null)
                    return false;
            }

            return true;
        }

        private void Add(ConservedClass cls, int count)
        {
            for (var field = 0; field < sums.Length; field++)
                sums[field] += cls.Values[field] * count;
        }
    }
}
