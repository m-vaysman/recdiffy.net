using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed class MatchWorkspace
{
    public required string PassId { get; init; }

    public required IReadOnlyList<ActiveItem> Items { get; init; }

    public required ImmutableHashSet<int> Free { get; init; }

    public required IReadOnlyDictionary<string, FieldDeclaration> Schema { get; init; }

    public required SearchLimits Limits { get; init; }

    public required string? SharedField { get; init; }

    public required int MaxGroupSize { get; init; }

    public required CancellationToken Cancellation { get; init; }
}

internal sealed record ActiveItem(int Index, Side Side, ConsolidatedRow Row);

internal sealed record CommittedMatch(string PassId, string Key, IReadOnlyList<ConsolidatedRow> Left, IReadOnlyList<ConsolidatedRow> Right);

internal sealed record ResidueCluster(Alignment Alignment, string? Code, string? PassId, IReadOnlyList<ConsolidatedRow> Rows);

internal sealed record MatchReport(IReadOnlyList<CommittedMatch> Committed, IReadOnlyList<ResidueCluster> Residue, MatchCounters Counters);

internal sealed class MatchCounters
{
    public int IndexBuilds { get; set; }

    public int SubsetSearches { get; set; }

    public int DirtyResearches { get; set; }

    public int Components { get; set; }
}

internal static class MatchKeys
{
    public static string? Compose(ActiveItem item, IReadOnlyList<string> fields, IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        var parts = fields.Select(field => Text(item.Row.Context, schema[field])).ToArray();
        return parts.Any(part => part is null) ? null : string.Join('\u001f', parts);
    }

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
        var schema = fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var shared = schema.ContainsKey("TradeRef") ? "TradeRef" : null;
        var engines = passes.Select(pass => Create(pass, limits)).ToArray();
        var counters = new MatchCounters();
        var rowIndex = items.ToDictionary(item => item.Row, item => item.Index, RowIdentity.Instance);
        var free = items.Select(item => item.Index).ToHashSet();
        var workspace = Workspace(passes.Count == 0 ? "" : passes[0].Id, items, free.ToImmutableHashSet(), schema, limits, shared, limits.MaxGroupSize, cancellation);
        foreach (var engine in engines)
            engine.BuildIndex(workspace, counters);

