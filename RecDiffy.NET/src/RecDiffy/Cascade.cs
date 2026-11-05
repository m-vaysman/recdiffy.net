namespace RecDiffy.Engine;

internal sealed class MatchItem
{
    public int Id { get; init; }
    public required Side Side { get; init; }
    public required string PathId { get; init; }
    public required MutableRow Row { get; init; }
    public required List<SourceRef> Sources { get; init; }
    public bool Package { get; init; }
    public bool Lifecycle { get; init; }
    public bool Consumed { get; set; }
    public Dictionary<string, (PassVerdict Verdict, string? Key)> Verdicts { get; } = [];
}

internal sealed class MatchGroup
{
    public List<MatchItem> Left { get; } = [];
    public List<MatchItem> Right { get; } = [];
    public string? PassId { get; set; }
    public string? Key { get; set; }
    public Alignment Alignment { get; set; } = Alignment.Matched;
    public string? Detail { get; set; }
}

internal sealed class MatchSession
{
    public required List<MatchItem> Items { get; init; }
    public required RunState State { get; init; }
    public List<int> ActiveIds { get; } = [];
    public MatchItem Item(int id) => Items[id];

    public bool TryValue(int itemId, string field, out CanonicalValue value)
    {
        var row = Items[itemId].Row;
        if (row.Values.TryGetValue(field, out var found) && !found.IsNull)
        {
            value = found;
            return true;
        }
        value = default;
        return false;
    }

    public void RefreshActive()
    {
        ActiveIds.Clear();
        foreach (var item in Items)
        {
            if (!item.Consumed)
                ActiveIds.Add(item.Id);
        }
    }
}

internal static class Cascade
{
    public static (List<MatchGroup> Groups, List<HashSet<int>> Contests, List<(HashSet<int> Items, string Code)> Incomplete) Match(
        IReadOnlyList<MatchItem> items,
        RunState state,
        CancellationToken cancellation)
    {
        var session = new MatchSession { Items = items.ToList(), State = state };
        session.RefreshActive();
        var groups = new List<MatchGroup>();
        var contests = new List<HashSet<int>>();
        var incomplete = new List<(HashSet<int>, string)>();
        var paths = state.Profile.Model.Paths;
        foreach (var path in paths)
        {
            var cursor = 0;
            while (cursor < path.Passes.Count)
            {
                cancellation.ThrowIfCancellationRequested();
                var committed = RunPass(session, path, path.Passes[cursor], groups, contests, incomplete, cancellation);
                if (committed > 0 && cursor > 0)
                {
                    var stable = false;
                    while (!stable)
                    {
                        stable = true;
                        for (var earlier = 0; earlier < cursor; earlier++)
                        {
                            if (RunPass(session, path, path.Passes[earlier], groups, contests, incomplete, cancellation) > 0)
                            {
                                stable = false;
                                break;
                            }
                        }
                    }
                }
                cursor++;
            }
        }
        return (groups, contests, incomplete);
    }

    private static int RunPass(
        MatchSession session,
        PathModel path,
        PassModel pass,
        List<MatchGroup> groups,
        List<HashSet<int>> contests,
        List<(HashSet<int>, string)> incomplete,
        CancellationToken cancellation)
    {
        var active = session.Items.Where(item => !item.Consumed && item.PathId == path.Id).ToList();
        return pass.Kind switch
        {
            "match.groupSum" => RunGroup(session, pass, active, groups, contests, incomplete, cancellation),
            _ => RunOneToOne(session, pass, active, groups),
        };
    }

    private static int RunOneToOne(MatchSession session, PassModel pass, List<MatchItem> active, List<MatchGroup> groups)
    {
        var buckets = new Dictionary<string, List<MatchItem>>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            if (!TryKey(session.State, item, pass.Key.Count > 0 ? pass.Key : ["Counterparty", "TradeRef"], out var key))
            {
                Remember(item, pass, PassVerdict.NoKey, null);
                continue;
            }
            if (pass.Kind == "match.sharedRef" && !IsShared(session.State, item, "TradeRef"))
            {
                Remember(item, pass, PassVerdict.NoKey, key);
                continue;
            }
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = [];
            list.Add(item);
        }

