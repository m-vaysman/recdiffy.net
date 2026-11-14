using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class ResultBuilder
{
    // A degree below 2, or a single job, stays on the caller. Parallel starts at two jobs.
    public static IReconciliationResult Build(
        CompiledProfile profile,
        ReferenceDataSet reference,
        IReadOnlyList<(PathDeclaration Path, MatchReport Match)> routed,
        IReadOnlyList<ConsolidatedRow> left,
        IReadOnlyList<ConsolidatedRow> right,
        RunOptions options,
        CancellationToken cancellation)
    {
        var jobs = routed.SelectMany(item => item.Match.Committed.Select(group => (item.Path, Group: group))).ToArray();
        var use = FieldUse.Of(profile.Document);
        var built = new (ResultEntry Entry, RowContext Left, RowContext Right)[jobs.Length];
        var degree = options.MaxDegreeOfParallelism is int chosen and > 1 ? chosen : 1;
        if (degree == 1 || jobs.Length < 2)
        {
            for (var index = 0; index < jobs.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var plan = DiffPlan.From(jobs[index].Path, profile.Document.Fields, profile.Document.Midpoint, profile.Attributions);
                built[index] = Matched(jobs[index].Path, jobs[index].Group, plan, EqualByConstruction(jobs[index].Path, jobs[index].Group.PassId), use);
            }
        }
        else
        {
            Parallel.For(
                0,
                jobs.Length,
                new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellation },
                index =>
                {
                    var plan = DiffPlan.From(jobs[index].Path, profile.Document.Fields, profile.Document.Midpoint, profile.Attributions);
                    built[index] = Matched(jobs[index].Path, jobs[index].Group, plan, EqualByConstruction(jobs[index].Path, jobs[index].Group.PassId), use);
                });
        }

        var entries = new List<ResultEntry>();
        var projections = new Dictionary<GroupId, (RowContext Left, RowContext Right)>();
        foreach (var item in built)
        {
            entries.Add(item.Entry);
            projections[item.Entry.Id] = (item.Left, item.Right);
        }

        foreach (var item in routed)
        {
            foreach (var cluster in item.Match.Residue)
                entries.Add(Open(item.Path.Id, cluster, item.Path.Passes, use));
        }

        entries.AddRange(Unrouted(entries, left.Concat(right)));
        var entryByRow = Index(entries, left.Concat(right).ToArray());
        var entryById = entries.ToDictionary(entry => entry.Id);
        var rows = left.Concat(right).SelectMany(row => row.Members.Select(member => Row(row, member, entryByRow, entryById, projections))).ToImmutableArray();
        var summary = Summarize(entries, rows);
        return new ReconciliationResult(
            new RunInfo(profile.Hash, reference.Id, reference.ContentHash, EngineVersion()) { Reading = profile.Reading },
            entries.ToImmutableArray(),
            rows,
            summary,
            projections,
            left.Concat(right).SelectMany(row => row.Members.Select(member => (member, row.Context))).ToDictionary(pair => pair.member, pair => pair.Context));
    }

    // An empty diff list is Exact. NotCompared fields are absent from that list and do not lower the outcome.
    private static (ResultEntry Entry, RowContext Left, RowContext Right) Matched(PathDeclaration path, CommittedMatch group, DiffPlan plan, IReadOnlySet<string> equal, FieldUse use)
    {
        var left = Aggregate(group.Left, plan, equal, use);
        var right = Aggregate(group.Right, plan, equal, use);
        var diffs = FieldDiffer.Compare(left, right, plan);
        var outcome = diffs.Any(diff => diff.Outcome == FieldOutcome.Break)
            ? FieldOutcome.Break
            : diffs.Any(diff => diff.Outcome == FieldOutcome.WithinTolerance) ? FieldOutcome.WithinTolerance : FieldOutcome.Exact;
        var members = Members(group.Left, group.Right);
        var id = Identify(members);
        return (
            new ResultEntry(
                id,
                path.Id,
                Alignment.Matched,
                Compose(group),
                outcome,
                group.Left.SelectMany(row => row.Members).ToImmutableArray(),
                group.Right.SelectMany(row => row.Members).ToImmutableArray(),
                diffs.ToImmutableArray(),
                new Explanation(group.PassId, [new PassAttempt(group.PassId, PassVerdict.Committed, TrailKey(path, group, use), members.ToImmutableArray(), null)], [])),
            left,
            right);
    }

    // The trail key a person reads is the value form of the pass's key fields (#338, R2), never the encoded key.
    // A committed group keys on its target row's RowKey, which is already the value form.
    private static string? TrailKey(PathDeclaration path, CommittedMatch group, FieldUse use) =>
        TrailRow(group) is { } row
            ? MatchKeys.CommittedTrailKey(Pass(path.Passes, group.PassId), group.Key, row, use.Fields)
            : null;

    private static RowContext? TrailRow(CommittedMatch group) =>
        group.Left is [var first, ..] ? first.Context
        : group.Right is [var only, ..] ? only.Context
        : null;

    // LeftOnly and RightOnly residue record NoCandidate. Ambiguous records Contest.
    private static ResultEntry Open(string pathId, ResidueCluster cluster, IReadOnlyList<PassDeclaration> passes, FieldUse use)
    {
        var left = cluster.Rows.Where(row => row.Context.Side == Side.Left).SelectMany(row => row.Members).ToArray();
        var right = cluster.Rows.Where(row => row.Context.Side == Side.Right).SelectMany(row => row.Members).ToArray();
        var members = (SourceRef[])[.. left, .. right];
        var verdict = cluster.Alignment switch
        {
            Alignment.Ambiguous => PassVerdict.Contest,
            Alignment.SearchIncomplete => PassVerdict.SearchIncomplete,
            _ => PassVerdict.NoCandidate,
        };
        return new ResultEntry(
            Identify(members),
            pathId,
            cluster.Alignment,
            Composition.None,
            FieldOutcome.NotCompared,
            left.ToImmutableArray(),
            right.ToImmutableArray(),
            [],
            new Explanation(cluster.PassId, cluster.PassId is null ? [] : [new PassAttempt(cluster.PassId, verdict, OpenTrailKey(cluster, passes, use), members.ToImmutableArray(), cluster.Code)], []));
    }

    private static string? OpenTrailKey(ResidueCluster cluster, IReadOnlyList<PassDeclaration> passes, FieldUse use) =>
        cluster is { PassId: { } passId, Rows: [var row, ..] }
            ? MatchKeys.OpenTrailKey(Pass(passes, passId), cluster.Key, row.Context, use.Fields)
            : null;

    private static PassDeclaration? Pass(IReadOnlyList<PassDeclaration> passes, string passId) =>
        passes.FirstOrDefault(pass => string.Equals(pass.Id, passId, StringComparison.Ordinal));

    // Lineage members inherit the survivor's entry.
    // A member that already has one keeps it, and a suppressed row publishes nothing.
    private static Dictionary<SourceRef, GroupId> Index(IReadOnlyList<ResultEntry> entries, IReadOnlyList<ConsolidatedRow> rows)
    {
        var map = new Dictionary<SourceRef, GroupId>();
        foreach (var entry in entries)
        {
            foreach (var member in entry.Left.Concat(entry.Right))
                map[member] = entry.Id;
        }

        foreach (var row in rows)
        {
            if (row.Lineage is null || row.Suppression is not null)
                continue;
            var id = row.Members.Select(member => map.TryGetValue(member, out var found) ? found : (GroupId?)null).FirstOrDefault(found => found is not null);
            if (id is not { } linked)
                continue;
            foreach (var member in row.Lineage)
                map.TryAdd(member, linked);
        }

        return map;
    }

    // Quarantine wins over suppression and over the match, and the entry id is cleared on a quarantined row.
    private static RowResult Row(ConsolidatedRow row, SourceRef member, Dictionary<SourceRef, GroupId> entryByRow, Dictionary<GroupId, ResultEntry> entries, IReadOnlyDictionary<GroupId, (RowContext Left, RowContext Right)> projections)
    {
        var linked = entryByRow.TryGetValue(member, out var entryId) ? entryId : (GroupId?)null;
        var alignment = linked is { } id ? entries[id].Alignment : (Alignment?)null;
        var disposition = row switch
        {
            { Quarantined: true } => Disposition.Quarantined,
            _ when Invalid(row) => Disposition.Quarantined,
            { Suppression: not null } => Disposition.Suppressed,
            _ when alignment is Alignment.Matched => Disposition.Matched,
            _ when alignment is Alignment.Ambiguous => Disposition.Ambiguous,
            _ when alignment is Alignment.SearchIncomplete => Disposition.SearchIncomplete,
            _ => row.Context.Side == Side.Left ? Disposition.LeftOnly : Disposition.RightOnly,
        };
        var diagnostics = OwnDiagnostics(row, member).AddRange(DiagnosticsOf(row)).AddRange(GroupWarnings(row, linked, projections));
        var validity = row.Quarantined || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid)
            ? InputValidity.Invalid
            : diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning) ? InputValidity.ValidWithWarnings : InputValidity.Valid;
        return new RowResult(member, disposition, row.Suppression, validity, diagnostics, disposition is Disposition.Quarantined ? null : linked);
    }

    // Invalid always means Quarantined (spec v3 §3.4). The matcher already leaves such a row out, so it is not unmatched (#92).
    private static bool Invalid(ConsolidatedRow row) =>
        row.Context.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid);

    // A row no route accepts never reaches a matcher. It still gets its own LeftOnly or RightOnly entry, with no path and
    // the code Unrouted, so every unmatched row has an entry and RunSummary counts it (#92). The trail records the route
    // step; no pass saw the row, so CommittedBy is null.
    private static ResultEntry[] Unrouted(IReadOnlyList<ResultEntry> entries, IEnumerable<ConsolidatedRow> rows)
    {
        var placed = entries.SelectMany(entry => entry.Left.Concat(entry.Right)).ToHashSet();
        return rows
            .Where(row => row is { Quarantined: false, Suppression: null } && Invalid(row) is false && row.Members.Any(member => placed.Contains(member) is false))
            .Select(row => Open("", new ResidueCluster(row.Context.Side == Side.Left ? Alignment.LeftOnly : Alignment.RightOnly, "Unrouted", "route", [row]), [], FieldUse.Everything))
            .Select(entry => entry with { Explanation = entry.Explanation with { CommittedBy = null } })
            .ToArray();
    }

    // An N:1 group's MembersDisagree and AggregateOverflow warnings live on its projection, so each member row of the group
    // reports them too.
    private static IEnumerable<Diagnostic> GroupWarnings(ConsolidatedRow row, GroupId? linked, IReadOnlyDictionary<GroupId, (RowContext Left, RowContext Right)> projections) =>
        linked is { } id && projections.TryGetValue(id, out var pair)
            ? (row.Context.Side == Side.Left ? pair.Left : pair.Right).Diagnostics
                .Where(diagnostic => diagnostic is { Code: MemberAgreement.Disagree or CheckedDecimal.Overflow, RuleId: "aggregate" })
            : [];

    // A package leg reports its own diagnostics first, then the package's (spec v3 §3.2, §3.4; #217).
    private static ImmutableList<Diagnostic> OwnDiagnostics(ConsolidatedRow row, SourceRef member) =>
        row.MemberDiagnostics is { } own && own.TryGetValue(member, out var diagnostics) ? diagnostics : [];

    // Package quarantine already records its code on the row context. Lifecycle quarantine keeps the code only on the consolidated row.
    private static ImmutableList<Diagnostic> DiagnosticsOf(ConsolidatedRow row) =>
        row.Code is { } code && row.Context.Diagnostics.Any(diagnostic => diagnostic.Code == code) is false
            ? row.Context.Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Invalid, "consolidation", null, null))
            : row.Context.Diagnostics;

    // The set is the pass block minus the conserved fields. Those fields matched exactly and are not summed.
    private static IReadOnlySet<string> EqualByConstruction(PathDeclaration path, string passId) =>
        path.Passes.Where(pass => pass.Id == passId).SelectMany(pass => pass.Block.Except(pass.Conserve, StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal);

    // An N:1 projection (spec v3 §5.3). Compared decimals outside the equal-by-construction set are summed and rounded,
    // and one null member nulls the sum. Every other field follows §5.2: members that agree give the value, and members
    // that disagree make it unknown, with MembersDisagree when the run uses the field. The base is the member with the
    // lowest row key, so allocation order never decides (#167). The base keeps only the raw columns every member agrees on,
    // so a condition on a raw column never reads the base member's own column (#236).
    private static RowContext Aggregate(IReadOnlyList<ConsolidatedRow> rows, DiffPlan plan, IReadOnlySet<string> equal, FieldUse use)
    {
        if (rows.Count == 1)
            return rows[0].Context;
        RowContext[] contexts = [.. rows.Select(row => row.Context).OrderBy(context => context.RowKey, StringComparer.Ordinal)];
        DiffStep[] summed = [.. plan.Steps.Where(step => step.Field.Type == FieldType.Decimal && equal.Contains(step.Field.Name) is false)];
        var summedNames = summed.Select(step => step.Field.Name).ToHashSet(StringComparer.Ordinal);
        var start = contexts[0] with { Raw = AgreedRaw(contexts) };
        var warned = DisagreeingRawColumns(contexts, start.Raw, use)
            .Aggregate(start, (current, column) => current.Diagnose(MemberAgreement.Disagree, DiagnosticSeverity.Warning, "aggregate", column, null));
        var settled = contexts
            .SelectMany(context => context.Values.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(field => summedNames.Contains(field) is false)
            .Order(StringComparer.Ordinal)
            .Aggregate(warned, (current, field) => Settle(current, field, contexts, use));
        return summed.Aggregate(settled, (current, step) => Sum(current, step, contexts, plan.Midpoint));
    }

    // A raw column stays on the projection only when every member holds the same text in it, read as a condition reads it.
    private static Dictionary<string, object?> AgreedRaw(RowContext[] members) =>
        members[0].Raw
            .Where(column => members.All(member => member.Raw.TryGetValue(column.Key, out var value) && string.Equals(RawText(value), RawText(column.Value), StringComparison.Ordinal)))
            .ToDictionary(column => column.Key, column => column.Value, StringComparer.Ordinal);

    private static string? RawText(object? value) => InvariantText.Of(value)?.Trim();

    // A raw column the run uses that the members hold differently is named in a MembersDisagree warning, as a canonical
    // field is (§5.2). A canonical field of that name is settled by Settle instead.
    private static IEnumerable<string> DisagreeingRawColumns(IReadOnlyList<RowContext> members, IReadOnlyDictionary<string, object?> agreed, FieldUse use) =>
        members
            .SelectMany(member => member.Raw.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(column => use.IsUsed(column) && agreed.ContainsKey(column) is false && members.All(member => member.Values.ContainsKey(column) is false))
            .Order(StringComparer.Ordinal);

    private static RowContext Settle(RowContext current, string field, RowContext[] members, FieldUse use)
    {
        var typedBy = members.FirstOrDefault(member => member.Values.ContainsKey(field)) ?? members[0];
        return (MemberAgreement.Agree(members, field, use), use.IsUsed(field)) switch
        {
            (true, _) => current,
            (false, true) => MemberAgreement.Unknown(current, field, typedBy, "aggregate")
                .Diagnose(MemberAgreement.Disagree, DiagnosticSeverity.Warning, "aggregate", field, null),
            (false, false) => MemberAgreement.Unknown(current, field, typedBy, "aggregate"),
        };
    }

    // A sum past the decimal range is unknown, with AggregateOverflow naming the field (#224).
    private static RowContext Sum(RowContext current, DiffStep step, IReadOnlyList<RowContext> members, MidpointMode midpoint) =>
        members.Any(member => member.IsNull(step.Field.Name))
            ? current.Write(step.Field.Name, CanonicalCell.Null(FieldType.Decimal), "aggregate", null, null)
            : CheckedDecimal.Sum(members.Select(member => member.Values[step.Field.Name].Number)) switch
            {
                { } total => current.Write(step.Field.Name, CanonicalCell.FromDecimal(Rounding.Round(total, step.Field.Scale, midpoint)), "aggregate", null, null),
                null => current.Write(step.Field.Name, CanonicalCell.Null(FieldType.Decimal), "aggregate", null, null)
                    .Diagnose(CheckedDecimal.Overflow, DiagnosticSeverity.Warning, "aggregate", step.Field.Name, null),
            };

    private static Composition Compose(CommittedMatch group)
    {
        var leftCount = group.Left.Sum(row => row.Packaged ? row.Members.Count : 1);
        var rightCount = group.Right.Sum(row => row.Packaged ? row.Members.Count : 1);
        var composition = (leftCount, rightCount) switch
        {
            (1, 1) => Composition.OneToOne,
            (_, 1) => Composition.ManyToOne,
            _ => Composition.OneToMany,
        };
        if (group.Left.Concat(group.Right).Any(row => row.Packaged))
            composition |= Composition.Package;
        if (group.Left.Concat(group.Right).Any(CollapsedLifecycle))
            composition |= Composition.LifecycleCollapsed;
        return composition;
    }

    private static bool CollapsedLifecycle(ConsolidatedRow row)
    {
        if (row.LifecycleCollapsed is false)
            return false;
        if (row.Lineage is null)
            return true;
        var memberCount = row.Members.Select(member => member.RowKey).ToHashSet(StringComparer.Ordinal).Count;
        return row.Lineage.Select(member => member.RowKey).Distinct(StringComparer.Ordinal).Skip(memberCount).Any();
    }

    private static SourceRef[] Members(IReadOnlyList<ConsolidatedRow> left, IReadOnlyList<ConsolidatedRow> right) =>
        [.. left.SelectMany(row => row.Members), .. right.SelectMany(row => row.Members)];

    // The id keeps the first 16 bytes of the SHA-256.
    // Ordinal is not part of the hash, and duplicate refs collapse first.
    public static GroupId Identify(IReadOnlyList<SourceRef> members)
    {
        var text = string.Join(
            '\n',
            members.Distinct().OrderBy(member => member.Source, StringComparer.Ordinal).ThenBy(member => member.RowKey, StringComparer.Ordinal).Select(member => Delimited.Join('\u001f', [member.Source, member.RowKey])));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new GroupId(Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant());
    }

    // LeftOnly and RightOnly count entries, so they exclude unrouted rows and rows excluded as Invalid that still show as LeftOnly or RightOnly in Rows.
    private static RunSummary Summarize(IReadOnlyList<ResultEntry> entries, IReadOnlyList<RowResult> rows) => new(
        entries.Count(entry => entry.Alignment == Alignment.Matched),
        entries.Count(entry => entry.Outcome == FieldOutcome.Exact),
        entries.Count(entry => entry.Outcome == FieldOutcome.WithinTolerance),
        entries.Count(entry => entry.Outcome == FieldOutcome.Break),
        entries.Count(entry => entry.Alignment == Alignment.LeftOnly),
        entries.Count(entry => entry.Alignment == Alignment.RightOnly),
        entries.Count(entry => entry.Alignment == Alignment.Ambiguous),
        entries.Count(entry => entry.Alignment == Alignment.SearchIncomplete),
        rows.Count(row => row.Disposition == Disposition.Quarantined),
        rows.Count(row => row.Disposition == Disposition.Suppressed));

    // The package version is the informational version without its build metadata: the assembly version drops the
    // prerelease label, so every 1.2.0 preview would report 1.2.0.0. A missing informational version falls back to the
    // assembly version, and a missing assembly version to 1.0.0.
    private static string EngineVersion()
    {
        var assembly = typeof(Reconciler).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational is { Length: > 0 } ? informational.Split('+', 2)[0] : assembly.GetName().Version?.ToString() ?? "1.0.0";
    }
}

internal sealed class ReconciliationResult(
    RunInfo run,
    ImmutableArray<ResultEntry> entries,
    ImmutableArray<RowResult> rows,
    RunSummary summary,
    Dictionary<GroupId, (RowContext Left, RowContext Right)> projections,
    Dictionary<SourceRef, RowContext> contexts) : IReconciliationResult
{
    private readonly Dictionary<GroupId, (RowContext Left, RowContext Right)> _projections = projections ?? throw new ArgumentNullException(nameof(projections));
    private readonly Dictionary<SourceRef, RowContext> _rows = contexts ?? throw new ArgumentNullException(nameof(contexts));

    public RunInfo Run { get; } = run ?? throw new ArgumentNullException(nameof(run));

    public ImmutableArray<ResultEntry> Entries { get; } = entries;

    public ImmutableArray<RowResult> Rows { get; } = rows;

    public RunSummary Summary { get; } = summary ?? throw new ArgumentNullException(nameof(summary));

    public ISideBySideView BuildSideBySide(SideBySideSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return SideBySideBuilder.Build(this, spec);
    }

    // Only a matched entry has a projection. A residue id throws.
    public CanonicalRow Projection(GroupId entry, Side side)
    {
        var pair = _projections[entry];
        return new CanonicalRow(side == Side.Left ? pair.Left : pair.Right);
    }

    public CanonicalRow Canonical(SourceRef row) => new(_rows[row]);

    internal RowContext ConsolidatedItem(SourceRef row) => _rows[row];

    public IReadOnlyList<TraceStep> Trace(SourceRef row) => _rows[row].Traces;

    public string? Raw(SourceRef row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return _rows[row].Raw.TryGetValue(column, out var value) ? InvariantText.Of(value) : null;
    }

    // Text values format as empty. Decimal, date, symbol and identifier are the types that produce text.
    public string Format(in CanonicalValue value) => value.Type switch
    {
        FieldType.Decimal when value.IsNull => "",
        FieldType.Decimal => value.Number.ToString(CultureInfo.InvariantCulture),
        FieldType.Date when value.IsNull => "",
        FieldType.Date => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldType.Symbol or FieldType.Identifier => value.IsNull ? "" : value.Text ?? "",
        _ => "",
    };
}