        var committed = ImmutableList<CommittedMatch>.Empty;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var chosen = engines.Select(engine => (Engine: engine, Proposal: engine.NextReady())).FirstOrDefault(pair => pair.Proposal is not null);
            if (chosen.Proposal is null)
                break;
            committed = committed.Add(Commit(chosen.Engine.PassId, chosen.Proposal, items));
            var taken = chosen.Proposal.Left.Concat(chosen.Proposal.Right).ToArray();
            free.ExceptWith(taken);
            foreach (var engine in engines)
                engine.Release(taken, counters);
        }

        var claimed = new HashSet<int>();
        var residue = new List<ResidueCluster>();
        foreach (var engine in engines)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var cluster in engine.Residue(workspace))
            {
                var rows = cluster.Rows.Where(row => claimed.Contains(rowIndex[row]) is false).ToArray();
                if (rows.Length == 0)
                    continue;
                foreach (var row in rows)
                    claimed.Add(rowIndex[row]);
                residue.Add(cluster with { Rows = rows });
            }
        }

        var only = items.Where(item => free.Contains(item.Index) && claimed.Contains(item.Index) is false)
            .Select(item => new ResidueCluster(item.Side == Side.Left ? Alignment.LeftOnly : Alignment.RightOnly, null, null, [item.Row]));
        return new MatchReport(committed, [.. residue, .. only], counters);
    }

    private static ActiveItem[] Items(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right) =>
        [.. left.Concat(right).Where(row => row is { Quarantined: false, Suppression: null } && row.Context.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Invalid)).Select((row, index) => new ActiveItem(index, row.Context.Side, row))];

    private static MatchWorkspace Workspace(
        string passId,
        IReadOnlyList<ActiveItem> items,
        ImmutableHashSet<int> free,
        IReadOnlyDictionary<string, FieldDeclaration> schema,
        SearchLimits limits,
        string? shared,
        int maxGroup,
        CancellationToken cancellation) => new()
        {
            PassId = passId,
            Items = items,
            Free = free,
            Schema = schema,
            Limits = limits,
            SharedField = shared,
            MaxGroupSize = maxGroup,
            Cancellation = cancellation,
        };

    private static CascadePass Create(PassDeclaration pass, SearchLimits limits) => pass.Pass switch
    {
        "match.sharedRef" => new PairPass(pass.Id, pass.Key.Count == 0 ? ["Counterparty", "TradeRef"] : pass.Key, false, limits.MaxGroupSize),
        "match.naturalKey" or "match.reducedKey" => new PairPass(pass.Id, pass.Key, true, pass.MaxGroupSize ?? limits.MaxGroupSize),
        "match.groupSum" => new GroupPass(
            pass.Id,
            pass.Block,
            pass.Conserve,
            pass.Many == "right" ? Side.Right : Side.Left,
            pass.MaxGroupSize ?? limits.MaxGroupSize),
        var other => throw new InvalidOperationException($"Pass '{other}' is not a cascade pass."),
    };

    private static CommittedMatch Commit(string passId, MatchProposal proposal, IReadOnlyList<ActiveItem> items) =>
        new(passId, proposal.Key, Rows(items, proposal.Left), Rows(items, proposal.Right));

    private static IReadOnlyList<ConsolidatedRow> Rows(IReadOnlyList<ActiveItem> items, IReadOnlyList<int> indexes) =>
        [.. indexes.Select(index => items[index].Row)];

    private sealed class RowIdentity : IEqualityComparer<ConsolidatedRow>
    {
        public static RowIdentity Instance { get; } = new();

        public bool Equals(ConsolidatedRow? left, ConsolidatedRow? right) => ReferenceEquals(left, right);

        public int GetHashCode(ConsolidatedRow row) => RuntimeHelpers.GetHashCode(row);
    }
}

internal abstract class CascadePass : IMatchPass
{
    public abstract string PassId { get; }

    public abstract PassCardinality Cardinality { get; }

    public abstract int MaxGroup { get; }

    public abstract void Propose(in PassContext context, ProposalSink sink);

