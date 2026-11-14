using System.Collections.Immutable;

namespace RecDiffy;

// The many side of one block, bucketed by the shared value each row claims. Many, Wildcards and every bucket are in
// ascending row order, and nothing writes to them after Collect returns.
internal sealed record Census(int[] Many, int[] Wildcards, ImmutableDictionary<string, int[]> Buckets)
{
    public bool Has(string value) => Buckets.TryGetValue(value, out var rows) && rows.Length > 0;

    public int[] For(string value) => Merge(Wildcards, Buckets.GetValueOrDefault(value) ?? []);

    public static Census Collect(int[] many, Func<int, MatchKeys.SharedClaim> claim, CancellationToken cancellation)
    {
        var wildcards = new List<int>();
        var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var item in many)
        {
            cancellation.ThrowIfCancellationRequested();
            var row = claim(item);
            if (row.Wildcard)
            {
                wildcards.Add(item);
                continue;
            }

            if (buckets.TryGetValue(row.Value!, out var list) is false)
                buckets[row.Value!] = list = [];
            list.Add(item);
        }

        return new(many, [.. wildcards], buckets.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }

    // Both arrays arrive sorted by index. Collect appends in that order.
    private static int[] Merge(int[] left, int[] right)
    {
        var merged = new int[left.Length + right.Length];
        var i = 0;
        var j = 0;
        var n = 0;
        while (i < left.Length && j < right.Length)
            merged[n++] = left[i] <= right[j] ? left[i++] : right[j++];
        while (i < left.Length)
            merged[n++] = left[i++];
        while (j < right.Length)
            merged[n++] = right[j++];
        return merged;
    }
}

// Issue #407. Splits one block's targets into the components the group pass searches, in the order that the left
// fold this replaces emitted them, in one pass over the targets. Two targets belong together when the claim rules
// let them draw the same many-side row: a shared value the many side holds, a wildcard on either side, or both.
//
// Positions are indexes into the ordered target list; each component is reported as positions in the order the fold
// would have placed them. A target's claim is read once, before the split, and no component is copied to add one.
internal static class ComponentPartition
{
    public static IReadOnlyList<int[]> Split(IReadOnlyList<MatchKeys.SharedClaim> claims, Census census, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(census);

        // No many-side row means no claim meets a draft, so every target is a component of its own.
        if (census.Many.Length is 0)
            return Singletons(claims.Count);

        // A many-side wildcard stands in for every value, so it draws for every target and glues the whole block.
        if (census.Wildcards.Length > 0)
            return claims.Count is 0 ? [] : [Whole(claims.Count)];

        var placed = new Draft?[claims.Count];
        var live = new LinkedList<Draft>();
        var byValue = new Dictionary<string, LinkedListNode<Draft>>(StringComparer.Ordinal);
        LinkedListNode<Draft>? glued = null;
        var joined = new List<Draft>(3);

        for (var position = 0; position < claims.Count; position++)
        {
            cancellation.ThrowIfCancellationRequested();
            var claim = claims[position];
            var value = claim is { Wildcard: false, Value: { } shared } && census.Has(shared) ? shared : null;

            // A value the many side does not hold draws nothing, so it starts a component of its own and never joins.
            if (claim.Wildcard is false && value is null)
            {
                placed[position] = new Draft(Run.Of(position), position);
                continue;
            }

            var adoptsGlue = claim.Wildcard || glued is not null;
            joined.Clear();
            if (claim.Wildcard)
            {
                joined.AddRange(live);
                live.Clear();
                byValue.Clear();
                glued = null;
            }
            else
            {
                if (glued is { } held)
                {
                    joined.Add(held.Value);
                    live.Remove(held);
                    glued = null;
                }

                if (byValue.Remove(value!, out var valued))
                {
                    joined.Add(valued.Value);
                    live.Remove(valued);
                }
            }

            // The fold kept the surviving drafts in place and appended the merged one, which is last-touch order.
            joined.Sort(static (older, newer) => older.Touch.CompareTo(newer.Touch));
            var merged = new Draft(Chain(joined, position), position);
            if (adoptsGlue)
                glued = live.AddLast(merged);
            else
                byValue[value!] = live.AddLast(merged);
        }

        // A draft is placed at the target that last touched it, which reads back in the order the fold held.
        foreach (var draft in live)
            placed[draft.Touch] = draft;

        return [.. placed.OfType<Draft>().Select(draft => draft.Run.ToArray())];
    }

    // The joined drafts keep their order, oldest first, and the target that merged them is appended.
    private static Run Chain(List<Draft> joined, int target)
    {
        var run = Run.Of(target);
        for (var index = joined.Count - 1; index >= 0; index--)
            run = joined[index].Run.Then(run);
        return run;
    }

    private static int[][] Singletons(int count)
    {
        var singles = new int[count][];
        for (var position = 0; position < count; position++)
            singles[position] = [position];
        return singles;
    }

    private static int[] Whole(int count)
    {
        var all = new int[count];
        for (var position = 0; position < count; position++)
            all[position] = position;
        return all;
    }

    private sealed record Draft(Run Run, int Touch);

    // A component's targets in emission order. A merge splices runs by linking the older tail to the newer head, so
    // appending a target to a component costs one link instead of a copy of every target already in it.
    private sealed class Run(Node head, Node tail)
    {
        private Node Tail { get; } = tail;

        private Node Head { get; } = head;

        public static Run Of(int target)
        {
            var node = new Node(target);
            return new Run(node, node);
        }

        public Run Then(Run next)
        {
            Tail.Next = next.Head;
            return new Run(Head, next.Tail);
        }

        public int[] ToArray()
        {
            var length = 0;
            for (var node = Head; node is not null; node = node.Next)
                length++;
            var targets = new int[length];
            var index = 0;
            for (var node = Head; node is not null; node = node.Next)
                targets[index++] = node.Target;
            return targets;
        }
    }

    private sealed class Node(int target)
    {
        public int Target { get; } = target;

        public Node? Next { get; set; }
    }
}
