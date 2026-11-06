using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RecDiffy.Results;

namespace RecDiffy;

internal static class CascadeMatcher
{
    public static MatchReport Run(
        IReadOnlyList<ConsolidatedRow> left,
        IReadOnlyList<ConsolidatedRow> right,
        IReadOnlyList<PassDeclaration> passes,
        IReadOnlyList<FieldDeclaration> fields,
        SearchLimits limits,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(limits);
        var items = Items(left, right);
        var schema = fields.ToFrozenDictionary(field => field.Name, field => field, StringComparer.Ordinal);
        // TradeRef is the veto column when the schema declares it. Shared-ref passes do not apply it; the other passes do.
        var shared = schema.ContainsKey("TradeRef") ? "TradeRef" : null;
        var engines = passes.Select(pass => Create(pass, limits)).ToArray();
        var rowIndex = items.ToFrozenDictionary(item => item.Row, item => item.Index, RowIdentity.Instance);
        var initialFree = items.Select(item => item.Index).ToImmutableHashSet();
        // Free on the workspace is the pre-commit snapshot. Later commits drop rows through Release, not through this set.
        var workspace = new MatchWorkspace(passes.Count is 0 ? "" : passes[0].Id, items, initialFree, schema, limits, shared, limits.MaxGroupSize, cancellation);
        var built = BuildPasses(engines, workspace);
        // One commit per turn. A recursive walk of one group per row overflows at the 100k-row bench.
        var committed = Commit(built.Indexes, built.Counters, initialFree, items, cancellation);
        var residue = FoldResidue(committed.Indexes, rowIndex, cancellation);
        var unmatched = items
            .Where(item => committed.Free.Contains(item.Index) && residue.Claimed.Contains(item.Index) is false)
            .Select(item => new ResidueCluster(item.Side is Side.Left ? Alignment.LeftOnly : Alignment.RightOnly, null, null, [item.Row]));
        return new MatchReport(committed.Matches, [.. residue.Clusters, .. unmatched], committed.Counters);
    }

    // Run keeps the index Release returns. Propose and Residue on a pass rebuild from the original free set for IMatchPass; that copy is not the commit path.
    private static (ImmutableList<PassIndex> Indexes, MatchCounters Counters) BuildPasses(CascadePass[] engines, MatchWorkspace workspace)
    {
        var indexes = ImmutableList<PassIndex>.Empty;
        var counters = new MatchCounters();
        foreach (var engine in engines)
        {
            var built = engine.Build(workspace, counters);
            indexes = indexes.Add(built.Index);
            counters = built.Counters;
        }

        return (indexes, counters);
    }

    private static (ImmutableList<CommittedMatch> Matches, ImmutableList<PassIndex> Indexes, MatchCounters Counters, ImmutableHashSet<int> Free) Commit(
        ImmutableList<PassIndex> indexes,
        MatchCounters counters,
        ImmutableHashSet<int> free,
        IReadOnlyList<ActiveItem> items,
        CancellationToken cancellation)
    {
        var matches = ImmutableList<CommittedMatch>.Empty;
        while (Next(indexes) is { } chosen)
        {
            cancellation.ThrowIfCancellationRequested();
            var taken = (IReadOnlyList<int>)[.. chosen.Proposal.Left, .. chosen.Proposal.Right];
            var released = ReleaseAll(indexes, taken, counters);
            matches = matches.Add(Commit(chosen.Index.PassId, chosen.Proposal, items));
            indexes = released.Indexes;
            counters = released.Counters;
            free = free.Except(taken);
        }

        return (matches, indexes, counters, free);
    }

    private static (ImmutableList<PassIndex> Indexes, MatchCounters Counters) ReleaseAll(
        ImmutableList<PassIndex> indexes,
        IReadOnlyList<int> taken,
        MatchCounters counters)
    {
        var next = ImmutableList<PassIndex>.Empty;
        foreach (var index in indexes)
        {
            var released = index.Release(taken, counters);
            next = next.Add(released.Index);
            counters = released.Counters;
        }

        return (next, counters);
    }

    private static (ImmutableHashSet<int> Claimed, ImmutableList<ResidueCluster> Clusters) FoldResidue(
        ImmutableList<PassIndex> indexes,
        FrozenDictionary<ConsolidatedRow, int> rowIndex,
        CancellationToken cancellation)
    {
        var claimed = ImmutableHashSet<int>.Empty;
        var clusters = ImmutableList<ResidueCluster>.Empty;
        foreach (var index in indexes)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var cluster in index.Residue())
            {
                var rows = cluster.Rows.Where(row => claimed.Contains(rowIndex[row]) is false).ToArray();
                if (rows.Length is 0)
                    continue;
                claimed = claimed.Union(rows.Select(row => rowIndex[row]));
                clusters = clusters.Add(cluster with { Rows = rows });
            }
        }

        return (claimed, clusters);
    }

    private static (PassIndex Index, MatchProposal Proposal)? Next(ImmutableList<PassIndex> indexes) =>
        indexes.Select(index => (Index: index, Proposal: index.NextReady())).FirstOrDefault(pair => pair.Proposal is not null) is { Proposal: { } proposal } pair
            ? (pair.Index, proposal)
            : null;

    private static ActiveItem[] Items(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right) =>
        [.. left.Concat(right).Where(row => row is { Quarantined: false, Suppression: null } && row.Context.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Invalid)).Select((row, index) => new ActiveItem(index, row.Context.Side, row))];

    private static CascadePass Create(PassDeclaration pass, SearchLimits limits) => pass.Pass switch
    {
        "match.sharedRef" => new PairPass(pass.Id, pass.Key.Count is 0 ? ["Counterparty", "TradeRef"] : pass.Key, false, limits.MaxGroupSize),
        "match.naturalKey" or "match.reducedKey" => new PairPass(pass.Id, pass.Key, true, pass.MaxGroupSize ?? limits.MaxGroupSize),
        "match.groupSum" => new GroupPass(
            pass.Id,
            pass.Block,
            pass.Conserve,
            pass.Many is "right" ? Side.Right : Side.Left,
            pass.MaxGroupSize ?? limits.MaxGroupSize),
        var other => throw new InvalidOperationException($"Pass '{other}' is not a cascade pass."),
    };

    private static CommittedMatch Commit(string passId, MatchProposal proposal, IReadOnlyList<ActiveItem> items) =>
        new(passId, proposal.Key, Rows(items, proposal.Left), Rows(items, proposal.Right));

    private static IReadOnlyList<ConsolidatedRow> Rows(IReadOnlyList<ActiveItem> items, IReadOnlyList<int> indexes) =>
        [.. indexes.Select(index => items[index].Row)];

    // ConsolidatedRow is a record. Value equality would treat distinct rows as the same key.
    private sealed class RowIdentity : IEqualityComparer<ConsolidatedRow>
    {
        public static RowIdentity Instance { get; } = new();

        public bool Equals(ConsolidatedRow? left, ConsolidatedRow? right) => ReferenceEquals(left, right);

        public int GetHashCode(ConsolidatedRow row) => RuntimeHelpers.GetHashCode(row);
    }
}