    public abstract IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace);

    public abstract void BuildIndex(MatchWorkspace workspace, MatchCounters counters);

    public abstract MatchProposal? NextReady();

    public abstract void Release(IReadOnlyCollection<int> taken, MatchCounters counters);

    public MatchProposal? ProposeFirst(MatchWorkspace workspace)
    {
        var sink = new ProposalSink();
        Propose(new PassContext(workspace), sink);
        return sink.Proposals
            .Where(proposal => proposal.Left.All(workspace.Free.Contains) && proposal.Right.All(workspace.Free.Contains))
            .OrderBy(proposal => proposal.Key, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

internal sealed class PairPass(string passId, IReadOnlyList<string> fields, bool veto, int maxGroup) : CascadePass
{
    private readonly Dictionary<string, HashSet<int>> _left = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<int>> _right = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _keyOf = [];
    private readonly Dictionary<string, MatchProposal> _ready = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _readyOrder = new(StringComparer.Ordinal);
    private MatchWorkspace? _indexed;

    public override string PassId => passId;

    public override PassCardinality Cardinality => PassCardinality.OneToOne;

    public override int MaxGroup => maxGroup;

    public override void BuildIndex(MatchWorkspace workspace, MatchCounters counters)
    {
        _indexed = workspace;
        counters.IndexBuilds++;
        foreach (var index in workspace.Free)
        {
            var key = MatchKeys.Compose(workspace.Items[index], fields, workspace.Schema);
            if (key is null)
                continue;
            var side = workspace.Items[index].Side == Side.Left ? _left : _right;
            if (!side.TryGetValue(key, out var members))
            {
                members = [];
                side[key] = members;
            }

            members.Add(index);
            _keyOf[index] = key;
        }

        foreach (var key in _left.Keys.Union(_right.Keys, StringComparer.Ordinal))
            Refresh(key);
    }

    public override MatchProposal? NextReady()
    {
        if (_readyOrder.Count == 0)
            return null;
        var key = _readyOrder.Min;
        return key is null ? null : _ready[key];
    }

    public override void Release(IReadOnlyCollection<int> taken, MatchCounters counters)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in taken)
        {
            if (!_keyOf.TryGetValue(index, out var key))
                continue;
            dirty.Add(key);
            (_indexed!.Items[index].Side == Side.Left ? _left : _right)[key].Remove(index);
            _keyOf.Remove(index);
        }

        foreach (var key in dirty)
            Refresh(key);
    }

    public override void Propose(in PassContext context, ProposalSink sink)
    {
        var workspace = context.Workspace;
        foreach (var bucket in Eligible(workspace, veto).Where(bucket => bucket.Left.Length == 1 && bucket.Right.Length == 1))
            sink.Propose([bucket.Left[0]], [bucket.Right[0]], bucket.Key);
    }

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        _indexed is null
            ? [.. Eligible(workspace, veto)
                .Where(bucket => bucket.Left.Length > 0 && bucket.Right.Length > 0 && (bucket.Left.Length > 1 || bucket.Right.Length > 1))
                .Select(bucket => new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(workspace, [.. bucket.Left, .. bucket.Right])))]
            : [.. _left.Keys.Union(_right.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).Select(IndexedResidue).OfType<ResidueCluster>()];

    private ResidueCluster? IndexedResidue(string key)
    {
        var (left, right) = Sides(key);
        return left.Length > 0 && right.Length > 0 && (left.Length != 1 || right.Length != 1)
            ? new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(_indexed!, [.. left.OrderBy(index => index), .. right.OrderBy(index => index)]))
            : null;
    }

    private void Refresh(string key)
    {
        _ready.Remove(key);
        _readyOrder.Remove(key);
        var (left, right) = Sides(key);
        if (left.Length != 1 || right.Length != 1)
            return;
        _ready[key] = new MatchProposal([left[0]], [right[0]], key);
        _readyOrder.Add(key);
    }

    private (int[] Left, int[] Right) Sides(string key)
    {
        var left = _left.TryGetValue(key, out var leftMembers) ? leftMembers : [];
        var right = _right.TryGetValue(key, out var rightMembers) ? rightMembers : [];
        var eligibleLeft = left.Where(index => right.Any(other => !veto || !Veto(index, other))).OrderBy(index => index).ToArray();
        var eligibleRight = right.Where(index => left.Any(other => !veto || !Veto(other, index))).OrderBy(index => index).ToArray();
        return (eligibleLeft, eligibleRight);
    }

    private bool Veto(int left, int right) =>
        MatchKeys.Vetoed(_indexed!.Items[left], _indexed.Items[right], _indexed.SharedField, _indexed.Schema);

    private IEnumerable<Bucket> Eligible(MatchWorkspace workspace, bool applyVeto) =>
        workspace.Free
            .Select(index => workspace.Items[index])
            .Select(item => (Item: item, Key: MatchKeys.Compose(item, fields, workspace.Schema)))
            .Where(pair => pair.Key is not null)
            .GroupBy(pair => pair.Key!, StringComparer.Ordinal)
            .Select(group => BucketOf(group.Key, [.. group.Select(pair => pair.Item)], workspace, applyVeto));

    private static Bucket BucketOf(string key, IReadOnlyList<ActiveItem> items, MatchWorkspace workspace, bool applyVeto)
    {
        var left = items.Where(item => item.Side == Side.Left).Select(item => item.Index).ToArray();
        var right = items.Where(item => item.Side == Side.Right).Select(item => item.Index).ToArray();
        var eligibleLeft = left.Where(index => right.Any(other => !applyVeto || !MatchKeys.Vetoed(workspace.Items[index], workspace.Items[other], workspace.SharedField, workspace.Schema))).ToArray();
        var eligibleRight = right.Where(index => left.Any(other => !applyVeto || !MatchKeys.Vetoed(workspace.Items[other], workspace.Items[index], workspace.SharedField, workspace.Schema))).ToArray();
        return new Bucket(key, eligibleLeft, eligibleRight);
    }

    private static IReadOnlyList<ConsolidatedRow> Rows(MatchWorkspace workspace, IReadOnlyList<int> indexes) =>
        [.. indexes.Select(index => workspace.Items[index].Row)];

    private sealed record Bucket(string Key, int[] Left, int[] Right);
}

