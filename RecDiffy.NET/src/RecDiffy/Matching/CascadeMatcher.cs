using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed record MatchWorkspace(
    string PassId,
    IReadOnlyList<ActiveItem> Items,
    ImmutableHashSet<int> Free,
    IReadOnlyDictionary<string, FieldDeclaration> Schema,
    SearchLimits Limits,
    string? SharedField,
    int MaxGroupSize,
    CancellationToken Cancellation);

internal sealed record ActiveItem(int Index, Side Side, ConsolidatedRow Row);

internal sealed record CommittedMatch(string PassId, string Key, IReadOnlyList<ConsolidatedRow> Left, IReadOnlyList<ConsolidatedRow> Right);

internal sealed record ResidueCluster(Alignment Alignment, string? Code, string? PassId, IReadOnlyList<ConsolidatedRow> Rows);

internal sealed record MatchReport(IReadOnlyList<CommittedMatch> Committed, IReadOnlyList<ResidueCluster> Residue, MatchCounters Counters);

internal sealed record MatchCounters(int IndexBuilds = 0, int SubsetSearches = 0, int DirtyResearches = 0, int Components = 0)
{
    public MatchCounters Built() => this with { IndexBuilds = IndexBuilds + 1 };

    public MatchCounters SawComponent() => this with { Components = Components + 1 };

    public MatchCounters SawSearch(bool dirty) => this with
    {
        SubsetSearches = SubsetSearches + 1,
        DirtyResearches = DirtyResearches + (dirty ? 1 : 0),
    };
}

internal static class MatchKeys
{
    public static string? Compose(ActiveItem item, IReadOnlyList<string> fields, IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        var parts = fields.Select(field => Text(item.Row.Context, schema[field])).ToArray();
        return parts.Any(part => part is null) ? null : string.Join('\u001f', parts.Select(part => Escape(part!)));
    }

    // A value may contain the unit separator. Escape it, and escape backslash first, so the join stays one-to-one.
    private static string Escape(string part) =>
        part.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\u001f", "\\\u001f", StringComparison.Ordinal);

    public static string? Text(RowContext row, FieldDeclaration field) =>
        row.IsNull(field.Name)
            ? null
            : field.Type switch
            {
                FieldType.Decimal => row.Values[field.Name].Number.ToString("F" + field.Scale, CultureInfo.InvariantCulture),
                FieldType.Date => row.Values[field.Name].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FieldType.Symbol => row.Pool.Symbol(row.Values[field.Name].Interned),
                FieldType.Identifier => row.Pool.Identifier(row.Values[field.Name].Interned).Value,
                FieldType.Text => row.Values[field.Name].Text,
                _ => null,
            };

    public static bool Vetoed(ActiveItem left, ActiveItem right, string? sharedField, IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        sharedField is not null
        && schema.TryGetValue(sharedField, out var field)
        && Identity(left.Row.Context, field) is ({ } leftScheme, { } leftValue)
        && Identity(right.Row.Context, field) is ({ } rightScheme, { } rightValue)
        && IsShared(leftScheme)
        && IsShared(rightScheme)
        && leftValue != rightValue;

    private static (string? Scheme, string? Value) Identity(RowContext row, FieldDeclaration field) =>
        row.IsNull(field.Name)
            ? (null, null)
            : field.Type switch
            {
                FieldType.Identifier => (row.Pool.Identifier(row.Values[field.Name].Interned).Scheme, row.Pool.Identifier(row.Values[field.Name].Interned).Value),
                FieldType.Symbol => ("shared", row.Pool.Symbol(row.Values[field.Name].Interned)),
                _ => ("shared", Text(row, field)),
            };

    private static bool IsShared(string scheme) => scheme is not "broker";
}

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

internal abstract record PassIndex(string PassId)
{
    public abstract MatchProposal? NextReady();

    public abstract IReadOnlyList<MatchProposal> ReadyProposals { get; }

    public abstract (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters);

    public abstract IReadOnlyList<ResidueCluster> Residue();

    public abstract ImmutableHashSet<int> WorkspaceFree { get; }
}

internal abstract class CascadePass : IMatchPass
{
    public abstract string PassId { get; }

