using System.Collections.Immutable;
using System.Globalization;
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
}

internal sealed record ActiveItem(int Index, Side Side, ConsolidatedRow Row);

internal sealed record CommittedMatch(string PassId, string Key, IReadOnlyList<ConsolidatedRow> Left, IReadOnlyList<ConsolidatedRow> Right);

internal sealed record ResidueCluster(Alignment Alignment, string? Code, string? PassId, IReadOnlyList<ConsolidatedRow> Rows);

internal sealed record MatchReport(IReadOnlyList<CommittedMatch> Committed, IReadOnlyList<ResidueCluster> Residue);

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
        SearchLimits limits)
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
        var free = items.Select(item => item.Index).ToImmutableHashSet();
        var committed = ImmutableList<CommittedMatch>.Empty;
        var cursor = 0;
        while (cursor < engines.Length)
        {
            var workspace = Workspace(engines[cursor].PassId, items, free, schema, limits, shared, engines[cursor].MaxGroup);
            var proposal = engines[cursor].ProposeFirst(workspace);
            if (proposal is null)
            {
                cursor++;
                continue;
            }

            committed = committed.Add(Commit(engines[cursor].PassId, proposal, items));
            free = free.Except(proposal.Left).Except(proposal.Right);
            cursor = 0;
        }

        var claimed = ImmutableHashSet<int>.Empty;
        var residue = engines.Aggregate(
            (Claimed: claimed, Clusters: ImmutableList<ResidueCluster>.Empty),
            (state, engine) =>
            {
                var found = engine.Residue(Workspace(engine.PassId, items, free, schema, limits, shared, engine.MaxGroup))
                    .Select(cluster => cluster with { Rows = [.. cluster.Rows.Where(row => state.Claimed.Contains(IndexOf(items, row)) is false)] })
                    .Where(cluster => cluster.Rows.Count > 0)
                    .ToArray();
                var taken = found.SelectMany(cluster => cluster.Rows.Select(row => IndexOf(items, row))).ToImmutableHashSet();
                return (state.Claimed.Union(taken), state.Clusters.AddRange(found));
            });
        var only = items.Where(item => free.Contains(item.Index) && residue.Claimed.Contains(item.Index) is false)
            .Select(item => new ResidueCluster(item.Side == Side.Left ? Alignment.LeftOnly : Alignment.RightOnly, null, null, [item.Row]));
        return new MatchReport(committed, [.. residue.Clusters, .. only]);
    }

    private static ActiveItem[] Items(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right) =>
        [.. left.Concat(right).Where(row => row is { Quarantined: false, Suppression: null }).Select((row, index) => new ActiveItem(index, row.Context.Side, row))];

    private static MatchWorkspace Workspace(
        string passId,
        IReadOnlyList<ActiveItem> items,
        ImmutableHashSet<int> free,
        IReadOnlyDictionary<string, FieldDeclaration> schema,
        SearchLimits limits,
        string? shared,
        int maxGroup) => new()
        {
            PassId = passId,
            Items = items,
            Free = free,
            Schema = schema,
            Limits = limits,
            SharedField = shared,
            MaxGroupSize = maxGroup,
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

    private static int IndexOf(IReadOnlyList<ActiveItem> items, ConsolidatedRow row) =>
        items.First(item => ReferenceEquals(item.Row, row)).Index;
}

internal abstract class CascadePass : IMatchPass
{
    public abstract string PassId { get; }

    public abstract PassCardinality Cardinality { get; }

    public abstract int MaxGroup { get; }

    public abstract void Propose(in PassContext context, ProposalSink sink);

    public abstract IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace);

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
    public override string PassId => passId;

    public override PassCardinality Cardinality => PassCardinality.OneToOne;

    public override int MaxGroup => maxGroup;

    public override void Propose(in PassContext context, ProposalSink sink)
    {
        var workspace = context.Workspace;
        foreach (var bucket in Eligible(workspace, veto).Where(bucket => bucket.Left.Length == 1 && bucket.Right.Length == 1))
            sink.Propose([bucket.Left[0]], [bucket.Right[0]], bucket.Key);
    }

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        [.. Eligible(workspace, veto)
            .Where(bucket => bucket.Left.Length > 0 && bucket.Right.Length > 0 && (bucket.Left.Length > 1 || bucket.Right.Length > 1))
            .Select(bucket => new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(workspace, [.. bucket.Left, .. bucket.Right])))];

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
    public override string PassId => passId;

    public override PassCardinality Cardinality => many == Side.Left ? PassCardinality.ManyToOne : PassCardinality.OneToMany;

    public override int MaxGroup => maxGroup;

    public override void Propose(in PassContext context, ProposalSink sink)
    {
        var workspace = context.Workspace;
        foreach (var proposal in Components(workspace).SelectMany(component => Proposals(component, workspace)))
            sink.Propose(proposal.Left, proposal.Right, proposal.Key);
    }

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        [.. Components(workspace).SelectMany(component => Clusters(component, workspace))];

    private IEnumerable<MatchProposal> Proposals(Component component, MatchWorkspace workspace) =>
        Decide(component, workspace) switch
        {
            { Kind: DecisionKind.Commit } decision => decision.Commits,
            _ => [],
        };

    private IEnumerable<ResidueCluster> Clusters(Component component, MatchWorkspace workspace) =>
        Decide(component, workspace) switch
        {
            { Kind: DecisionKind.Contest } decision => [new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(workspace, decision.Contest))],
            { Kind: DecisionKind.Incomplete } decision => [new ResidueCluster(Alignment.SearchIncomplete, decision.Code, passId, Rows(workspace, decision.Incomplete))],
            { Kind: DecisionKind.Split } decision =>
            [
                new ResidueCluster(Alignment.Ambiguous, null, passId, Rows(workspace, decision.Contest)),
                new ResidueCluster(Alignment.SearchIncomplete, decision.Code, passId, Rows(workspace, decision.Incomplete)),
            ],
            _ => [],
        };

    private Decision Decide(Component component, MatchWorkspace workspace)
    {
        if (component.Targets.Length > workspace.Limits.MaxTargetsPerComponent || component.Eligible.Length > workspace.Limits.MaxBlockItems)
            return Decision.Blocked([.. component.Targets, .. component.Eligible]);
        var searches = component.Targets.Select(target => (Target: target, Search: Search(target, component, workspace))).ToArray();
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

    private TargetSearch Search(int target, Component component, MatchWorkspace workspace)
    {
        var eligible = EligibleFor(target, component).Select(index => workspace.Items[index]).ToArray();
        var classes = eligible
            .Select(item => (Item: item, Values: conserve.Select(field => workspace.Items[item.Index].Row.Context.Values[field].Number).ToArray()))
            .GroupBy(pair => string.Join('\u001f', pair.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .Select(group => new ConservedClass(group.First().Values, [.. group.Select(pair => pair.Item.Index)]))
            .OrderByDescending(cls => cls.Values[0])
            .ThenBy(cls => string.Join('\u001f', cls.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))), StringComparer.Ordinal)
            .ToArray();
        var targetValues = conserve.Select(field => workspace.Items[target].Row.Context.Values[field].Number).ToArray();
        return SubsetSearch.Search(classes, targetValues, workspace.MaxGroupSize, workspace.Limits.MaxNodesPerTarget);
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
