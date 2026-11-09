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

    // A quarantined intake row keeps its own reason as its code, so result building adds no code the row does not
    // already report. Each diagnostic is written once, by the stage that owns it (spec v3 §3.4; #173).
    private static ConsolidatedRow PassThrough(IntakeRow row) =>
        new(row.Members, row.Context, row.Quarantined, null, row.Quarantined ? Reason(row.Context) : null);

    private static string? Reason(RowContext context) =>
        context.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid)?.Code;

    // Each row gets its own fate, keyed by its source ref: copies of one row key can differ in scope (#214).
    private static IReadOnlyList<ConsolidatedRow> Collapse(IReadOnlyList<IntakeRow> rows, LifecyclePlan plan)
    {
        var events = rows.Select(row => ToEvent(row, plan)).OfType<LifeEvent>().ToArray();
        var index = Copies(events);
        var linked = Components(events, index);
        var decided = linked.SelectMany(component => Decide(component, index, plan.CorrPointsAt)).ToDictionary(row => row.Members[0]);
        return [.. rows.Select(row => decided.TryGetValue(row.Members[0], out var fate) ? fate : PassThrough(row))];
    }

    // Every copy of an event is kept, and each copy's target links its chain. No copy stands for the others, so copy
    // order cannot pick which chain is quarantined (spec v3 §3.2; #214).
    private static IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> Copies(IReadOnlyList<LifeEvent> events) =>
        events.GroupBy(evt => (evt.Scope, evt.Id)).ToDictionary(group => group.Key, group => (IReadOnlyList<LifeEvent>)[.. group]);

    // A blank action is not an event. The row stays on the pass-through path.
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
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index) =>
        events.Aggregate(
            (Seen: ImmutableHashSet<LifeKey>.Empty, Groups: ImmutableList<IReadOnlyList<LifeEvent>>.Empty),
            (state, evt) => state.Seen.Contains(evt.Key) ? state : Add(state, evt, index)).Groups;

    private static (ImmutableHashSet<LifeKey> Seen, ImmutableList<IReadOnlyList<LifeEvent>> Groups) Add(
        (ImmutableHashSet<LifeKey> Seen, ImmutableList<IReadOnlyList<LifeEvent>> Groups) state,
        LifeEvent start,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index)
    {
        var keys = Reachable([start.Key], [], index);
        var members = keys.SelectMany(key => index[(key.Scope, key.Id)]).ToArray();
        return (state.Seen.Union(keys), state.Groups.Add(members));
    }

    private static ImmutableHashSet<LifeKey> Reachable(
        LifeKey[] pending,
        ImmutableHashSet<LifeKey> seen,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index) =>
        pending.Length is 0
            ? seen
            : Reachable(
                [.. pending.SelectMany(key => Links(key, index)).Where(key => seen.Contains(key) is false && pending.Contains(key) is false).Distinct()],
                seen.Union(pending),
                index);

    private static IEnumerable<LifeKey> Links(LifeKey key, IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index)
    {
        var outbound = index.TryGetValue((key.Scope, key.Id), out var copies)
            ? copies.Select(copy => copy.Target).OfType<string>().Where(target => index.ContainsKey((key.Scope, target))).Select(target => new LifeKey(key.Scope, target))
            : Array.Empty<LifeKey>();
        var inbound = index.Values.SelectMany(others => others).Where(other => other.Scope == key.Scope && other.Target == key.Id).Select(other => other.Key);
        return [key, .. outbound, .. inbound];
    }

    // A key with more than one copy is a conflict even when no copy is quarantined yet.
    private static IReadOnlyList<ConsolidatedRow> Decide(
        IReadOnlyList<LifeEvent> members,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index,
        string corrPointsAt) =>
        members switch
        {
            [] => [],
            _ when members.Any(member => member.Row.Quarantined || index[(member.Scope, member.Id)].Count > 1) => QuarantineChain(members),
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

    // A cancel is never a survivor. It removes its target; the cancel row itself is suppressed later as a cancel
    // event.
    private static IReadOnlyList<LifeEvent> Survivors(
        IReadOnlyList<LifeEvent> members,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index,
        string corrPointsAt)
    {
        var cancelled = members.Where(member => member.Action is "CANC" && member.Target is not null).Select(member => member.Target!).ToHashSet(StringComparer.Ordinal);
        var superseded = members.Where(member => member.Action is "CORR").Select(member => Predecessor(member, index, corrPointsAt)).ToHashSet(StringComparer.Ordinal);
        return [.. members.Where(member => member.Action is not "CANC" && cancelled.Contains(member.Id) is false && superseded.Contains(member.Id) is false)];
    }

    // When corrPointsAt is CANC and the named event is a cancel, the correction supersedes that cancel's target.
    private static string Predecessor(LifeEvent corr, IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index, string corrPointsAt) =>
        (corr.Target, corrPointsAt) switch
        {
            (null, _) => corr.Id,
            (var target, "CANC") when index.TryGetValue((corr.Scope, target), out var linked) && linked is [{ Action: "CANC", Target: { } cancelled }] => cancelled,
            (var target, _) => target,
        };

    // Only the survivor carries the lineage of every member.
    // A cancel is suppressed as a cancel event; every other non-survivor is superseded.
    private static IReadOnlyList<ConsolidatedRow> Assign(
        IReadOnlyList<LifeEvent> members,
        string survivorId,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index,
        string corrPointsAt) =>
        [.. members.Select(member => member.Id == survivorId
            ? Row(member, false, null, null) with { LifecycleCollapsed = true, Lineage = [.. members.SelectMany(item => item.Row.Members)] }
            : Row(member, false, member.Action is "CANC" ? SuppressionReason.CancelEvent : SuppressionReason.Superseded, null))];

    private static bool HasCycle(IReadOnlyList<LifeEvent> members, IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index) =>
        members.Any(member => Walk(member.Scope, member.Id, [], index));

    private static bool Walk(string scope, string id, ImmutableHashSet<string> stack, IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index) =>
        index.TryGetValue((scope, id), out var copies)
        && copies.Any(evt => evt.Target is { } target && (stack.Contains(target) || Walk(scope, target, stack.Add(id), index)));

    // A row already quarantined keeps its own reason, so a conflicting copy reports DuplicateConflict alone (spec v3
    // §3.4; #214). Every other row of the chain is ChainMemberQuarantined.
    private static IReadOnlyList<ConsolidatedRow> QuarantineChain(IReadOnlyList<LifeEvent> members) =>
        [.. members.Select(member => Row(member, true, null, (member.Row.Quarantined ? Reason(member.Row.Context) : null) ?? "ChainMemberQuarantined"))];

    private static IReadOnlyList<ConsolidatedRow> Mark(IReadOnlyList<LifeEvent> members, bool quarantine, SuppressionReason? suppression, string? code) =>
        [.. members.Select(member => Row(member, quarantine, suppression, code))];

    private static ConsolidatedRow Row(LifeEvent evt, bool quarantine, SuppressionReason? suppression, string? code) =>
        new(evt.Row.Members, evt.Row.Context, quarantine, suppression, code);

    private readonly record struct LifeKey(string Scope, string Id);

    private sealed record LifeEvent(IntakeRow Row, string Scope, string Id, string Action, string? Target)
    {
        public LifeKey Key => new(Scope, Id);
    }
}