    public abstract PassCardinality Cardinality { get; }

    public abstract int MaxGroup { get; }

    public abstract void Propose(in PassContext context, ProposalSink sink);

    public abstract IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace);

    public abstract (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters);

    public MatchProposal? ProposeFirst(MatchWorkspace workspace)
    {
        var sink = new ProposalSink();
        Propose(new PassContext(workspace), sink);
        return sink.Proposals
            .Where(proposal => proposal.Left.All(workspace.Free.Contains) && proposal.Right.All(workspace.Free.Contains))
            .OrderBy(proposal => proposal.Key, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    protected static void Publish(ProposalSink sink, IEnumerable<MatchProposal> proposals)
    {
        foreach (var proposal in proposals)
            sink.Propose(proposal.Left, proposal.Right, proposal.Key);
    }
}

internal sealed class PairPass(string passId, IReadOnlyList<string> fields, bool veto, int maxGroup) : CascadePass
{
    public override string PassId => passId;

    public override PassCardinality Cardinality => PassCardinality.OneToOne;

    public override int MaxGroup => maxGroup;

    public override (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters) =>
        PairIndex.Create(passId, fields, veto, workspace, counters);

    public override void Propose(in PassContext context, ProposalSink sink) =>
        Publish(sink, Build(context.Workspace, new MatchCounters()).Index.ReadyProposals);

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        Build(workspace, new MatchCounters()).Index.Residue();
}

// A row belongs to one key. Release rewrites only keys that contained a removed row.
internal sealed record PairIndex(
    string PassId,
    IReadOnlyList<string> Fields,
    bool ApplyVeto,
    MatchWorkspace Workspace,
    ImmutableDictionary<string, ImmutableArray<int>> Left,
    ImmutableDictionary<string, ImmutableArray<int>> Right,
    ImmutableDictionary<int, string> KeyOf,
    ImmutableDictionary<string, MatchProposal> Ready,
    ImmutableSortedSet<string> ReadyOrder) : PassIndex(PassId)
{
    public override ImmutableHashSet<int> WorkspaceFree => Workspace.Free;

    public override IReadOnlyList<MatchProposal> ReadyProposals => [.. ReadyOrder.Select(key => Ready[key])];

    public override MatchProposal? NextReady() =>
        ReadyOrder.IsEmpty || ReadyOrder.Min is not { } key ? null : Ready[key];

    public static (PairIndex Index, MatchCounters Counters) Create(
        string passId,
        IReadOnlyList<string> fields,
        bool veto,
        MatchWorkspace workspace,
        MatchCounters counters)
    {
        var placed = Placed(workspace, fields);
        var keyed = placed.ToImmutableDictionary(row => row.Index, row => row.Key);
        var seeded = new PairIndex(
            passId,
            fields,
            veto,
            workspace,
            SideMap(placed, Side.Left),
            SideMap(placed, Side.Right),
            keyed,
            ImmutableDictionary.Create<string, MatchProposal>(StringComparer.Ordinal),
            ImmutableSortedSet.Create<string>(StringComparer.Ordinal));
        var ready = RefreshAll(seeded, seeded.Left.Keys.Union(seeded.Right.Keys, StringComparer.Ordinal));
        return (ready, counters.Built());
    }

    public override (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters)
    {
        var dirty = taken.Where(KeyOf.ContainsKey).Select(index => KeyOf[index]).Distinct(StringComparer.Ordinal);
        var removed = taken.Aggregate(this, (state, index) => state.Without(index));
        return (RefreshAll(removed, dirty), counters);
    }

    private static PairIndex RefreshAll(PairIndex index, IEnumerable<string> keys)
    {
        var current = index;
        foreach (var key in keys)
            current = current.Refresh(key);
        return current;
    }

    public override IReadOnlyList<ResidueCluster> Residue() =>
        [.. Left.Keys.Union(Right.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).SelectMany(Cluster)];

    private IEnumerable<ResidueCluster> Cluster(string key) =>
        Sides(key) is { Left: { Length: > 0 } left, Right: { Length: > 0 } right } && (left.Length is not 1 || right.Length is not 1)
            ? [new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows([.. left, .. right]))]
            : [];

    private PairIndex Without(int index) =>
        KeyOf.TryGetValue(index, out var key) is false
            ? this
            : Workspace.Items[index].Side switch
            {
                Side.Left => this with { Left = Left.SetItem(key, Left[key].Remove(index)), KeyOf = KeyOf.Remove(index) },
                _ => this with { Right = Right.SetItem(key, Right[key].Remove(index)), KeyOf = KeyOf.Remove(index) },
            };

    private PairIndex Refresh(string key)
    {
        var cleared = this with { Ready = Ready.Remove(key), ReadyOrder = ReadyOrder.Remove(key) };
        var (left, right) = cleared.Sides(key);
        return left.Length is 1 && right.Length is 1
            ? cleared with
            {
                Ready = cleared.Ready.SetItem(key, new MatchProposal([left[0]], [right[0]], key)),
                ReadyOrder = cleared.ReadyOrder.Add(key),
            }
            : cleared;
    }

    private (int[] Left, int[] Right) Sides(string key)
    {
        var left = Members(Left, key);
        var right = Members(Right, key);
        return (
            [.. left.Where(index => right.Any(other => ApplyVeto is false || Veto(index, other) is false)).OrderBy(index => index)],
            [.. right.Where(index => left.Any(other => ApplyVeto is false || Veto(other, index) is false)).OrderBy(index => index)]);
    }

    private bool Veto(int left, int right) =>
        MatchKeys.Vetoed(Workspace.Items[left], Workspace.Items[right], Workspace.SharedField, Workspace.Schema);

    private IReadOnlyList<ConsolidatedRow> Rows(IReadOnlyList<int> indexes) =>
        [.. indexes.Select(index => Workspace.Items[index].Row)];

    private static ImmutableArray<int> Members(ImmutableDictionary<string, ImmutableArray<int>> side, string key) =>
        side.TryGetValue(key, out var members) ? members : [];

    private static ImmutableDictionary<string, ImmutableArray<int>> SideMap(IReadOnlyList<PlacedRow> rows, Side side) =>
        rows.Where(row => row.Side == side)
            .GroupBy(row => row.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Select(row => row.Index).ToImmutableArray(), StringComparer.Ordinal);

    private static IReadOnlyList<PlacedRow> Placed(MatchWorkspace workspace, IReadOnlyList<string> fields) =>
        [.. workspace.Free
            .Select(index => MatchKeys.Compose(workspace.Items[index], fields, workspace.Schema) is { } key
                ? new PlacedRow(index, workspace.Items[index].Side, key)
                : null)
            .OfType<PlacedRow>()];

    private sealed record PlacedRow(int Index, Side Side, string Key);
}

internal sealed class GroupPass(string passId, IReadOnlyList<string> block, IReadOnlyList<string> conserve, Side many, int maxGroup) : CascadePass
{
    public override string PassId => passId;

