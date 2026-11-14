using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed record ReadyProposal(MatchProposal Proposal, MatchKey Block);

// A row belongs to one block. Evaluate rewrites only blocks that contained a removed row.
internal sealed record GroupIndex(
    string PassId,
    IReadOnlyList<string> Block,
    IReadOnlyList<string> Conserve,
    Side Many,
    int MaxGroup,
    MatchWorkspace Workspace,
    ImmutableDictionary<MatchKey, ImmutableArray<int>> Blocks,
    ImmutableDictionary<int, MatchKey> BlockOf,
    ImmutableDictionary<MatchKey, ImmutableArray<Decision>> Decisions,
    ImmutableDictionary<string, ImmutableList<ReadyProposal>> Ready,
    ImmutableDictionary<MatchKey, ImmutableArray<string>> ReadyByBlock,
    ImmutableSortedSet<string> ReadyOrder) : PassIndex(PassId)
{
    public override ImmutableHashSet<int> WorkspaceFree => Workspace.Free;

    public override IReadOnlyList<MatchProposal> ReadyProposals =>
        [.. ReadyOrder.SelectMany(key => Ready[key].Select(ready => ready.Proposal))];

    // The first proposal under the ordinal-smallest key commits next. Further proposals on that key wait.
    public override MatchProposal? NextReady() =>
        ReadyOrder.IsEmpty || ReadyOrder.Min is not { } key ? null : Ready[key][0].Proposal;

    public static (GroupIndex Index, MatchCounters Counters) Create(
        string passId,
        IReadOnlyList<string> block,
        IReadOnlyList<string> conserve,
        Side many,
        int maxGroup,
        MatchWorkspace workspace,
        MatchCounters counters)
    {
        var placed = Placed(workspace, block);
        var blocks = placed.GroupBy(row => row.Key, MatchKey.OrdinalEquality)
            .ToImmutableDictionary(group => group.Key, group => group.Select(row => row.Index).ToImmutableArray(), MatchKey.OrdinalEquality);
        var blockOf = placed.ToImmutableDictionary(row => row.Index, row => row.Key);
        var seeded = new GroupIndex(
            passId,
            block,
            conserve,
            many,
            maxGroup,
            workspace,
            blocks,
            blockOf,
            ImmutableDictionary.Create<MatchKey, ImmutableArray<Decision>>(MatchKey.OrdinalEquality),
            ImmutableDictionary.Create<string, ImmutableList<ReadyProposal>>(StringComparer.Ordinal),
            ImmutableDictionary.Create<MatchKey, ImmutableArray<string>>(MatchKey.OrdinalEquality),
            ImmutableSortedSet.Create<string>(StringComparer.Ordinal));
        return EvaluateAll(seeded, seeded.Blocks.Keys.OrderBy(key => key, MatchKey.Ordinal), counters.Built(), false);
    }

    public override (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters)
    {
        var dirty = taken.Where(BlockOf.ContainsKey).Select(index => BlockOf[index]).Distinct(MatchKey.OrdinalEquality);
        var removed = taken.Aggregate(this, (state, index) => state.Without(index));
        return EvaluateAll(removed, dirty, counters, true);
    }

    private static (GroupIndex Index, MatchCounters Counters) EvaluateAll(GroupIndex index, IEnumerable<MatchKey> keys, MatchCounters counters, bool dirty)
    {
        var current = index;
        foreach (var key in keys)
        {
            index.Workspace.Cancellation.ThrowIfCancellationRequested();
            var evaluated = current.Evaluate(key, counters, dirty);
            current = evaluated.Index;
            counters = evaluated.Counters;
        }

        return (current, counters);
    }

    public override IReadOnlyList<ResidueCluster> Residue() =>
        [.. Decisions.SelectMany(pair => pair.Value.SelectMany(decision => FromDecision(pair.Key, decision)))];

    private (GroupIndex Index, MatchCounters Counters) Evaluate(MatchKey key, MatchCounters counters, bool dirty)
    {
        var dropped = Drop(key);
        if (dropped.Blocks.TryGetValue(key, out var members) is false || members.IsEmpty)
            return (dropped with { Decisions = dropped.Decisions.Remove(key), Blocks = dropped.Blocks.Remove(key) }, counters);

        var items = members.OrderBy(index => index).Select(index => Workspace.Items[index]).ToArray();
        var components = Couple(items);
        return PostComponents(dropped, key, components, counters, dirty);
    }

    private (GroupIndex Index, MatchCounters Counters) PostComponents(
        GroupIndex dropped,
        MatchKey key,
        IEnumerable<Component> components,
        MatchCounters counters,
        bool dirty)
    {
        var index = dropped with { Decisions = dropped.Decisions.SetItem(key, []) };
        foreach (var component in components)
        {
            var decided = index.Decide(component, counters.SawComponent(), dirty);
            var stored = index with { Decisions = index.Decisions.SetItem(key, index.Decisions[key].Add(decided.Decision)) };
            index = decided.Decision is { Kind: DecisionKind.Commit }
                ? decided.Decision.Commits.Aggregate(stored, (current, proposal) => current.Add(key, proposal))
                : stored;
            counters = decided.Counters;
        }

        return (index, counters);
    }

    private GroupIndex Without(int index) =>
        BlockOf.TryGetValue(index, out var key) is false
            ? this
            : this with { Blocks = Blocks.SetItem(key, Blocks[key].Remove(index)), BlockOf = BlockOf.Remove(index) };

    private GroupIndex Drop(MatchKey blockKey) =>
        ReadyByBlock.TryGetValue(blockKey, out var keys) is false
            ? this
            : keys.Aggregate(
                this with { ReadyByBlock = ReadyByBlock.Remove(blockKey) },
                (state, key) => state.RemoveReady(blockKey, key));

    private GroupIndex RemoveReady(MatchKey blockKey, string key)
    {
        if (Ready.TryGetValue(key, out var proposals) is false)
            return this;
        var kept = proposals.RemoveAll(ready => ready.Block == blockKey);
        return kept.IsEmpty
            ? this with { Ready = Ready.Remove(key), ReadyOrder = ReadyOrder.Remove(key) }
            : this with { Ready = Ready.SetItem(key, kept) };
    }

    private GroupIndex Add(MatchKey blockKey, MatchProposal proposal)
    {
        var keys = ReadyByBlock.TryGetValue(blockKey, out var existing) ? existing.Add(proposal.Key) : [proposal.Key];
        return Ready.TryGetValue(proposal.Key, out var list)
            ? this with
            {
                Ready = Ready.SetItem(proposal.Key, list.Add(new ReadyProposal(proposal, blockKey))),
                ReadyByBlock = ReadyByBlock.SetItem(blockKey, keys),
            }
            : this with
            {
                Ready = Ready.SetItem(proposal.Key, [new ReadyProposal(proposal, blockKey)]),
                ReadyByBlock = ReadyByBlock.SetItem(blockKey, keys),
                ReadyOrder = ReadyOrder.Add(proposal.Key),
            };
    }

    // The block limits count targets and eligible rows, and they are applied before any search.
    private (Decision Decision, MatchCounters Counters) Decide(Component component, MatchCounters counters, bool dirty) =>
        component.Targets.Length > Workspace.Limits.MaxTargetsPerComponent || component.Eligible.Length > Workspace.Limits.MaxBlockItems
            ? (Decision.Blocked([.. component.Targets, .. component.Eligible]), counters)
            : Outcome(component, SearchTargets(component, counters, dirty));

    private (ImmutableArray<(int Target, TargetSearch Search)> Rows, MatchCounters Counters) SearchTargets(Component component, MatchCounters counters, bool dirty)
    {
        var rows = new (int Target, TargetSearch Search)[component.Targets.Length];
        for (var index = 0; index < rows.Length; index++)
        {
            var target = component.Targets[index];
            var searched = Search(target, component, counters, dirty);
            rows[index] = (target, searched.Result);
            counters = searched.Counters;
        }

        return (ImmutableCollectionsMarshal.AsImmutableArray(rows), counters);
    }

    // Two targets with unique subsets still contest when they draw the same row.
    private (Decision Decision, MatchCounters Counters) Outcome(Component component, (ImmutableArray<(int Target, TargetSearch Search)> Rows, MatchCounters Counters) searched)
    {
        var searches = searched.Rows;
        var overlap = searches.Where(pair => pair.Search.State is SearchState.Unique).Select(pair => pair.Search.DrawnItems).ToArray();
        var shared = overlap.SelectMany(items => items).GroupBy(item => item).Any(group => group.Count() > 1);
        var contest = searches.Any(pair => pair.Search.State is SearchState.Contest) || shared;
        var incomplete = searches.Any(pair => pair.Search.State is SearchState.Incomplete);
        var decision = (contest, incomplete) switch
        {
            (false, false) => Decision.Ready([.. searches.Where(pair => pair.Search.State is SearchState.Unique).Select(pair => ProposalFor(pair.Target, pair.Search.DrawnItems))]),
            (true, true) => Decision.Mixed(
                [.. searches.Where(pair => pair.Search.State is SearchState.Contest or SearchState.Unique).SelectMany(pair => new[] { pair.Target }.Concat(pair.Search.DrawnItems)), .. searches.Where(pair => pair.Search.State is SearchState.Incomplete).SelectMany(pair => pair.Search.DrawnItems)],
                [.. searches.Where(pair => pair.Search.State is SearchState.Incomplete).SelectMany(pair => EligibleFor(pair.Target, component).Except(pair.Search.DrawnItems))]),
            (true, false) => Decision.Contested([.. searches.Where(pair => pair.Search.State is not SearchState.None).SelectMany(pair => new[] { pair.Target }.Concat(pair.Search.DrawnItems))]),
            _ => Decision.Exhausted([.. component.Targets, .. component.Eligible]),
        };
        return (decision, searched.Counters);
    }

    // The proposal key is the target row's key, so ready order follows that key.
    private MatchProposal ProposalFor(int target, int[] drawn) =>
        Many is Side.Left
            ? new MatchProposal(drawn, [target], Workspace.Items[target].Row.Context.RowKey)
            : new MatchProposal([target], drawn, Workspace.Items[target].Row.Context.RowKey);

    private (TargetSearch Result, MatchCounters Counters) Search(int target, Component component, MatchCounters counters, bool dirty) =>
        Conserved(Workspace.Items[target].Row.Context) is not { } values
            ? (new TargetSearch(SearchState.None, []), counters)
            : (
                SubsetSearch.Search(Classes(target, component), values, MaxGroup, Workspace.Limits.MaxNodesPerTarget, Workspace.Cancellation),
                counters.SawSearch(dirty));

    // Rows share a class when invariant formatting matches, scale included. 1.0 and 1.00 are different classes.
    private ConservedClass[] Classes(int target, Component component) =>
        [.. EligibleFor(target, component)
            .Select(index => (Index: index, Values: Conserved(Workspace.Items[index].Row.Context)))
            .Where(pair => pair.Values is not null)
            .GroupBy(pair => Delimited.Join('\u001f', pair.Values!.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .Select(group => new ConservedClass(group.First().Values!, [.. group.Select(pair => pair.Index)]))
            .OrderByDescending(cls => cls.Values[0])
            .ThenBy(cls => Delimited.Join('\u001f', cls.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)];

    private decimal[]? Conserved(RowContext row)
    {
        var numbers = Conserve.Select(field => row.IsNull(field) ? (decimal?)null : row.Values[field].Number).ToArray();
        return numbers.Any(number => number is null) ? null : [.. numbers.Select(number => number.GetValueOrDefault())];
    }

    private IEnumerable<Component> Couple(IReadOnlyList<ActiveItem> blockItems)
    {
        Workspace.Cancellation.ThrowIfCancellationRequested();
        var targets = blockItems.Where(item => item.Side != Many).Select(item => item.Index).OrderBy(index => index).ToArray();
        var manyItems = blockItems.Where(item => item.Side == Many).Select(item => item.Index).ToArray();
        var census = Census.Collect(manyItems, ClaimOf, Workspace.Cancellation);
        return ComponentPartition.Partition(targets, ClaimOf, census.Wildcards.Length > 0, census.Many.Length > 0, census.Has, Workspace.Cancellation)
            .Select(group => Finish(group, census));
    }

    private MatchKeys.SharedClaim ClaimOf(int index) =>
        MatchKeys.Claim(Workspace.Items[index].Row.Context, Workspace.SharedField, Workspace.Schema);

    private Component Finish(int[] targets, Census census)
    {
        var eligible = Union(targets, census);
        var blocked = targets.Length > Workspace.Limits.MaxTargetsPerComponent || eligible.Length > Workspace.Limits.MaxBlockItems;
        return new Component(targets, eligible, blocked ? ImmutableDictionary<int, int[]>.Empty : ListsFor(targets, census));
    }

    private ImmutableDictionary<int, int[]> ListsFor(int[] targets, Census census)
    {
        var shared = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var rows = ImmutableDictionary.CreateBuilder<int, int[]>();
        foreach (var target in targets)
            rows[target] = ForClaim(ClaimOf(target));
        return rows.ToImmutable();

        int[] ForClaim(MatchKeys.SharedClaim claim) => claim switch
        {
            { Wildcard: true } => census.Many,
            { Value: { } value } => Shared(value),
            _ => [],
        };

        int[] Shared(string value)
        {
            if (shared.TryGetValue(value, out var existing))
                return existing;
            var created = census.For(value);
            shared[value] = created;
            return created;
        }
    }

    private int[] Union(int[] targets, Census census)
    {
        var seen = new HashSet<int>();
        var result = new List<int>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var covered = false;
        foreach (var target in targets)
        {
            Workspace.Cancellation.ThrowIfCancellationRequested();
            var claim = ClaimOf(target);
            if (claim.Wildcard)
            {
                if (covered)
                    continue;
                foreach (var item in census.Many)
                    if (seen.Add(item))
                        result.Add(item);
                covered = true;
                continue;
            }

            if (covered || claim.Value is not { } value || emitted.Add(value) is false)
                continue;
            foreach (var item in census.For(value))
                if (seen.Add(item))
                    result.Add(item);
        }

        return [.. result];
    }

    private sealed record Census(int[] Many, int[] Wildcards, ImmutableDictionary<string, int[]> Buckets)
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

    private static int[] EligibleFor(int target, Component component) => component.ByTarget[target];

    private IEnumerable<ResidueCluster> FromDecision(MatchKey key, Decision decision) => decision.Kind switch
    {
        DecisionKind.Contest => [new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows(decision.Contest), key.Encoded)],
        DecisionKind.Incomplete => [new ResidueCluster(Alignment.SearchIncomplete, decision.Code, PassId, Rows(decision.Incomplete))],
        DecisionKind.Split =>
        [
            new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows(decision.Contest), key.Encoded),
            new ResidueCluster(Alignment.SearchIncomplete, decision.Code, PassId, Rows(decision.Incomplete)),
        ],
        _ => [],
    };

    private IReadOnlyList<ConsolidatedRow> Rows(IEnumerable<int> indexes) =>
        [.. indexes.Distinct().Select(index => Workspace.Items[index].Row)];

    // A null key part is not placed. The row stays free for a later pass.
    private static IReadOnlyList<BlockedRow> Placed(MatchWorkspace workspace, IReadOnlyList<string> block) =>
        [.. workspace.Free
            .Select(index => MatchKeys.Compose(workspace.Items[index], block, workspace.Schema) is { } key ? new BlockedRow(index, key) : null)
            .OfType<BlockedRow>()];

    private sealed record BlockedRow(int Index, MatchKey Key);

    private sealed record Component(int[] Targets, int[] Eligible, ImmutableDictionary<int, int[]> ByTarget);
}