internal sealed class GroupPass(string passId, IReadOnlyList<string> block, IReadOnlyList<string> conserve, Side many, int maxGroup) : CascadePass
{
    private readonly Dictionary<string, HashSet<int>> _blocks = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _blockOf = [];
    private readonly Dictionary<string, Decision[]> _decisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ReadyProposal>> _ready = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _readyOrder = new(StringComparer.Ordinal);
    private MatchWorkspace? _indexed;

    public override string PassId => passId;

    public override PassCardinality Cardinality => many == Side.Left ? PassCardinality.ManyToOne : PassCardinality.OneToMany;

    public override int MaxGroup => maxGroup;

    public override void BuildIndex(MatchWorkspace workspace, MatchCounters counters)
    {
        _indexed = workspace;
        counters.IndexBuilds++;
        foreach (var index in workspace.Free)
        {
            var key = MatchKeys.Compose(workspace.Items[index], block, workspace.Schema);
            if (key is null)
                continue;
            if (!_blocks.TryGetValue(key, out var members))
            {
                members = [];
                _blocks[key] = members;
            }

            members.Add(index);
            _blockOf[index] = key;
        }

        foreach (var key in _blocks.Keys.ToArray())
            Evaluate(key, counters, false);
    }

    public override MatchProposal? NextReady()
    {
        if (_readyOrder.Count == 0)
            return null;
        var key = _readyOrder.Min;
        return key is null ? null : _ready[key][0].Proposal;
    }