    public override PassCardinality Cardinality => many == Side.Left ? PassCardinality.ManyToOne : PassCardinality.OneToMany;

    public override int MaxGroup => maxGroup;

    public override (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters) =>
        GroupIndex.Create(passId, block, conserve, many, maxGroup, workspace, counters);

    public override void Propose(in PassContext context, ProposalSink sink) =>
        Publish(sink, Build(context.Workspace, new MatchCounters()).Index.ReadyProposals);

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        Build(workspace, new MatchCounters()).Index.Residue();
}

internal sealed record ReadyProposal(MatchProposal Proposal, string Block);

// A row belongs to one block. Evaluate rewrites only blocks that contained a removed row.
internal sealed record GroupIndex(
    string PassId,
    IReadOnlyList<string> Block,
    IReadOnlyList<string> Conserve,
    Side Many,
    int MaxGroup,
    MatchWorkspace Workspace,
    ImmutableDictionary<string, ImmutableArray<int>> Blocks,
    ImmutableDictionary<int, string> BlockOf,
    ImmutableDictionary<string, ImmutableArray<Decision>> Decisions,
    ImmutableDictionary<string, ImmutableList<ReadyProposal>> Ready,
    ImmutableSortedSet<string> ReadyOrder) : PassIndex(PassId)
{
    public override ImmutableHashSet<int> WorkspaceFree => Workspace.Free;

    public override IReadOnlyList<MatchProposal> ReadyProposals =>
        [.. ReadyOrder.SelectMany(key => Ready[key].Select(ready => ready.Proposal))];

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
        var blocks = placed.GroupBy(row => row.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Select(row => row.Index).ToImmutableArray(), StringComparer.Ordinal);
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
            ImmutableDictionary.Create<string, ImmutableArray<Decision>>(StringComparer.Ordinal),
            ImmutableDictionary.Create<string, ImmutableList<ReadyProposal>>(StringComparer.Ordinal),
            ImmutableSortedSet.Create<string>(StringComparer.Ordinal));
        return EvaluateAll(seeded, seeded.Blocks.Keys.OrderBy(key => key, StringComparer.Ordinal), counters.Built(), false);
    }

    public override (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters)
    {
        var dirty = taken.Where(BlockOf.ContainsKey).Select(index => BlockOf[index]).Distinct(StringComparer.Ordinal);
        var removed = taken.Aggregate(this, (state, index) => state.Without(index));
        return EvaluateAll(removed, dirty, counters, true);
    }

    private static (GroupIndex Index, MatchCounters Counters) EvaluateAll(GroupIndex index, IEnumerable<string> keys, MatchCounters counters, bool dirty)
    {
        var current = index;
        foreach (var key in keys)
        {
            var evaluated = current.Evaluate(key, counters, dirty);
            current = evaluated.Index;
            counters = evaluated.Counters;
        }

        return (current, counters);
    }

    public override IReadOnlyList<ResidueCluster> Residue() =>
        [.. Decisions.SelectMany(pair => pair.Value.SelectMany(decision => FromDecision(decision)))];

    private (GroupIndex Index, MatchCounters Counters) Evaluate(string key, MatchCounters counters, bool dirty)
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
        string key,
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

    private GroupIndex Drop(string blockKey) =>
        Ready.Keys.Aggregate(this, (state, key) =>
        {
            var kept = state.Ready[key].RemoveAll(ready => ready.Block == blockKey);
            return kept.IsEmpty
                ? state with { Ready = state.Ready.Remove(key), ReadyOrder = state.ReadyOrder.Remove(key) }
                : state with { Ready = state.Ready.SetItem(key, kept) };
        });

    private GroupIndex Add(string blockKey, MatchProposal proposal) =>
        Ready.TryGetValue(proposal.Key, out var list)
            ? this with { Ready = Ready.SetItem(proposal.Key, list.Add(new ReadyProposal(proposal, blockKey))) }
            : this with
            {
                Ready = Ready.SetItem(proposal.Key, [new ReadyProposal(proposal, blockKey)]),
                ReadyOrder = ReadyOrder.Add(proposal.Key),
            };

    private (Decision Decision, MatchCounters Counters) Decide(Component component, MatchCounters counters, bool dirty) =>
        component.Targets.Length > Workspace.Limits.MaxTargetsPerComponent || component.Eligible.Length > Workspace.Limits.MaxBlockItems
            ? (Decision.Blocked([.. component.Targets, .. component.Eligible]), counters)
            : Outcome(component, SearchTargets(component, counters, dirty));

    private (ImmutableArray<(int Target, TargetSearch Search)> Rows, MatchCounters Counters) SearchTargets(Component component, MatchCounters counters, bool dirty)
    {
        var rows = ImmutableArray<(int Target, TargetSearch Search)>.Empty;
        foreach (var target in component.Targets)
        {
            var searched = Search(target, component, counters, dirty);
            rows = rows.Add((target, searched.Result));
            counters = searched.Counters;
        }

        return (rows, counters);
    }

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

    private ConservedClass[] Classes(int target, Component component) =>
        [.. EligibleFor(target, component)
            .Select(index => (Index: index, Values: Conserved(Workspace.Items[index].Row.Context)))
            .Where(pair => pair.Values is not null)
            .GroupBy(pair => string.Join('\u001f', pair.Values!.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .Select(group => new ConservedClass(group.First().Values!, [.. group.Select(pair => pair.Index)]))
            .OrderByDescending(cls => cls.Values[0])
            .ThenBy(cls => string.Join('\u001f', cls.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)];

    private decimal[]? Conserved(RowContext row)
    {
        var numbers = Conserve.Select(field => row.IsNull(field) ? (decimal?)null : row.Values[field].Number).ToArray();
        return numbers.Any(number => number is null) ? null : [.. numbers.Select(number => number.GetValueOrDefault())];
    }

    private IEnumerable<Component> Couple(IReadOnlyList<ActiveItem> blockItems)
    {
        var targets = blockItems.Where(item => item.Side != Many).Select(item => item.Index).OrderBy(index => index).ToArray();
        var manyItems = blockItems.Where(item => item.Side == Many).Select(item => item.Index).ToArray();
        var eligible = targets.ToImmutableDictionary(target => target, target => manyItems.Where(item => Veto(target, item) is false).ToArray());
        return targets.Aggregate(
            ImmutableList<int[]>.Empty,
            (components, target) =>
            {
                var joined = components.Where(component => component.Any(other => eligible[target].Intersect(eligible[other]).Any())).ToArray();
                var kept = components.Where(component => joined.Contains(component) is false);
                var merged = joined.SelectMany(component => component).Append(target).Distinct().ToArray();
                return kept.Append(merged).ToImmutableList();
            }).Select(group => new Component(group, [.. group.SelectMany(target => eligible[target]).Distinct()], eligible));
    }

    private bool Veto(int target, int item) =>
        Workspace.Items[target].Side is Side.Left
            ? MatchKeys.Vetoed(Workspace.Items[target], Workspace.Items[item], Workspace.SharedField, Workspace.Schema)
            : MatchKeys.Vetoed(Workspace.Items[item], Workspace.Items[target], Workspace.SharedField, Workspace.Schema);

    private static int[] EligibleFor(int target, Component component) => component.ByTarget[target];

    private IEnumerable<ResidueCluster> FromDecision(Decision decision) => decision.Kind switch
    {
        DecisionKind.Contest => [new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows(decision.Contest))],
        DecisionKind.Incomplete => [new ResidueCluster(Alignment.SearchIncomplete, decision.Code, PassId, Rows(decision.Incomplete))],
        DecisionKind.Split =>
        [
            new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows(decision.Contest)),
            new ResidueCluster(Alignment.SearchIncomplete, decision.Code, PassId, Rows(decision.Incomplete)),
        ],
        _ => [],
    };

    private IReadOnlyList<ConsolidatedRow> Rows(IEnumerable<int> indexes) =>
        [.. indexes.Distinct().Select(index => Workspace.Items[index].Row)];

    private static IReadOnlyList<BlockedRow> Placed(MatchWorkspace workspace, IReadOnlyList<string> block) =>
        [.. workspace.Free
            .Select(index => MatchKeys.Compose(workspace.Items[index], block, workspace.Schema) is { } key ? new BlockedRow(index, key) : null)
            .OfType<BlockedRow>()];

    private sealed record BlockedRow(int Index, string Key);

    private sealed record Component(int[] Targets, int[] Eligible, ImmutableDictionary<int, int[]> ByTarget);
}

internal enum DecisionKind { Commit, Contest, Incomplete, Split }

internal sealed record Decision(DecisionKind Kind, MatchProposal[] Commits, int[] Contest, int[] Incomplete, string? Code)
{
    public static Decision Ready(MatchProposal[] commits) => new(DecisionKind.Commit, commits, [], [], null);

    public static Decision Contested(int[] items) => new(DecisionKind.Contest, [], items, [], null);

    public static Decision Exhausted(int[] items) => new(DecisionKind.Incomplete, [], [], items, "BudgetExhausted");

    public static Decision Blocked(int[] items) => new(DecisionKind.Incomplete, [], [], items, "BlockTooLarge");

    public static Decision Mixed(int[] contest, int[] incomplete) => new(DecisionKind.Split, [], contest, incomplete, "BudgetExhausted");
}
