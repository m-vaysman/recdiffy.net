using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal static class ResultBuilder
{
    public static ReconciliationResult Build(
        CompiledProfile profile,
        ReferenceDataSet reference,
        PathDeclaration path,
        IReadOnlyList<ConsolidatedRow> left,
        IReadOnlyList<ConsolidatedRow> right,
        MatchReport match,
        RunOptions options,
        CancellationToken cancellation)
    {
        var plan = DiffPlan.From(path, profile.Document.Fields, profile.Document.Midpoint);
        var committed = match.Committed.ToArray();
        var built = new (ResultEntry Entry, RowContext Left, RowContext Right)[committed.Length];
        var degree = options.MaxDegreeOfParallelism is int chosen and > 1 ? chosen : 1;
        if (degree == 1 || committed.Length < 2)
        {
            for (var index = 0; index < committed.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                built[index] = Matched(path.Id, committed[index], plan);
            }
        }
        else
        {
            Parallel.For(
                0,
                committed.Length,
                new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellation },
                index => built[index] = Matched(path.Id, committed[index], plan));
        }

        var entries = new List<ResultEntry>();
        var projections = new Dictionary<GroupId, (RowContext Left, RowContext Right)>();
        foreach (var item in built)
        {
            entries.Add(item.Entry);
            projections[item.Entry.Id] = (item.Left, item.Right);
        }

        foreach (var cluster in match.Residue)
            entries.Add(Open(path.Id, cluster));
        var entryByRow = Index(entries, left.Concat(right).ToArray());
        var rows = left.Concat(right).SelectMany(row => row.Members.Select(member => Row(row, member, entryByRow, entries))).ToArray();
        var summary = Summarize(entries, rows);
        return new ReconciliationResult(
            new RunInfo(profile.Hash, reference.Id, reference.ContentHash, EngineVersion()),
            entries,
            rows,
            summary,
            projections,
            left.Concat(right).SelectMany(row => row.Members.Select(member => (member, row.Context))).ToDictionary(pair => pair.member, pair => pair.Context));
    }

    private static (ResultEntry Entry, RowContext Left, RowContext Right) Matched(string pathId, CommittedMatch group, DiffPlan plan)
    {
        var left = Aggregate(group.Left, plan);
        var right = Aggregate(group.Right, plan);
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

    private static RowResult Row(ConsolidatedRow row, SourceRef member, IReadOnlyDictionary<SourceRef, GroupId> entryByRow, IReadOnlyList<ResultEntry> entries)
    {
        var linked = entryByRow.TryGetValue(member, out var entryId) ? entryId : (GroupId?)null;
        var disposition = row switch
        {
            { Quarantined: true } => Disposition.Quarantined,
            { Suppression: not null } => Disposition.Suppressed,
            _ when linked is not null && entries.First(entry => entry.Id == linked.Value).Alignment is Alignment.Matched => Disposition.Matched,
            _ when linked is not null && entries.First(entry => entry.Id == linked.Value).Alignment is Alignment.Ambiguous => Disposition.Ambiguous,
            _ when linked is not null && entries.First(entry => entry.Id == linked.Value).Alignment is Alignment.SearchIncomplete => Disposition.SearchIncomplete,
            _ => row.Context.Side == Side.Left ? Disposition.LeftOnly : Disposition.RightOnly,
        };
        var validity = row.Quarantined || row.Context.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid)
            ? InputValidity.Invalid
            : row.Context.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning) ? InputValidity.ValidWithWarnings : InputValidity.Valid;
        return new RowResult(member, disposition, row.Suppression, validity, row.Context.Diagnostics, disposition is Disposition.Quarantined ? null : linked);
    }

    private static RowContext Aggregate(IReadOnlyList<ConsolidatedRow> rows, DiffPlan plan)
    {
        var seed = rows[0].Context;
        return rows.Count == 1
            ? seed
            : plan.Steps.Where(step => step.Field.Type == FieldType.Decimal).Aggregate(seed, (current, step) =>
            {
                var sum = rows.Aggregate(0m, (total, row) => total + (row.Context.IsNull(step.Field.Name) ? 0m : row.Context.Values[step.Field.Name].Number));
                return current.Write(step.Field.Name, CanonicalCell.FromDecimal(Rounding.Round(sum, step.Field.Scale, plan.Midpoint)), "aggregate", null, null);
            });
    }

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

    public static GroupId Identify(IReadOnlyList<SourceRef> members)
    {
        var text = string.Join(
            '\n',
            members.Distinct().OrderBy(member => member.Source, StringComparer.Ordinal).ThenBy(member => member.RowKey, StringComparer.Ordinal).Select(member => $"{member.Source}\u001f{member.RowKey}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new GroupId(Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant());
    }

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

    public CanonicalRow Projection(GroupId entry, Side side)
    {
        var pair = _projections[entry];
        return new CanonicalRow(side == Side.Left ? pair.Left : pair.Right);
    }

    public CanonicalRow Canonical(SourceRef row) => new(_rows[row]);

    public IReadOnlyList<TraceStep> Trace(SourceRef row) => _rows[row].Traces;

    public string? Raw(SourceRef row, string column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return _rows[row].Raw.TryGetValue(column, out var value) ? value?.ToString() : null;
    }

    public string Format(in CanonicalValue value) => value.Type switch
    {
        FieldType.Decimal when value.IsNull => "",
        FieldType.Decimal => value.Number.ToString(CultureInfo.InvariantCulture),
        FieldType.Date when value.IsNull => "",
        FieldType.Date => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => "",
    };
}
