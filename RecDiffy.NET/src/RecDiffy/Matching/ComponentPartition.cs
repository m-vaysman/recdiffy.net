namespace RecDiffy;

// The targets of one block, split into the components that are decided together (spec §4.7): two targets are coupled
// when some many-side row is eligible for both, and coupling is transitive.
//
// A target's claim on the shared reference says which rows it can draw. A wildcard claim draws every many-side row. A
// value claim draws the many-side rows with that value, and the many-side rows whose own claim is a wildcard. So:
// - a target that can draw no row is a component of its own, and two such targets with the same value stay apart;
// - when a many-side row has a wildcard claim, every target can draw it, and the block is one component;
// - otherwise the targets of one value are one component, until a target with a wildcard claim comes: it joins every
//   component that can draw a row, and every later target that can draw a row joins that one too.
//
// The order is the order of the fold this replaces (#41, #407), because decisions, ready proposals and residue follow
// it. The fold took the targets in order, compared each with every component so far, and put the component a target
// joined at the end of the list. So the components are listed by their last target. Inside a component the targets
// are in the order they joined, which is target order except where a wildcard target joined several components: those
// come first, one after another, in the order they had in the list at that moment.
internal static class ComponentPartition
{
    // Each component holds the positions of its targets in claims. One pass over the targets, and nothing built for
    // an earlier target is scanned or copied for a later one. The lists are working state and do not leave the method.
    public static int[][] Of(IReadOnlyList<MatchKeys.SharedClaim> claims, bool anyMany, bool anyManyWildcard, Func<string, bool> has)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(has);

        // endsAt[p] is the component whose last target is at position p, so reading it in order lists the components.
        var endsAt = new List<int>?[claims.Count];
        var draws = new bool[claims.Count];
        var byValue = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        List<int>? glued = null;
        for (var position = 0; position < claims.Count; position++)
        {
            var claim = claims[position];
            draws[position] = claim.Wildcard ? anyMany : anyManyWildcard || (claim.Value is { } own && has(own));
            List<int> component = (draws[position], glued, anyManyWildcard || claim.Wildcard) switch
            {
                (false, _, _) => [],
                (true, { } all, _) => all,
                (true, null, true) => glued = Gathered(endsAt, draws, position),
                _ => byValue.TryGetValue(claim.Value!, out var known) ? known : byValue[claim.Value!] = [],
            };

            // The component moves to the end of the list: it is now listed by this target.
            if (component.Count > 0)
                endsAt[component[^1]] = null;
            component.Add(position);
            endsAt[position] = component;
        }

        return [.. endsAt.OfType<List<int>>().Select(component => component.ToArray())];
    }

    // Every component so far that can draw a row, joined in list order and taken out of the list. This runs once for a
    // block, when its first wildcard target comes.
    private static List<int> Gathered(List<int>?[] endsAt, bool[] draws, int before)
    {
        var gathered = new List<int>();
        for (var position = 0; position < before; position++)
        {
            if (endsAt[position] is not { } component || draws[position] is false)
                continue;
            gathered.AddRange(component);
            endsAt[position] = null;
        }

        return gathered;
    }
}
