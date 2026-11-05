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
    public static TargetSearch Search(IReadOnlyList<ConservedClass> classes, decimal[] target, int maxGroup, int maxNodes, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var counts = new int[classes.Count];
        var sums = new decimal[target.Length];
        var suffix = new decimal[classes.Count + 1];
        for (var index = classes.Count - 1; index >= 0; index--)
            suffix[index] = suffix[index + 1] + classes[index].Values[0] * classes[index].Items.Length;
        var nonNegative = classes.All(cls => cls.Values[0] >= 0m);
        var walk = new Walk(classes, target, maxGroup, maxNodes, counts, sums, suffix, nonNegative, cancellation);
        walk.Go(0, 0);
        return walk.Finish();
    }

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
                Add(cls, k);
                if (nonNegative && sums[0] > target[0])
                {
                    Add(cls, -k);
                    continue;
                }

                if (nonNegative && sums[0] + suffix[index + 1] < target[0])
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
            if (!whole || _solution is not null)
            {
                _contest = true;
                _stop = true;
                return;
            }

            _solution = [.. counts];
        }

        private void Add(ConservedClass cls, int count)
        {
            for (var field = 0; field < sums.Length; field++)
                sums[field] += cls.Values[field] * count;
        }
    }
}
