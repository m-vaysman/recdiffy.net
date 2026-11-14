namespace RecDiffy;

internal static class ComponentPartition
{
    internal static int[][] Partition(
        int[] targets,
        Func<int, MatchKeys.SharedClaim> claimOf,
        bool manySideWildcard,
        bool manySideHasRows,
        Func<string, bool> censusHas,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(claimOf);
        ArgumentNullException.ThrowIfNull(censusHas);

        var claims = Read(targets, claimOf, cancellation);
        return manySideWildcard ? One(targets) : Groups(targets, claims, manySideHasRows, censusHas);
    }

    private static MatchKeys.SharedClaim[] Read(int[] targets, Func<int, MatchKeys.SharedClaim> claimOf, CancellationToken cancellation)
    {
        var claims = new MatchKeys.SharedClaim[targets.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            claims[index] = claimOf(targets[index]);
        }

        return claims;
    }

    private static int[][] One(int[] targets) => targets.Length is 0 ? [] : [[.. targets]];

    private static int[][] Groups(int[] targets, MatchKeys.SharedClaim[] claims, bool manySideHasRows, Func<string, bool> censusHas)
    {
        var firstWildcard = -1;
        for (var index = 0; index < claims.Length; index++)
        {
            if (claims[index].Wildcard)
            {
                firstWildcard = index;
                break;
            }
        }

        return firstWildcard >= 0 && manySideHasRows
            ? Glue(targets, claims, firstWildcard, censusHas)
            : ByValue(targets, claims, censusHas);
    }

    private static int[][] ByValue(int[] targets, MatchKeys.SharedClaim[] claims, Func<string, bool> censusHas)
    {
        var grouped = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var lastAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var slots = new List<int>?[targets.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            var claim = claims[index];
            if (claim is { Wildcard: false, Value: { } value } && censusHas(value))
            {
                if (grouped.TryGetValue(value, out var list) is false)
                {
                    list = [];
                    grouped[value] = list;
                }
                else
                    slots[lastAt[value]] = null;

                list.Add(targets[index]);
                lastAt[value] = index;
                slots[index] = list;
            }
            else
                slots[index] = [targets[index]];
        }

        return Emit(slots);
    }

    private static int[][] Glue(int[] targets, MatchKeys.SharedClaim[] claims, int firstWildcard, Func<string, bool> censusHas)
    {
        var grouped = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var lastAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var prefix = new List<int>?[firstWildcard];
        var slots = new List<int>?[targets.Length];
        for (var index = 0; index < firstWildcard; index++)
        {
            var claim = claims[index];
            if (claim is { Wildcard: false, Value: { } value } && censusHas(value))
            {
                if (grouped.TryGetValue(value, out var list) is false)
                {
                    list = [];
                    grouped[value] = list;
                }
                else
                    prefix[lastAt[value]] = null;

                list.Add(targets[index]);
                lastAt[value] = index;
                prefix[index] = list;
            }
            else
                slots[index] = [targets[index]];
        }

        var glue = new List<int>();
        for (var index = 0; index < firstWildcard; index++)
        {
            if (prefix[index] is { } list)
                glue.AddRange(list);
        }

        var lastGlue = firstWildcard;
        for (var index = firstWildcard; index < targets.Length; index++)
        {
            if (Joins(claims[index], censusHas))
            {
                glue.Add(targets[index]);
                lastGlue = index;
            }
            else
                slots[index] = [targets[index]];
        }

        slots[lastGlue] = glue;
        return Emit(slots);
    }

    private static bool Joins(MatchKeys.SharedClaim claim, Func<string, bool> censusHas) =>
        claim.Wildcard || (claim.Value is { } value && censusHas(value));

    private static int[][] Emit(List<int>?[] slots)
    {
        var count = 0;
        foreach (var slot in slots)
            if (slot is not null)
                count++;

        var groups = new int[count][];
        var written = 0;
        foreach (var slot in slots)
        {
            if (slot is { } list)
                groups[written++] = [.. list];
        }

        return groups;
    }
}
