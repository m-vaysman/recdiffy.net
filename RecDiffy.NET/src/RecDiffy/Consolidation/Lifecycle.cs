using System.Collections.Immutable;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed record ConsolidatedRow(
    IReadOnlyList<SourceRef> Members,
    RowContext Context,
    bool Quarantined,
    SuppressionReason? Suppression,
    string? Code,
    bool Packaged = false,
    bool LifecycleCollapsed = false,
    IReadOnlyList<SourceRef>? Lineage = null);

internal sealed record LifecyclePlan(string ActionField, string TargetField, string ScopeField, string CorrPointsAt);

internal enum AggregateKind
{
    Same,
    Sum,
    FromRole,
    DifferenceByRole,
}

internal sealed record FieldAggregate(string Field, AggregateKind Kind, string? Role, string? OtherRole);

internal sealed record PackagePlan(
    string LinkField,
    string RoleField,
    IReadOnlyList<string> RequiredRoles,
    IReadOnlyList<FieldAggregate> Aggregates);

internal static class Consolidation
{
    public static IReadOnlyList<ConsolidatedRow> Run(
        IReadOnlyList<IntakeRow> rows,
        LifecyclePlan? lifecycle,
        PackagePlan? package)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var collapsed = lifecycle is null ? [.. rows.Select(PassThrough)] : Collapse(rows, lifecycle);
        return package is null ? collapsed : Packages.Pack(collapsed, package);
    }

    private static ConsolidatedRow PassThrough(IntakeRow row) =>
        new(row.Members, row.Context, row.Quarantined, null, row.Quarantined ? "DuplicateConflict" : null);

    private static IReadOnlyList<ConsolidatedRow> Collapse(IReadOnlyList<IntakeRow> rows, LifecyclePlan plan)
    {
        var events = rows.Select(row => ToEvent(row, plan)).OfType<LifeEvent>().ToArray();
        var index = events.ToDictionary(evt => (evt.Scope, evt.Id));
        var linked = Components(events, index);
        var decided = linked.SelectMany(component => Decide(component, index, plan.CorrPointsAt)).ToDictionary(row => row.Context.RowKey);
        return [.. rows.Select(row => decided.TryGetValue(row.Context.RowKey, out var fate) ? fate : PassThrough(row))];
    }

    private static LifeEvent? ToEvent(IntakeRow row, LifecyclePlan plan) =>
        row.Context.Display(plan.ActionField) is { } action
            ? new LifeEvent(
                row,
                row.Context.Display(plan.ScopeField) ?? "",
                row.Context.RowKey,
                action,
                row.Context.Display(plan.TargetField))
            : null;

    private static IEnumerable<IReadOnlyList<LifeEvent>> Components(
        IReadOnlyList<LifeEvent> events,
        IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index) =>
        events.Aggregate(
            (Seen: ImmutableHashSet.Create<string>(StringComparer.Ordinal), Groups: ImmutableList<IReadOnlyList<LifeEvent>>.Empty),
            (state, evt) => state.Seen.Contains(evt.Key) ? state : Add(state, evt, index)).Groups;

    private static (ImmutableHashSet<string> Seen, ImmutableList<IReadOnlyList<LifeEvent>> Groups) Add(
        (ImmutableHashSet<string> Seen, ImmutableList<IReadOnlyList<LifeEvent>> Groups) state,
        LifeEvent start,
        IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index)
    {
        var keys = Reachable([start.Key], [], index);
        var members = keys.Select(key => index[(ScopeOf(key), IdOf(key))]).DistinctBy(evt => evt.Key).ToArray();
        return (state.Seen.Union(keys), state.Groups.Add(members));
    }

    private static ImmutableHashSet<string> Reachable(
        string[] pending,
        ImmutableHashSet<string> seen,
        IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index) =>
        pending.Length is 0
            ? seen
            : Reachable(
                [.. pending.SelectMany(key => Links(key, index)).Where(key => seen.Contains(key) is false && pending.Contains(key) is false).Distinct()],
                seen.Union(pending),
                index);

    private static IEnumerable<string> Links(string key, IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index)
    {
        var scope = ScopeOf(key);
        var id = IdOf(key);
        var outbound = index.TryGetValue((scope, id), out var evt) && evt.Target is { } target && index.ContainsKey((scope, target))
            ? [Key(scope, target)]
            : Array.Empty<string>();
        var inbound = index.Values.Where(other => other.Scope == scope && other.Target == id).Select(other => other.Key);
        return [key, .. outbound, .. inbound];
    }

    private static IReadOnlyList<ConsolidatedRow> Decide(
        IReadOnlyList<LifeEvent> members,
        IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index,
        string corrPointsAt) =>
        members switch
        {
            [] => [],
            _ when members.Any(member => member.Row.Quarantined) => Mark(members, true, null, "ChainMemberQuarantined"),
            _ when members.Any(member => member.Target is not null && index.ContainsKey((member.Scope, member.Target)) is false) =>
                Mark(members, true, null, "MissingLifecycleTarget"),
            _ when HasCycle(members, index) => Mark(members, true, null, "LifecycleCycle"),
            _ => Survivors(members, index, corrPointsAt) switch
            {
                [] => Mark(members, false, SuppressionReason.FullyCancelled, null),
                [var survivor] => Assign(members, survivor.Id, index, corrPointsAt),
                _ => Mark(members, true, null, "LifecycleConflict"),
            },
        };

    private static IReadOnlyList<LifeEvent> Survivors(
        IReadOnlyList<LifeEvent> members,
        IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index,
        string corrPointsAt)
    {
        var cancelled = members.Where(member => member.Action is "CANC" && member.Target is not null).Select(member => member.Target!).ToHashSet(StringComparer.Ordinal);
        var superseded = members.Where(member => member.Action is "CORR").Select(member => Predecessor(member, index, corrPointsAt)).ToHashSet(StringComparer.Ordinal);
        return [.. members.Where(member => member.Action is not "CANC" && cancelled.Contains(member.Id) is false && superseded.Contains(member.Id) is false)];
    }

    private static string Predecessor(LifeEvent corr, IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index, string corrPointsAt) =>
        (corr.Target, corrPointsAt) switch
        {
            (null, _) => corr.Id,
            (var target, "CANC") when index.TryGetValue((corr.Scope, target), out var linked) && linked is { Action: "CANC", Target: { } cancelled } => cancelled,
            (var target, _) => target,
        };

    private static IReadOnlyList<ConsolidatedRow> Assign(
        IReadOnlyList<LifeEvent> members,
        string survivorId,
        IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index,
        string corrPointsAt) =>
        [.. members.Select(member => member.Id == survivorId
            ? Row(member, false, null, null) with { LifecycleCollapsed = true, Lineage = [.. members.SelectMany(item => item.Row.Members)] }
            : Row(member, false, member.Action is "CANC" ? SuppressionReason.CancelEvent : SuppressionReason.Superseded, null))];

    private static bool HasCycle(IReadOnlyList<LifeEvent> members, IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index) =>
        members.Any(member => Walk(member.Scope, member.Id, [], index));

    private static bool Walk(string scope, string id, ImmutableHashSet<string> stack, IReadOnlyDictionary<(string Scope, string Id), LifeEvent> index) =>
        index.TryGetValue((scope, id), out var evt) && evt.Target is { } target
            ? stack.Contains(target) || Walk(scope, target, stack.Add(id), index)
            : false;

    private static IReadOnlyList<ConsolidatedRow> Mark(IReadOnlyList<LifeEvent> members, bool quarantine, SuppressionReason? suppression, string? code) =>
        [.. members.Select(member => Row(member, quarantine, suppression, code))];

    private static ConsolidatedRow Row(LifeEvent evt, bool quarantine, SuppressionReason? suppression, string? code) =>
        new(evt.Row.Members, evt.Row.Context, quarantine, suppression, code);

    private static string Key(string scope, string id) => scope + "\u001f" + id;

    private static string ScopeOf(string key) => key.Split('\u001f')[0];

    private static string IdOf(string key) => key.Split('\u001f')[^1];

    private sealed record LifeEvent(IntakeRow Row, string Scope, string Id, string Action, string? Target)
    {
        public string Key => Consolidation.Key(Scope, Id);
    }
}
