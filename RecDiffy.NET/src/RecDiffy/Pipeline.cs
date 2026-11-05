using System.Security.Cryptography;
using System.Text;

namespace RecDiffy.Engine;

internal static class Pipeline
{
    private static readonly string[] InstrumentColumns = ["ISIN", "CUSIP", "SEDOL", "Ticker"];

    public static ReconciliationResult Run(
        CompiledProfile profile,
        RecordSource left,
        RecordSource right,
        ReferenceDataSet referenceData,
        RunOptions options,
        CancellationToken cancellation)
    {
        using var activity = RecDiffyTelemetry.Activity.StartActivity("reconcile");
        var leftRows = left.Materialize(profile.Model.LeftSource, profile.Model.LeftKey, cancellation);
        var rightRows = right.Materialize(profile.Model.RightSource, profile.Model.RightKey, cancellation);
        return RunMaterialized(profile, leftRows, rightRows, referenceData, options, cancellation);
    }

    public static ReconciliationResult RunMaterialized(
        CompiledProfile profile,
        IReadOnlyList<RawRow> leftRows,
        IReadOnlyList<RawRow> rightRows,
        ReferenceDataSet referenceData,
        RunOptions options,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var state = new RunState
        {
            Profile = profile,
            Reference = referenceData,
            Pool = new InternPool(),
            Instruments = BuildMaster(leftRows),
            Midpoint = profile.Model.Midpoint,
        };
        var left = Intake(state, leftRows, Side.Left, profile.Model.MappedColumnsLeft);
        var right = Intake(state, rightRows, Side.Right, profile.Model.MappedColumnsRight);
        cancellation.ThrowIfCancellationRequested();
        Normalize(state, left, profile.LeftRules, options, cancellation);
        Normalize(state, right, profile.RightRules, options, cancellation);
        Route(state, left.Concat(right));
        cancellation.ThrowIfCancellationRequested();
        var leftItems = Consolidate(state, left, profile.LeftLife, profile.LeftPackages);
        var rightItems = Consolidate(state, right, profile.RightLife, profile.RightPackages);
        var items = leftItems.Concat(rightItems).Select((item, index) =>
        {
            item.GetType();
            return item;
        }).ToList();
        for (var i = 0; i < items.Count; i++)
            items[i] = Relabel(items[i], i);
        cancellation.ThrowIfCancellationRequested();
        var matched = Cascade.Match(items, state, cancellation);
        return Finish(state, items, matched.Groups, matched.Contests, matched.Incomplete, left.Count, right.Count);
    }

    private static InstrumentMaster BuildMaster(IReadOnlyList<RawRow> leftRows)
    {
        var master = new InstrumentMaster();
        foreach (var row in leftRows)
        {
            string? canonical = null;
            foreach (var column in new[] { "ISIN", "Ticker" })
            {
                if (row.TryGet(column, out var value) && !string.IsNullOrWhiteSpace(value.Text))
                {
                    canonical = value.Text.Trim();
                    break;
                }
            }
            if (canonical is null)
                continue;
            foreach (var column in InstrumentColumns)
            {
                if (!row.TryGet(column, out var value) || string.IsNullOrWhiteSpace(value.Text))
                    continue;
                var scheme = column == "Ticker" ? "TICKER" : column;
                master.Add(scheme, value.Text.Trim(), canonical);
            }
        }
        return master;
    }

    private static List<MutableRow> Intake(RunState state, IReadOnlyList<RawRow> rows, Side side, IReadOnlyList<string> columns)
    {
        var grouped = rows.GroupBy(row => row.Ref.RowKey, StringComparer.Ordinal);
        var result = new List<MutableRow>();
        foreach (var group in grouped)
        {
            var copies = group.ToList();
            var fingerprints = copies.Select(row => row.Fingerprint(columns)).Distinct(StringComparer.Ordinal).ToList();
            if (copies.Count > 1 && fingerprints.Count > 1)
            {
                foreach (var copy in copies)
                {
                    var bad = NewRow(state, side, copy);
                    bad.Quarantined = true;
                    bad.Diagnostics.Add(new StoredDiagnostic("DuplicateConflict", "intake", null, copy.Ref.RowKey, DiagnosticSeverity.Invalid));
                    result.Add(bad);
                }
                continue;
            }
            var row = NewRow(state, side, copies[0]);
            row.Members.Clear();
            foreach (var copy in copies)
            {
                row.Members.Add(copy.Ref);
                state.ByRef[(copy.Ref.Source, copy.Ref.RowKey)] = row;
            }
            if (copies.Count > 1)
                row.Diagnostics.Add(new StoredDiagnostic("DuplicateRowCollapsed", "intake", null, copies.Count.ToString(CultureInfo.InvariantCulture), DiagnosticSeverity.Warning));
            result.Add(row);
        }
        return result;
    }