        var committed = 0;
        foreach (var bucket in buckets)
        {
            var left = bucket.Value.Where(item => item.Side == Side.Left).ToList();
            var right = bucket.Value.Where(item => item.Side == Side.Right).ToList();
            if (left.Count == 0 || right.Count == 0)
            {
                foreach (var item in bucket.Value)
                    Remember(item, pass, PassVerdict.NoCandidate, bucket.Key);
                continue;
            }

            var edges = new List<(MatchItem Left, MatchItem Right)>();
            foreach (var l in left)
            foreach (var r in right)
            {
                if (Veto(session.State, l, r, pass.Veto))
                    continue;
                edges.Add((l, r));
            }
            if (edges.Count == 0)
            {
                foreach (var item in bucket.Value)
                    Remember(item, pass, PassVerdict.Vetoed, bucket.Key);
                continue;
            }

            var components = Components(edges);
            var touched = new HashSet<int>();
            foreach (var component in components)
            {
                foreach (var item in component)
                    touched.Add(item.Id);
                var cLeft = component.Where(item => item.Side == Side.Left).ToList();
                var cRight = component.Where(item => item.Side == Side.Right).ToList();
                if (cLeft.Count == 1 && cRight.Count == 1)
                {
                    Commit(groups, pass, bucket.Key, cLeft, cRight);
                    committed++;
                }
                else
                {
                    foreach (var item in component)
                        Remember(item, pass, PassVerdict.Contest, bucket.Key);
                }
            }
            foreach (var item in bucket.Value)
            {
                if (!touched.Contains(item.Id) && !item.Consumed)
                    Remember(item, pass, PassVerdict.Vetoed, bucket.Key);
            }
        }
        return committed;
    }

    private static int RunGroup(
        MatchSession session,
        PassModel pass,
        List<MatchItem> active,
        List<MatchGroup> groups,
        List<HashSet<int>> contests,
        List<(HashSet<int>, string)> incomplete,
        CancellationToken cancellation)
    {
        var many = pass.Many == "right" ? Side.Right : Side.Left;
        var buckets = new Dictionary<string, List<MatchItem>>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            if (!TryKey(session.State, item, pass.Block, out var key))
            {
                Remember(item, pass, PassVerdict.NoKey, null);
                continue;
            }
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = [];
            list.Add(item);
        }

        var committed = 0;
        foreach (var bucket in buckets)
        {
            cancellation.ThrowIfCancellationRequested();
            var manyItems = bucket.Value.Where(item => item.Side == many).ToList();
            var targets = bucket.Value.Where(item => item.Side != many).ToList();
            if (manyItems.Count == 0 || targets.Count == 0)
            {
                foreach (var item in bucket.Value)
                    Remember(item, pass, PassVerdict.NoCandidate, bucket.Key);
                continue;
            }

            var eligible = targets.ToDictionary(
                target => target.Id,
                target => manyItems.Where(item => !Veto(session.State, item, target, pass.Veto)).ToList());
            var parent = targets.Select(target => target.Id).ToDictionary(id => id, id => id);
            int Find(int id)
            {
                while (parent[id] != id)
                    id = parent[id] = parent[parent[id]];
                return id;
            }
            foreach (var left in targets)
            foreach (var right in targets)
            {
                if (left.Id >= right.Id)
                    continue;
                if (eligible[left.Id].Select(item => item.Id).Intersect(eligible[right.Id].Select(item => item.Id)).Any())
                    parent[Find(left.Id)] = Find(right.Id);
            }

            foreach (var component in targets.GroupBy(target => Find(target.Id)))
            {
                var componentTargets = component.ToList();
                var componentItems = componentTargets.SelectMany(target => eligible[target.Id]).DistinctBy(item => item.Id).ToList();
                if (componentItems.Count > pass.Limits.MaxBlockItems || componentTargets.Count > pass.Limits.MaxTargetsPerComponent)
                {
                    var ids = componentItems.Select(item => item.Id).Concat(componentTargets.Select(item => item.Id)).ToHashSet();
                    incomplete.Add((ids, "BlockTooLarge"));
                    foreach (var item in componentItems.Concat(componentTargets))
                        Remember(item, pass, PassVerdict.SearchIncomplete, bucket.Key);
                    continue;
                }

                var searches = componentTargets.Select(target => (Target: target, Search: Search(target, eligible[target.Id], pass, session.State, cancellation))).ToList();
                var uniques = searches.Where(s => s.Search.State == SearchState.Unique).ToList();
                var overlap = uniques.SelectMany(s => s.Search.Items).GroupBy(id => id).Any(g => g.Count() > 1);
                var anyContest = searches.Any(s => s.Search.State == SearchState.Contest) || overlap;
                var anyIncomplete = searches.Any(s => s.Search.State == SearchState.Incomplete);
                if (!anyContest && !anyIncomplete && uniques.Count > 0)
                {
                    foreach (var unique in uniques)
                    {
                        var members = unique.Search.Items.Select(id => session.Item(id)).ToList();
                        var left = many == Side.Left ? members : [unique.Target];
                        var right = many == Side.Right ? members : [unique.Target];
                        Commit(groups, pass, bucket.Key, left, right);
                        committed++;
                    }
                    foreach (var search in searches.Where(s => s.Search.State == SearchState.None))
                        Remember(search.Target, pass, PassVerdict.NoCandidate, bucket.Key);
                    foreach (var item in componentItems.Where(item => !item.Consumed))
                        Remember(item, pass, PassVerdict.NoCandidate, bucket.Key);
                }
                else if (anyContest)
                {
                    var ids = new HashSet<int>(componentTargets.Select(target => target.Id));
                    foreach (var search in searches.Where(s => s.Search.State is SearchState.Contest or SearchState.Unique))
                    {
                        foreach (var id in search.Search.ContestItems)
                            ids.Add(id);
                    }
                    contests.Add(ids);
                    foreach (var id in ids)
                        Remember(session.Item(id), pass, PassVerdict.Contest, bucket.Key);
                    foreach (var search in searches.Where(s => s.Search.State == SearchState.Incomplete))
                    {
                        var rest = eligible[search.Target.Id].Select(item => item.Id).Where(id => !ids.Contains(id)).Append(search.Target.Id).ToHashSet();
                        if (rest.Count > 0)
                            incomplete.Add((rest, "BudgetExhausted"));
                    }
                }
                else if (anyIncomplete)
                {
                    var ids = componentItems.Select(item => item.Id).Concat(componentTargets.Select(item => item.Id)).ToHashSet();
                    incomplete.Add((ids, "BudgetExhausted"));
                    foreach (var item in componentItems.Concat(componentTargets))
                        Remember(item, pass, PassVerdict.SearchIncomplete, bucket.Key);
                }
                else
                {
                    foreach (var item in componentItems.Concat(componentTargets))
                        Remember(item, pass, PassVerdict.NoCandidate, bucket.Key);
                }
            }
        }
        return committed;
    }

    private static SearchResult Search(MatchItem target, List<MatchItem> eligible, PassModel pass, RunState state, CancellationToken cancellation)
    {
        if (eligible.Count < 2)
            return SearchResult.None();
        var classes = eligible
            .GroupBy(item => ConserveKey(state, item, pass.Conserve))
            .Select(group => new SearchClass(group.Key, group.Select(item => item.Id).ToArray()))
            .OrderByDescending(c => c.Quantity)
            .ThenByDescending(c => c.Cash)
            .ToList();
        var suffix = new decimal[classes.Count + 1];
        for (var i = classes.Count - 1; i >= 0; i--)
            suffix[i] = suffix[i + 1] + classes[i].Quantity * classes[i].Ids.Length;
        var targetQty = Value(state, target, pass.Conserve[0]);
        var targetCash = pass.Conserve.Count > 1 ? Value(state, target, pass.Conserve[1]) : 0m;
        var search = new SearchWalk(classes, suffix, targetQty, targetCash, pass.Limits, cancellation);
        search.Walk(0, 0m, 0m, 0, false, new int[classes.Count]);
        return search.Result();
    }

    private static string ConserveKey(RunState state, MatchItem item, IReadOnlyList<string> fields) =>
        string.Join("|", fields.Select(field => FormatField(state, item, field)));

    private static decimal Value(RunState state, MatchItem item, string field) =>
        item.Row.Values.TryGetValue(field, out var value) && !value.IsNull ? value.Number : 0m;

    private static void Commit(List<MatchGroup> groups, PassModel pass, string key, IReadOnlyList<MatchItem> left, IReadOnlyList<MatchItem> right)
    {
        if (left.Any(item => item.Consumed) || right.Any(item => item.Consumed))
            return;
        foreach (var item in left.Concat(right))
        {
            item.Consumed = true;
            Remember(item, pass, PassVerdict.Committed, key);
        }
        var group = new MatchGroup { PassId = pass.Id, Key = key };
        group.Left.AddRange(left);
        group.Right.AddRange(right);
        groups.Add(group);
    }

    private static void Remember(MatchItem item, PassModel pass, PassVerdict verdict, string? key)
    {
        if (item.Consumed && verdict != PassVerdict.Committed)
            return;
        item.Verdicts[pass.Id] = (verdict, key);
    }

    private static List<List<MatchItem>> Components(List<(MatchItem Left, MatchItem Right)> edges)
    {
        var parent = new Dictionary<int, int>();
        int Find(int id)
        {
            if (!parent.ContainsKey(id))
                parent[id] = id;
            while (parent[id] != id)
                id = parent[id] = parent[parent[id]];
            return id;
        }
        var items = new Dictionary<int, MatchItem>();
        foreach (var edge in edges)
        {
            items[edge.Left.Id] = edge.Left;
            items[edge.Right.Id] = edge.Right;
            parent[Find(edge.Left.Id)] = Find(edge.Right.Id);
        }
        return items.Values.GroupBy(item => Find(item.Id)).Select(group => group.ToList()).ToList();
    }

    private static bool TryKey(RunState state, MatchItem item, IReadOnlyList<string> fields, out string key)
    {
        var parts = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            var text = FormatField(state, item, fields[i]);
            if (text is null)
            {
                key = "";
                return false;
            }
            parts[i] = text;
        }
        key = string.Join("|", parts);
        return true;
    }

    private static string? FormatField(RunState state, MatchItem item, string field)
    {
        if (!item.Row.Values.TryGetValue(field, out var value) || value.IsNull)
            return null;
        return value.Type switch
        {
            FieldType.Decimal => Formatting.Decimal(value.Number, state.ScaleOf(field)),
            FieldType.Date => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FieldType.Symbol => state.Pool.Symbol(value.Interned),
            FieldType.Identifier => state.Pool.Id(value.Interned).Value,
            _ => null,
        };
    }

    private static bool IsShared(RunState state, MatchItem item, string field)
    {
        if (!item.Row.Values.TryGetValue(field, out var value) || value.IsNull)
            return false;
        return state.Pool.Id(value.Interned).Scheme == "shared";
    }

    private static bool Veto(RunState state, MatchItem left, MatchItem right, string? field)
    {
        if (field is null)
            return false;
        if (!left.Row.Values.TryGetValue(field, out var a) || a.IsNull || !right.Row.Values.TryGetValue(field, out var b) || b.IsNull)
            return false;
        var leftId = state.Pool.Id(a.Interned);
        var rightId = state.Pool.Id(b.Interned);
        return leftId.Scheme == "shared" && rightId.Scheme == "shared" && !string.Equals(leftId.Value, rightId.Value, StringComparison.Ordinal);
    }

    private sealed record SearchClass(string Key, int[] Ids)
    {
        public decimal Quantity { get; } = decimal.Parse(Key.Split('|')[0], CultureInfo.InvariantCulture);
        public decimal Cash { get; } = Key.Contains('|') ? decimal.Parse(Key.Split('|')[1], CultureInfo.InvariantCulture) : 0m;
    }

    private sealed class SearchWalk
    {
        private readonly List<SearchClass> _classes;
        private readonly decimal[] _suffix;
        private readonly decimal _targetQty;
        private readonly decimal _targetCash;
        private readonly Limits _limits;
        private readonly CancellationToken _cancellation;
        private int _nodes;
        private int _solutions;
        private bool _partial;
        private bool _incomplete;
        private int[] _chosen = [];
        private readonly HashSet<int> _contest = [];

        public SearchWalk(List<SearchClass> classes, decimal[] suffix, decimal targetQty, decimal targetCash, Limits limits, CancellationToken cancellation)
        {
            _classes = classes;
            _suffix = suffix;
            _targetQty = targetQty;
            _targetCash = targetCash;
            _limits = limits;
            _cancellation = cancellation;
        }

        public void Walk(int index, decimal qty, decimal cash, int count, bool partial, int[] chosen)
        {
            _cancellation.ThrowIfCancellationRequested();
            _nodes++;
            if (_nodes > _limits.MaxNodesPerTarget)
            {
                _incomplete = true;
                return;
            }
            if (_solutions >= _limits.StopAfterSolutions)
                return;
            if (index == _classes.Count)
            {
                if (count >= 2 && count <= _limits.MaxGroupSize && qty == _targetQty && cash == _targetCash)
                {
                    _solutions++;
                    _partial |= partial;
                    _chosen = chosen.ToArray();
                    for (var i = 0; i < _classes.Count; i++)
                    {
                        if (_chosen[i] > 0)
                            foreach (var id in _classes[i].Ids)
                                _contest.Add(id);
                    }
                }
                return;
            }
            if (qty > _targetQty || qty + _suffix[index] < _targetQty)
                return;

            var item = _classes[index];
            for (var take = 0; take <= item.Ids.Length; take++)
            {
                if (_incomplete || _solutions >= _limits.StopAfterSolutions)
                    return;
                chosen[index] = take;
                Walk(index + 1, qty + item.Quantity * take, cash + item.Cash * take, count + take, partial || (take > 0 && take < item.Ids.Length), chosen);
            }
        }

        public SearchResult Result()
        {
            if (_solutions >= 2 || (_solutions == 1 && _partial && !_incomplete))
                return new SearchResult(SearchState.Contest, Ids(false), _contest.ToList());
            if (_incomplete)
                return new SearchResult(SearchState.Incomplete, _solutions == 1 ? Ids(false) : [], _contest.ToList());
            if (_solutions == 1)
                return new SearchResult(SearchState.Unique, Ids(false), Ids(false));
            return SearchResult.None();
        }

        private List<int> Ids(bool wholeClass)
        {
            var ids = new List<int>();
            for (var i = 0; i < _classes.Count && i < _chosen.Length; i++)
            {
                var take = _chosen[i];
                if (take == 0)
                    continue;
                if (wholeClass || take == _classes[i].Ids.Length)
                    ids.AddRange(_classes[i].Ids);
                else
                    ids.AddRange(_classes[i].Ids.Take(take));
            }
            return ids;
        }
    }

    private enum SearchState { None, Unique, Contest, Incomplete }

    private sealed record SearchResult(SearchState State, List<int> Items, List<int> ContestItems)
    {
        public static SearchResult None() => new(SearchState.None, [], []);
    }
}
