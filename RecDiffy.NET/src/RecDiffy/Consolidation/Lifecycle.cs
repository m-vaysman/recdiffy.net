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
    IReadOnlyList<SourceRef>? Lineage = null,
    IReadOnlyDictionary<SourceRef, ImmutableList<Diagnostic>>? MemberDiagnostics = null);

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

    // A chain is a linked component of the event graph: each event links to its target and to every event that names it.
    // The events that name each key are looked up once, and a loop with an explicit stack walks each chain, so the work
    // grows with the event count and a chain of any length cannot overflow the call stack (spec v3 §3.1, §12; #222).
    // The sets and the stack are local to this call; the result is a new list of chains.
    private static IReadOnlyList<IReadOnlyList<LifeEvent>> Components(
        IReadOnlyList<LifeEvent> events,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index)
    {
        var inbound = events.Where(evt => evt.Target is not null).ToLookup(evt => (evt.Scope, evt.Target!), evt => evt.Key);
        var seen = new HashSet<LifeKey>();
        var chains = new List<IReadOnlyList<LifeEvent>>();
        foreach (var start in events)
        {
            if (seen.Add(start.Key) is false)
                continue;
            var keys = new List<LifeKey>();
            var pending = new Stack<LifeKey>([start.Key]);
            while (pending.TryPop(out var key))
            {
                keys.Add(key);
                foreach (var next in Links(key, index, inbound))
                {
                    if (seen.Add(next))
                        pending.Push(next);
                }
            }

            chains.Add([.. keys.SelectMany(key => index[(key.Scope, key.Id)])]);
        }

        return chains;
    }

    private static IEnumerable<LifeKey> Links(
        LifeKey key,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index,
        ILookup<(string Scope, string Id), LifeKey> inbound) =>
        index[(key.Scope, key.Id)]
            .Select(copy => copy.Target)
            .OfType<string>()
            .Where(target => index.ContainsKey((key.Scope, target)))
            .Select(target => new LifeKey(key.Scope, target))
            .Concat(inbound[(key.Scope, key.Id)]);

    // A key with more than one copy is a conflict even when no copy is quarantined yet.
    private static IReadOnlyList<ConsolidatedRow> Decide(
        IReadOnlyList<LifeEvent> members,
        IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index,
        string corrPointsAt) =>
        members switch
        {
            [] => [],
            _ when members.Any(member => member.Row.Quarantined || index[(member.Scope, member.Id)].Count > 1) => QuarantineChain(members),
            _ when members.Any(member => MissingTarget(member, index)) => Mark(members, true, null, "MissingLifecycleTarget"),
            _ when HasCycle(members) => Mark(members, true, null, "LifecycleCycle"),
            _ => Survivors(members, index, corrPointsAt) switch
            {
                [] => Mark(members, false, SuppressionReason.FullyCancelled, null),
                [var survivor] => Assign(members, survivor.Id, index, corrPointsAt),
                _ => Mark(members, true, null, "LifecycleConflict"),
            },
        };

    // A cancel or a correction must name an event that exists. One that names none has nothing to cancel or supersede,
    // so it is not read as cancelling itself (spec v3 §5.1 rule 1; #216).
    private static bool MissingTarget(LifeEvent member, IReadOnlyDictionary<(string Scope, string Id), IReadOnlyList<LifeEvent>> index) =>
        member switch
        {
            { Action: "CANC" or "CORR", Target: null } => true,
            { Target: { } target } => index.ContainsKey((member.Scope, target)) is false,
            _ => false,
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

    // Decide reaches this only when every key of the chain has one event and every named target exists. Each event names
    // at most one target, so a linked chain of n events has n - 1 links unless its links close a loop: it has a cycle
    // exactly when every event names a target. No walk is needed, so a long chain costs one pass (#222).
    private static bool HasCycle(IReadOnlyList<LifeEvent> members) =>
        members.All(member => member.Target is not null);

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