    private static MutableRow NewRow(RunState state, Side side, RawRow raw)
    {
        var row = new MutableRow { Side = side, Primary = raw.Ref, Raw = raw };
        row.Members.Add(raw.Ref);
        state.ByRef[(raw.Ref.Source, raw.Ref.RowKey)] = row;
        if (side == Side.Left)
            state.Left.Add(row);
        else
            state.Right.Add(row);
        return row;
    }

    private static void Normalize(RunState state, List<MutableRow> rows, IReadOnlyList<RuleUse> rules, RunOptions options, CancellationToken cancellation)
    {
        var degree = options.MaxDegreeOfParallelism ?? Environment.ProcessorCount;
        if (degree <= 1 || rows.Count < 32)
        {
            foreach (var row in rows)
                Apply(state, row, rules, cancellation);
            return;
        }
        Parallel.ForEach(rows, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellation }, row => Apply(state, row, rules, cancellation));
    }

    private static void Apply(RunState state, MutableRow row, IReadOnlyList<RuleUse> rules, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (row.Quarantined)
            return;
        var context = new RowContext(state, row);
        var isCancel = row.Raw.TryGet("Trans_Lifecycle_Action", out var action) && action.Text == "CANC";
        foreach (var use in rules)
        {
            if (isCancel && use.Rule.Phase != RulePhase.Identify)
                continue;
            if (!WhenEvaluator.Matches(use.When, row, state.Pool))
                continue;
            context.RuleId = use.Rule.RuleId;
            use.Rule.Apply(ref context);
        }
        if (row.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Invalid))
            row.Quarantined = true;
    }

    private static void Route(RunState state, IEnumerable<MutableRow> rows)
    {
        foreach (var row in rows)
        {
            foreach (var path in state.Profile.Model.Paths)
            {
                if (path.Route == "route.all")
                    row.PathId = path.Id;
                else if (path.RouteField is not null && WhenEvaluator.Read(row, path.RouteField, state.Pool) == path.RouteEquals)
                    row.PathId = path.Id;
            }
            row.PathId ??= state.Profile.Model.Paths.FirstOrDefault()?.Id ?? "default";
        }
    }

    private static List<MatchItem> Consolidate(RunState state, List<MutableRow> rows, LifecycleBinding? life, IReadOnlyList<PackageBinding> packages)
    {
        if (life is not null)
            ApplyLife(state, rows, life);
        var active = rows.Where(row => !row.Quarantined && row.Suppression is null).ToList();
        var consumed = new HashSet<SourceRef>();
        var items = new List<MatchItem>();
        if (packages.Count > 0)
        {
            var linked = active.Where(row => Link(state, row) is not null).GroupBy(row => Link(state, row)!, StringComparer.Ordinal);
            foreach (var group in linked)
            {
                var members = group.ToList();
                var roles = members.Select(row => WhenEvaluator.Read(row, "LegRole", state.Pool) ?? "").ToHashSet(StringComparer.Ordinal);
                var plan = packages.FirstOrDefault(binding => binding.RequiredRoles.Any(roles.Contains));
                if (plan is null)
                    continue;
                if (plan.RequiredRoles.Any(role => !roles.Contains(role)))
                {
                    Quarantine(members, "IncompletePackage", plan.RuleId);
                    foreach (var member in members)
                        consumed.Add(member.Primary);
                    continue;
                }
                var target = new MutableRow { Side = members[0].Side, Primary = members[0].Primary, Raw = members[0].Raw, PathId = members[0].PathId };
                var fallbackRole = plan.RequiredRoles.FirstOrDefault() ?? "";
                foreach (var field in state.Profile.Model.Fields)
                {
                    IAggregator aggregator = plan.Aggregates.TryGetValue(field.Name, out var chosen)
                        ? chosen
                        : new RoleFallbackAggregator(fallbackRole);
                    aggregator.Apply(field.Name, members, target, state, out var failure);
                    if (failure is not null)
                    {
                        Quarantine(members, failure, plan.RuleId);
                        target = null;
                        break;
                    }
                }
                if (target is null)
                {
                    foreach (var member in members)
                        consumed.Add(member.Primary);
                    continue;
                }
                items.Add(new MatchItem
                {
                    Id = 0,
                    Side = target.Side,
                    PathId = target.PathId ?? "default",
                    Row = target,
                    Sources = members.SelectMany(member => member.Members).ToList(),
                    Package = true,
                });
                foreach (var member in members)
                    consumed.Add(member.Primary);
            }
        }

        foreach (var row in active)
        {
            if (consumed.Contains(row.Primary))
                continue;
            items.Add(new MatchItem
            {
                Id = 0,
                Side = row.Side,
                PathId = row.PathId ?? "default",
                Row = row,
                Sources = row.Members.ToList(),
                Lifecycle = row.ChainCollapsed,
            });
        }
        return items;
    }

    private static void ApplyLife(RunState state, List<MutableRow> rows, LifecycleBinding life)
    {
        var events = new List<LifeEvent>();
        foreach (var row in rows.Where(row => !row.Quarantined))
        {
            var action = WhenEvaluator.Read(row, "EventAction", state.Pool);
            var id = WhenEvaluator.Read(row, "EventId", state.Pool);
            if (action is null || id is null)
                continue;
            events.Add(new LifeEvent(row.Primary, WhenEvaluator.Read(row, "Counterparty", state.Pool) ?? "", id, action, WhenEvaluator.Read(row, "EventTarget", state.Pool)));
        }
        foreach (var decision in (life.Apply ?? ((_, _) => [])).Invoke(events, life.CorrPointsAt))
        {
            if (!state.ByRef.TryGetValue((decision.Source.Source, decision.Source.RowKey), out var row))
                continue;
            switch (decision.Fate)
            {
                case LifeFate.Survivor:
                    row.ChainCollapsed = decision.Collapsed;
                    break;
                case LifeFate.Superseded:
                    row.Suppression = SuppressionReason.Superseded;
                    row.ChainSurvivor = decision.LinkedTo;
                    break;
                case LifeFate.CancelEvent:
                    row.Suppression = SuppressionReason.CancelEvent;
                    row.ChainSurvivor = decision.LinkedTo;
                    break;
                case LifeFate.FullyCancelled:
                    row.Suppression = SuppressionReason.FullyCancelled;
                    break;
                case LifeFate.Quarantine:
                    row.Quarantined = true;
                    row.Diagnostics.Add(new StoredDiagnostic(decision.Code ?? "LifecycleConflict", life.RuleId, null, null, DiagnosticSeverity.Invalid));
                    break;
            }
        }
    }

    private static string? Link(RunState state, MutableRow row) => WhenEvaluator.Read(row, "PackageRef", state.Pool);

    private static void Quarantine(IEnumerable<MutableRow> rows, string code, string rule)
    {
        foreach (var row in rows)
        {
            row.Quarantined = true;
            row.Diagnostics.Add(new StoredDiagnostic(code, rule, null, null, DiagnosticSeverity.Invalid));
        }
    }

    private static MatchItem Relabel(MatchItem item, int id) => new()
    {
        Id = id,
        Side = item.Side,
        PathId = item.PathId,
        Row = item.Row,
        Sources = item.Sources,
        Package = item.Package,
        Lifecycle = item.Lifecycle,
        Consumed = item.Consumed,
    };

    private static ReconciliationResult Finish(
        RunState state,
        List<MatchItem> items,
        List<MatchGroup> groups,
        List<HashSet<int>> contests,
        List<(HashSet<int> Items, string Code)> incomplete,
        int leftCount,
        int rightCount)
    {
        var liveContests = contests
            .Select(set => set.Where(id => !items[id].Consumed).ToHashSet())
            .Where(set => set.Count > 1)
            .ToList();
        var residueContests = ResidueContests(state, items);
        foreach (var contest in residueContests)
            liveContests.Add(contest);
        var contestIds = liveContests.SelectMany(set => set).ToHashSet();
        var incompleteIds = incomplete.SelectMany(set => set.Items).Where(id => !items[id].Consumed && !contestIds.Contains(id)).ToHashSet();

        var entries = new List<ResultEntry>();
        var projections = new Dictionary<GroupId, (Dictionary<string, CanonicalValue> Left, Dictionary<string, CanonicalValue> Right)>();
        var attributions = new Dictionary<string, List<string>>();
        var nextAttr = 1;
        var commits = groups.GroupBy(group => group.PassId ?? "").ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var diff = Differ.Diff(state, group, attributions, ref nextAttr);
            var id = GroupIdOf(group.Left.SelectMany(item => item.Sources).Concat(group.Right.SelectMany(item => item.Sources)));
            var composition = CompositionOf(group);
            entries.Add(new ResultEntry(id, group.Left.Concat(group.Right).First().PathId, Alignment.Matched, composition, diff.Outcome, diff.Root, Sources(group.Left), Sources(group.Right), diff.Diffs, Explain(state, group, items)));
            projections[id] = (diff.Left, diff.Right);
        }

        foreach (var set in Distinct(liveContests))
            entries.Add(OpenEntry(state, items, set, Alignment.Ambiguous, "Contest"));
        foreach (var set in incomplete.Where(set => set.Items.Any(id => incompleteIds.Contains(id))))
        {
            var ids = set.Items.Where(incompleteIds.Contains).ToHashSet();
            if (ids.Count == 0)
                continue;
            entries.Add(OpenEntry(state, items, ids, Alignment.SearchIncomplete, set.Code));
        }

        var entryBySource = new Dictionary<(string, string), GroupId>();
        foreach (var entry in entries)
        {
            foreach (var source in entry.Left.Concat(entry.Right))
                entryBySource[(source.Source, source.RowKey)] = entry.Id;
        }
        foreach (var item in items.Where(item => item.Lifecycle))
        {
            if (item.Sources.Count == 0 || !entryBySource.TryGetValue((item.Sources[0].Source, item.Sources[0].RowKey), out var entry))
                continue;
            foreach (var source in item.Sources)
                entryBySource[(source.Source, source.RowKey)] = entry;
        }
        LinkSuppressed(state, entryBySource);

        var unmatched = items.Where(item => !item.Consumed && !contestIds.Contains(item.Id) && !incompleteIds.Contains(item.Id)).ToList();
        foreach (var item in unmatched)
        {
            var alignment = item.Side == Side.Left ? Alignment.LeftOnly : Alignment.RightOnly;
            var id = GroupIdOf(item.Sources);
            entries.Add(new ResultEntry(id, item.PathId, alignment, Composition.None, FieldOutcome.NotCompared, null, item.Side == Side.Left ? item.Sources : [], item.Side == Side.Right ? item.Sources : [], [], Explain(state, item)));
            foreach (var source in item.Sources)
                entryBySource[(source.Source, source.RowKey)] = id;
            projections[id] = (new Dictionary<string, CanonicalValue>(), new Dictionary<string, CanonicalValue>());
        }

        var entryById = entries.ToDictionary(entry => entry.Id);
        var rows = state.ByRef.Select(pair => ToRow(pair.Value, pair.Key.Item2, entryBySource, entryById)).OrderBy(row => row.Row.Source, StringComparer.Ordinal).ThenBy(row => row.Row.RowKey, StringComparer.Ordinal).ToList();
        var accounted = rows.Count;
        var input = state.ByRef.Count;
        if (accounted != input)
            throw new InvalidOperationException($"Row accounting failed: {accounted} dispositions for {input} rows.");

        var roots = entries.Where(entry => entry.Alignment == Alignment.Matched && entry.Outcome == FieldOutcome.Break && entry.RootField is not null)
            .GroupBy(entry => entry.RootField!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var summary = new RunSummary(
            leftCount,
            rightCount,
            entries.Count(entry => entry.Alignment == Alignment.Matched),
            entries.Count(entry => entry.Outcome == FieldOutcome.Exact),
            entries.Count(entry => entry.Outcome == FieldOutcome.WithinTolerance),
            entries.Count(entry => entry.Outcome == FieldOutcome.Break),
            entries.Count(entry => entry.Alignment == Alignment.LeftOnly),
            entries.Count(entry => entry.Alignment == Alignment.RightOnly),
            entries.Count(entry => entry.Alignment == Alignment.Ambiguous),
            entries.Count(entry => entry.Alignment == Alignment.SearchIncomplete),
            rows.Count(row => row.Disposition == Disposition.Quarantined),
            rows.Count(row => row.Disposition == Disposition.Suppressed),
            commits,
            roots);
        var info = new RunInfo(state.Profile.Id, state.Profile.Hash, state.Profile.Version, state.Reference.Id, state.Reference.ContentHash, EngineVersion.Value);
        return new ReconciliationResult(info, entries, rows, summary, state, attributions, projections);
    }

    private sealed class RoleFallbackAggregator : IAggregator
    {
        private readonly string _role;
        public RoleFallbackAggregator(string role) => _role = role;
        public void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure)
        {
            failure = null;
            var chosen = members.FirstOrDefault(row => WhenEvaluator.Read(row, "LegRole", state.Pool) == _role) ?? members[0];
            if (chosen.Values.TryGetValue(field, out var value))
                target.Values[field] = value;
        }
    }

    private sealed class SameValueAggregator : IAggregator
    {
        public static readonly SameValueAggregator Instance = new();
        public void Apply(string field, IReadOnlyList<MutableRow> members, MutableRow target, RunState state, out string? failure)
        {
            failure = null;
            CanonicalValue? first = null;
            foreach (var member in members)
            {
                member.Values.TryGetValue(field, out var value);
                if (first is null)
                {
                    first = value;
                    continue;
                }
                if (first.Value.IsNull != value.IsNull || first.Value.Number != value.Number || first.Value.Date != value.Date || first.Value.Interned != value.Interned)
                {
                    failure = "PackageInconsistent";
                    return;
                }
            }
            if (first is CanonicalValue chosen)
                target.Values[field] = chosen;
        }
    }

    private static void LinkSuppressed(RunState state, Dictionary<(string, string), GroupId> entryBySource)
    {
        foreach (var row in state.ByRef.Values.Where(row => row.ChainSurvivor is not null))
        {
            var survivor = row.ChainSurvivor!.Value;
            if (entryBySource.TryGetValue((survivor.Source, survivor.RowKey), out var id))
                entryBySource[(row.Primary.Source, row.Primary.RowKey)] = id;
        }
    }

    private static List<HashSet<int>> ResidueContests(RunState state, List<MatchItem> items)
    {
        var result = new List<HashSet<int>>();
        foreach (var path in state.Profile.Model.Paths)
        {
            foreach (var pass in path.Passes.Where(pass => pass.Kind != "match.groupSum"))
            {
                var active = items.Where(item => !item.Consumed && item.PathId == path.Id).ToList();
                var buckets = new Dictionary<string, List<MatchItem>>(StringComparer.Ordinal);
                foreach (var item in active)
                {
                    if (!item.Row.Values.ContainsKey(pass.Key.FirstOrDefault() ?? "Counterparty"))
                        continue;
                }
                _ = buckets;
            }
        }
        var parent = items.Where(item => !item.Consumed && item.Verdicts.Values.Any(v => v.Verdict == PassVerdict.Contest)).Select(item => item.Id).ToHashSet();
        if (parent.Count > 1)
            result.Add(parent);
        return result;
    }

    private static ResultEntry OpenEntry(RunState state, List<MatchItem> items, HashSet<int> ids, Alignment alignment, string detail)
    {
        var chosen = ids.Select(id => items[id]).ToList();
        var left = chosen.Where(item => item.Side == Side.Left).SelectMany(item => item.Sources).ToList();
        var right = chosen.Where(item => item.Side == Side.Right).SelectMany(item => item.Sources).ToList();
        var id = GroupIdOf(left.Concat(right));
        return new ResultEntry(id, chosen[0].PathId, alignment, Composition.None, FieldOutcome.NotCompared, null, left, right, [], new Explanation(null, [], [new Hint(detail, detail)]));
    }

    private static IEnumerable<HashSet<int>> Distinct(List<HashSet<int>> sets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            var key = string.Join(",", set.Order());
            if (seen.Add(key))
                yield return set;
        }
    }

    private static RowResult ToRow(MutableRow row, string rowKey, Dictionary<(string, string), GroupId> entries, Dictionary<GroupId, ResultEntry> entryById)
    {
        var source = new SourceRef(row.Primary.Source, rowKey, row.Primary.Ordinal);
        entries.TryGetValue((source.Source, source.RowKey), out var entryId);
        var linked = entryId.Value is not null && entryById.TryGetValue(entryId, out var entry) ? entry : null;
        var disposition = row.Quarantined
            ? Disposition.Quarantined
            : row.Suppression is not null
                ? Disposition.Suppressed
                : linked?.Alignment switch
                {
                    Alignment.Matched => Disposition.Matched,
                    Alignment.Ambiguous => Disposition.Ambiguous,
                    Alignment.SearchIncomplete => Disposition.SearchIncomplete,
                    Alignment.LeftOnly => Disposition.LeftOnly,
                    Alignment.RightOnly => Disposition.RightOnly,
                    _ => row.Side == Side.Left ? Disposition.LeftOnly : Disposition.RightOnly,
                };
        var validity = row.Quarantined
            ? InputValidity.Invalid
            : row.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning) ? InputValidity.ValidWithWarnings : InputValidity.Valid;
        return new RowResult(source, disposition, row.Suppression, validity, row.Diagnostics.Select(d => d.ToPublic()).ToArray(), linked is null ? null : entryId);
    }

    private static Composition CompositionOf(MatchGroup group)
    {
        var left = group.Left.Sum(item => item.Sources.Count);
        var right = group.Right.Sum(item => item.Sources.Count);
        var composition = left > 1 ? Composition.ManyToOne : right > 1 ? Composition.OneToMany : Composition.OneToOne;
        if (group.Left.Concat(group.Right).Any(item => item.Package))
            composition |= Composition.Package;
        if (group.Left.Concat(group.Right).Any(item => item.Lifecycle))
            composition |= Composition.LifecycleCollapsed;
        return composition;
    }

    private static List<SourceRef> Sources(IEnumerable<MatchItem> items) => items.SelectMany(item => item.Sources).ToList();

    private static GroupId GroupIdOf(IEnumerable<SourceRef> sources)
    {
        var text = string.Join("\n", sources.Select(source => source.Source + "\0" + source.RowKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new GroupId(Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant());
    }

    private static Explanation Explain(RunState state, MatchGroup group, List<MatchItem> items)
    {
        var sample = group.Left.Concat(group.Right).First();
        var trail = sample.Verdicts.Select(pair => new PassAttempt(pair.Key, pair.Value.Verdict, pair.Value.Key, [], null)).ToList();
        return new Explanation(group.PassId, trail, []);
    }

    private static Explanation Explain(RunState state, MatchItem item)
    {
        var trail = item.Verdicts.Select(pair => new PassAttempt(pair.Key, pair.Value.Verdict, pair.Value.Key, [], null)).ToList();
        return new Explanation(null, trail, Hints(state, item));
    }

    private static List<Hint> Hints(RunState state, MatchItem item)
    {
        if (!item.Row.Values.TryGetValue("Instrument", out var instrument) || instrument.IsNull)
            return [];
        var instrumentText = state.Pool.Id(instrument.Interned).Value;
        var party = item.Row.Values.TryGetValue("Counterparty", out var partyValue) && !partyValue.IsNull ? state.Pool.Id(partyValue.Interned).Value : "";
        var other = (item.Side == Side.Left ? state.Right : state.Left)
            .FirstOrDefault(row => !row.Quarantined && row.Suppression is null && WhenEvaluator.Read(row, "Instrument", state.Pool) == instrumentText && WhenEvaluator.Read(row, "Counterparty", state.Pool) != party);
        return other is null ? [] : [new Hint("NearestCandidate", other.Primary.RowKey)];
    }
}
