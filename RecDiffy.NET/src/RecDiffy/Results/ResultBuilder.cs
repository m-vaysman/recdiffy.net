using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class ResultBuilder
{
    // A degree below 2, or a single job, stays on the caller. Parallel starts at two jobs.
    public static ReconciliationResult Build(
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
                var plan = DiffPlan.From(jobs[index].Path, profile.Document.Fields, profile.Document.Midpoint);
                built[index] = Matched(jobs[index].Path.Id, jobs[index].Group, plan, EqualByConstruction(jobs[index].Path, jobs[index].Group.PassId), use);
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
                    var plan = DiffPlan.From(jobs[index].Path, profile.Document.Fields, profile.Document.Midpoint);
                    built[index] = Matched(jobs[index].Path.Id, jobs[index].Group, plan, EqualByConstruction(jobs[index].Path, jobs[index].Group.PassId), use);
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
                entries.Add(Open(item.Path.Id, cluster));
        }

        entries.AddRange(Unrouted(entries, left.Concat(right)));
        var entryByRow = Index(entries, left.Concat(right).ToArray());
        var entryById = entries.ToDictionary(entry => entry.Id);
        var rows = left.Concat(right).SelectMany(row => row.Members.Select(member => Row(row, member, entryByRow, entryById, projections))).ToArray();
        var summary = Summarize(entries, rows);
        return new ReconciliationResult(
            new RunInfo(profile.Hash, reference.Id, reference.ContentHash, EngineVersion()) { Reading = profile.Reading },
            entries,
            rows,
            summary,
            projections,
            left.Concat(right).SelectMany(row => row.Members.Select(member => (member, row.Context))).ToDictionary(pair => pair.member, pair => pair.Context));
    }

    // An empty diff list is Exact. NotCompared fields are absent from that list and do not lower the outcome.
    private static (ResultEntry Entry, RowContext Left, RowContext Right) Matched(string pathId, CommittedMatch group, DiffPlan plan, IReadOnlySet<string> equal, FieldUse use)
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
                pathId,
                Alignment.Matched,
                Compose(group),
                outcome,
                [.. group.Left.SelectMany(row => row.Members)],
                [.. group.Right.SelectMany(row => row.Members)],
                diffs,
                new Explanation(group.PassId, [new PassAttempt(group.PassId, PassVerdict.Committed, group.Key, members, null)], [])),
            left,
            right);
    }

    // LeftOnly and RightOnly residue record NoCandidate. Ambiguous records Contest.
    private static ResultEntry Open(string pathId, ResidueCluster cluster)
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
            left,
            right,
            [],
            new Explanation(cluster.PassId, cluster.PassId is null ? [] : [new PassAttempt(cluster.PassId, verdict, null, members, cluster.Code)], []));
    }

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
    private static RowResult Row(ConsolidatedRow row, SourceRef member, IReadOnlyDictionary<SourceRef, GroupId> entryByRow, IReadOnlyDictionary<GroupId, ResultEntry> entries, IReadOnlyDictionary<GroupId, (RowContext Left, RowContext Right)> projections)
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
        var diagnostics = DiagnosticsOf(row).AddRange(GroupWarnings(row, linked, projections));
        var validity = row.Quarantined || diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid)
            ? InputValidity.Invalid
            : diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning) ? InputValidity.ValidWithWarnings : InputValidity.Valid;
        return new RowResult(member, disposition, row.Suppression, validity, diagnostics, disposition is Disposition.Quarantined ? null : linked);
    }

    // Invalid always means Quarantined (spec v3 §3.4). The matcher already leaves such a row out, so it is not unmatched (#92).
    private static bool Invalid(ConsolidatedRow row) =>
        row.Context.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid);

    // A row no route accepts never reaches a matcher. It still gets its own LeftOnly or RightOnly entry, with no path and
    // the code Unrouted, so every unmatched row has an entry and RunSummary counts it (#92).
    private static IEnumerable<ResultEntry> Unrouted(IReadOnlyList<ResultEntry> entries, IEnumerable<ConsolidatedRow> rows)
    {
        var placed = entries.SelectMany(entry => entry.Left.Concat(entry.Right)).ToHashSet();
        return rows
            .Where(row => row is { Quarantined: false, Suppression: null } && Invalid(row) is false && row.Members.Any(member => placed.Contains(member) is false))
            .Select(row => Open("", new ResidueCluster(row.Context.Side == Side.Left ? Alignment.LeftOnly : Alignment.RightOnly, "Unrouted", "route", [row])))
            .ToArray();
    }

    // An N:1 group's MembersDisagree warnings live on its projection, so each member row of the group reports them too.
    private static IEnumerable<Diagnostic> GroupWarnings(ConsolidatedRow row, GroupId? linked, IReadOnlyDictionary<GroupId, (RowContext Left, RowContext Right)> projections) =>
        linked is { } id && projections.TryGetValue(id, out var pair)
            ? (row.Context.Side == Side.Left ? pair.Left : pair.Right).Diagnostics
                .Where(diagnostic => diagnostic is { Code: MemberAgreement.Disagree, RuleId: "aggregate" })
            : [];

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
    // lowest row key, so allocation order never decides (#167).
    private static RowContext Aggregate(IReadOnlyList<ConsolidatedRow> rows, DiffPlan plan, IReadOnlySet<string> equal, FieldUse use)
    {
        if (rows.Count == 1)
            return rows[0].Context;
        RowContext[] contexts = [.. rows.Select(row => row.Context).OrderBy(context => context.RowKey, StringComparer.Ordinal)];
        DiffStep[] summed = [.. plan.Steps.Where(step => step.Field.Type == FieldType.Decimal && equal.Contains(step.Field.Name) is false)];
        var summedNames = summed.Select(step => step.Field.Name).ToHashSet(StringComparer.Ordinal);
        var settled = contexts
            .SelectMany(context => context.Values.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(field => summedNames.Contains(field) is false)
            .Order(StringComparer.Ordinal)
            .Aggregate(contexts[0], (current, field) => Settle(current, field, contexts, use));
        return summed.Aggregate(settled, (current, step) => Sum(current, step, contexts, plan.Midpoint));
    }

    private static RowContext Settle(RowContext current, string field, IReadOnlyList<RowContext> members, FieldUse use)
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

    private static RowContext Sum(RowContext current, DiffStep step, IReadOnlyList<RowContext> members, MidpointMode midpoint) =>
        members.Any(member => member.IsNull(step.Field.Name))
            ? current.Write(step.Field.Name, CanonicalCell.Null(FieldType.Decimal), "aggregate", null, null)
            : current.Write(step.Field.Name, CanonicalCell.FromDecimal(Rounding.Round(members.Sum(member => member.Values[step.Field.Name].Number), step.Field.Scale, midpoint)), "aggregate", null, null);

    private static Composition Compose(CommittedMatch group)
    {
        var composition = (group.Left.Count, group.Right.Count) switch
        {
            (1, 1) => Composition.OneToOne,
            (_, 1) => Composition.ManyToOne,
            _ => Composition.OneToMany,
        };
        if (group.Left.Concat(group.Right).Any(row => row.Packaged))
            composition |= Composition.Package;
        if (group.Left.Concat(group.Right).Any(row => row.LifecycleCollapsed))
            composition |= Composition.LifecycleCollapsed;
        return composition;
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

    // A missing assembly version is reported as 1.0.0.
    private static string EngineVersion() => typeof(Reconciler).Assembly.GetName().Version?.ToString() ?? "1.0.0";
}

public sealed class ReconciliationResult
{
    private readonly Dictionary<GroupId, (RowContext Left, RowContext Right)> _projections;
    private readonly Dictionary<SourceRef, RowContext> _rows;

    internal ReconciliationResult(
        RunInfo run,
        IReadOnlyList<ResultEntry> entries,
        IReadOnlyList<RowResult> rows,
        RunSummary summary,
        Dictionary<GroupId, (RowContext Left, RowContext Right)> projections,
        Dictionary<SourceRef, RowContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(summary);
        Run = run;
        Entries = entries;
        Rows = rows;
        Summary = summary;
        _projections = projections;
        _rows = contexts;
    }

    public RunInfo Run { get; }

    public IReadOnlyList<ResultEntry> Entries { get; }

    public IReadOnlyList<RowResult> Rows { get; }

    public RunSummary Summary { get; }

    public SideBySideView BuildSideBySide(SideBySideSpec spec)
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