    public override void Release(IReadOnlyCollection<int> taken, MatchCounters counters)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in taken)
        {
            if (!_blockOf.TryGetValue(index, out var key))
                continue;
            dirty.Add(key);
            _blocks[key].Remove(index);
            _blockOf.Remove(index);
        }

        foreach (var key in dirty)
            Evaluate(key, counters, true);
    }

    public override void Propose(in PassContext context, ProposalSink sink)
    {
        var workspace = context.Workspace;
        foreach (var proposal in Components(workspace).SelectMany(component => Proposals(component, workspace)))
            sink.Propose(proposal.Left, proposal.Right, proposal.Key);
    }

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        _indexed is null
            ? [.. Components(workspace).SelectMany(component => Clusters(component, workspace))]
            : [.. _decisions.SelectMany(pair => pair.Value.SelectMany(decision => FromDecision(decision, _indexed)))];

    private IEnumerable<MatchProposal> Proposals(Component component, MatchWorkspace workspace) =>
        Decide(component, workspace, null, false) switch
        {
            { Kind: DecisionKind.Commit } decision => decision.Commits,
            _ => [],
        };

    private void Evaluate(string key, MatchCounters counters, bool dirty)
    {
        Drop(key);
        if (!_blocks.TryGetValue(key, out var members) || members.Count == 0)
        {
            _decisions.Remove(key);
            _blocks.Remove(key);
            return;
        }

        var items = members.OrderBy(index => index).Select(index => _indexed!.Items[index]).ToArray();
        var components = Couple(items, _indexed!).ToArray();
        var decisions = new Decision[components.Length];
        for (var index = 0; index < components.Length; index++)
        {
            counters.Components++;
            decisions[index] = Decide(components[index], _indexed!, counters, dirty);
            if (decisions[index].Kind != DecisionKind.Commit)
                continue;
            foreach (var proposal in decisions[index].Commits)
                Add(key, proposal);
        }

        _decisions[key] = decisions;
    }

    private void Add(string blockKey, MatchProposal proposal)
    {
        if (!_ready.TryGetValue(proposal.Key, out var list))
        {
            list = [];
            _ready[proposal.Key] = list;
            _readyOrder.Add(proposal.Key);
        }

        list.Add(new ReadyProposal(proposal, blockKey));
    }

    private void Drop(string blockKey)
    {
        foreach (var key in _ready.Keys.ToArray())
        {
            _ready[key].RemoveAll(ready => ready.Block == blockKey);
            if (_ready[key].Count > 0)
                continue;
            _ready.Remove(key);
            _readyOrder.Remove(key);
        }
    }

    private IEnumerable<ResidueCluster> FromDecision(Decision decision, MatchWorkspace workspace) => decision.Kind switch
    {
        DecisionKind.Contest => [new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(workspace, decision.Contest))],
        DecisionKind.Incomplete => [new ResidueCluster(Alignment.SearchIncomplete, decision.Code, passId, Rows(workspace, decision.Incomplete))],
        DecisionKind.Split =>
        [
            new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(workspace, decision.Contest)),
            new ResidueCluster(Alignment.SearchIncomplete, decision.Code, passId, Rows(workspace, decision.Incomplete)),
        ],
        _ => [],
    };

    private IEnumerable<ResidueCluster> Clusters(Component component, MatchWorkspace workspace) =>
        FromDecision(Decide(component, workspace, null, false), workspace);

    private Decision Decide(Component component, MatchWorkspace workspace, MatchCounters? counters, bool dirty)
    {
        if (component.Targets.Length > workspace.Limits.MaxTargetsPerComponent || component.Eligible.Length > workspace.Limits.MaxBlockItems)
            return Decision.Blocked([.. component.Targets, .. component.Eligible]);
        var searches = component.Targets.Select(target => (Target: target, Search: Search(target, component, workspace, counters, dirty))).ToArray();
        var overlap = searches.Where(pair => pair.Search.State == SearchState.Unique).Select(pair => pair.Search.DrawnItems).ToArray();
        var shared = overlap.SelectMany(items => items).GroupBy(item => item).Any(group => group.Count() > 1);
        var contest = searches.Any(pair => pair.Search.State == SearchState.Contest) || shared;
        var incomplete = searches.Any(pair => pair.Search.State == SearchState.Incomplete);
        return (contest, incomplete) switch
        {
            (false, false) => Decision.Ready(searches.Where(pair => pair.Search.State == SearchState.Unique).Select(pair => ProposalFor(pair.Target, pair.Search.DrawnItems, workspace)).ToArray()),
            (true, true) => Decision.Mixed(
                [.. searches.Where(pair => pair.Search.State is SearchState.Contest or SearchState.Unique).SelectMany(pair => new[] { pair.Target }.Concat(pair.Search.DrawnItems)), .. searches.Where(pair => pair.Search.State == SearchState.Incomplete).SelectMany(pair => pair.Search.DrawnItems)],
                [.. searches.Where(pair => pair.Search.State == SearchState.Incomplete).SelectMany(pair => EligibleFor(pair.Target, component).Except(pair.Search.DrawnItems))]),
            (true, false) => Decision.Contested([.. searches.Where(pair => pair.Search.State is not SearchState.None).SelectMany(pair => new[] { pair.Target }.Concat(pair.Search.DrawnItems))]),
            _ => Decision.Exhausted([.. component.Targets, .. component.Eligible]),
        };
    }

    private MatchProposal ProposalFor(int target, int[] drawn, MatchWorkspace workspace) =>
        many == Side.Left
            ? new MatchProposal(drawn, [target], Key(target, workspace))
            : new MatchProposal([target], drawn, Key(target, workspace));

    private TargetSearch Search(int target, Component component, MatchWorkspace workspace, MatchCounters? counters, bool dirty)
    {
        var targetValues = Conserved(workspace.Items[target].Row.Context);
        if (targetValues is null)
            return new TargetSearch(SearchState.None, []);
        if (counters is not null)
        {
            counters.SubsetSearches++;
            if (dirty)
                counters.DirtyResearches++;
        }

        var classes = EligibleFor(target, component)
            .Select(index => workspace.Items[index])
            .Select(item => (Item: item, Values: Conserved(item.Row.Context)))
            .Where(pair => pair.Values is not null)
            .GroupBy(pair => string.Join('\u001f', pair.Values!.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .Select(group => new ConservedClass(group.First().Values!, [.. group.Select(pair => pair.Item.Index)]))
            .OrderByDescending(cls => cls.Values[0])
            .ThenBy(cls => string.Join('\u001f', cls.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .ToArray();
        return SubsetSearch.Search(classes, targetValues, maxGroup, workspace.Limits.MaxNodesPerTarget, workspace.Cancellation);
    }

    private decimal[]? Conserved(RowContext row)
    {
        var values = new decimal[conserve.Count];
        for (var index = 0; index < conserve.Count; index++)
        {
            var field = conserve[index];
            if (row.IsNull(field))
                return null;
            values[index] = row.Values[field].Number;
        }

        return values;
    }

    private IEnumerable<Component> Components(MatchWorkspace workspace) =>
        workspace.Free
            .Select(index => workspace.Items[index])
            .Select(item => (Item: item, Key: MatchKeys.Compose(item, block, workspace.Schema)))
            .Where(pair => pair.Key is not null)
            .GroupBy(pair => pair.Key!, StringComparer.Ordinal)
            .SelectMany(group => Couple([.. group.Select(pair => pair.Item)], workspace));

    private IEnumerable<Component> Couple(IReadOnlyList<ActiveItem> blockItems, MatchWorkspace workspace)
    {
        var targets = blockItems.Where(item => item.Side != many).Select(item => item.Index).OrderBy(index => index).ToArray();
        var manyItems = blockItems.Where(item => item.Side == many).Select(item => item.Index).ToArray();
        var eligible = targets.ToDictionary(
            target => target,
            target => manyItems.Where(item => !Veto(target, item, workspace)).ToArray());
        var groups = targets.Aggregate(
            ImmutableList<int[]>.Empty,
            (components, target) =>
            {
                var joined = components.Where(component => component.Any(other => eligible[target].Intersect(eligible[other]).Any())).ToArray();
                var kept = components.Where(component => joined.Contains(component) is false);
                var merged = joined.SelectMany(component => component).Append(target).Distinct().ToArray();
                return kept.Append(merged).ToImmutableList();
            });
        return groups.Select(group => new Component(
            group,
            [.. group.SelectMany(target => eligible[target]).Distinct()],
            eligible));
    }

    private static bool Veto(int target, int item, MatchWorkspace workspace) =>
        workspace.Items[target].Side == Side.Left
            ? MatchKeys.Vetoed(workspace.Items[target], workspace.Items[item], workspace.SharedField, workspace.Schema)
            : MatchKeys.Vetoed(workspace.Items[item], workspace.Items[target], workspace.SharedField, workspace.Schema);

    private int[] EligibleFor(int target, Component component) => component.ByTarget[target];

    private static string Key(int target, MatchWorkspace workspace) => workspace.Items[target].Row.Context.RowKey;

    private static IReadOnlyList<ConsolidatedRow> Rows(MatchWorkspace workspace, IEnumerable<int> indexes) =>
        [.. indexes.Distinct().Select(index => workspace.Items[index].Row)];

    private sealed record ReadyProposal(MatchProposal Proposal, string Block);

    private sealed record Component(int[] Targets, int[] Eligible, Dictionary<int, int[]> ByTarget);

    private enum DecisionKind { Commit, Contest, Incomplete, Split }

    private sealed record Decision(DecisionKind Kind, MatchProposal[] Commits, int[] Contest, int[] Incomplete, string? Code)
    {
        public static Decision Ready(MatchProposal[] commits) => new(DecisionKind.Commit, commits, [], [], null);

        public static Decision Contested(int[] items) => new(DecisionKind.Contest, [], items, [], null);

        public static Decision Exhausted(int[] items) => new(DecisionKind.Incomplete, [], [], items, "BudgetExhausted");

        public static Decision Blocked(int[] items) => new(DecisionKind.Incomplete, [], [], items, "BlockTooLarge");

        public static Decision Mixed(int[] contest, int[] incomplete) => new(DecisionKind.Split, [], contest, incomplete, "BudgetExhausted");
    }
}
