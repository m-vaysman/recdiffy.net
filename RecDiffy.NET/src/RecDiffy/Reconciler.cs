using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy;

/// <summary>Options for one run.</summary>
/// <param name="MaxDegreeOfParallelism">
/// How many threads result building may use. Null, or a value below 2, uses no extra thread. A value of 2 or more lets
/// result building work on the matched entries in parallel, on up to that many threads: for each entry it combines the
/// rows and compares the fields. A run with fewer than two matched entries uses no extra thread either. Only this stage
/// is parallel. Normalization, consolidation and matching run on one thread, whatever the value. The value does not
/// change the result.
/// </param>
/// <param name="TelemetryIncludesRowKeys">
/// Reserved. The engine does not read this value, so it has no effect. Telemetry holds no row keys, whatever is passed.
/// </param>
public sealed record RunOptions(int? MaxDegreeOfParallelism = null, bool TelemetryIncludesRowKeys = false);

public static class Reconciler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);
        // The one place the server's culture is read: once, here, for every side that declares none (#158).
        return ProfileCompiler.Compile(document, registry, CultureInfo.CurrentCulture) switch
        {
            { Errors: [] } outcome => outcome.Profile!,
            var failed => throw new ProfileCompileException(failed.Errors),
        };
    }

    public static IReconciliationResult Run(
        CompiledProfile profile,
        RecordSource left,
        RecordSource right,
        ReferenceDataSet referenceData,
        RunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(referenceData);
        return RunPipeline.Execute(
            profile,
            left.Materialize("left", Key(profile, "left"), cancellationToken),
            right.Materialize("right", Key(profile, "right"), cancellationToken),
            referenceData,
            options ?? new RunOptions(),
            cancellationToken);
    }

    public static async Task<IReconciliationResult> RunAsync(
        CompiledProfile profile,
        RecordSource left,
        RecordSource right,
        ReferenceDataSet referenceData,
        RunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(referenceData);
        _ = options;
        cancellationToken.ThrowIfCancellationRequested();
        var leftRows = await left.MaterializeAsync("left", Key(profile, "left"), cancellationToken).ConfigureAwait(false);
        var rightRows = await right.MaterializeAsync("right", Key(profile, "right"), cancellationToken).ConfigureAwait(false);
        return RunPipeline.Execute(profile, leftRows, rightRows, referenceData, options ?? new RunOptions(), cancellationToken);
    }

    private static string Key(CompiledProfile profile, string side) =>
        profile.Document.SourceKeys.TryGetValue(side, out var key) ? key : "";
}

internal static class RunPipeline
{
    public static IReconciliationResult Execute(
        CompiledProfile profile,
        IReadOnlyList<RawRecord> left,
        IReadOnlyList<RawRecord> right,
        ReferenceDataSet reference,
        RunOptions options,
        CancellationToken cancellation)
    {
        using var activity = RecDiffyInstrumentation.Source.StartActivity("RecDiffy.Run");
        activity?.SetTag("profile.hash", profile.Hash);
        cancellation.ThrowIfCancellationRequested();
        RecDiffyInstrumentation.RowsRead.Add(left.Count + right.Count);
        var normalized = Intake.Normalize(profile, left, right, reference);
        cancellation.ThrowIfCancellationRequested();
        var consolidatedLeft = Consolidate(profile, "left", normalized.Left);
        var consolidatedRight = Consolidate(profile, "right", normalized.Right);
        var schema = profile.Document.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var paths = profile.Document.Paths;
        var routed = paths.Select(path => (
            Path: path,
            Match: CascadeMatcher.Run(
                OnPath(consolidatedLeft, path, paths, schema),
                OnPath(consolidatedRight, path, paths, schema),
                path.Passes,
                profile.Document.Fields,
                profile.Document.Limits,
                cancellation))).ToArray();
        cancellation.ThrowIfCancellationRequested();
        RecDiffyInstrumentation.Commits.Add(routed.Sum(item => item.Match.Committed.Count));
        var result = ResultBuilder.Build(profile, reference, routed, consolidatedLeft, consolidatedRight, options, cancellation);
        RecDiffyInstrumentation.Breaks.Add(result.Summary.Break);
        return result;
    }

    // The package argument stays null. Each package is applied afterwards, and passing one here would pack twice.
    private static IReadOnlyList<ConsolidatedRow> Consolidate(CompiledProfile profile, string side, IReadOnlyList<IntakeRow> rows)
    {
        var plan = profile.Document.Consolidation.TryGetValue(side, out var found) ? found : null;
        var collapsed = Consolidation.Run(rows, plan?.Lifecycle, null);
        var use = FieldUse.Of(profile.Document);
        return plan is null ? collapsed : plan.Packages.Aggregate(collapsed, (current, package) => Packages.Pack(current, package, use));
    }

    private static IReadOnlyList<ConsolidatedRow> OnPath(
        IReadOnlyList<ConsolidatedRow> rows,
        PathDeclaration path,
        IReadOnlyList<PathDeclaration> paths,
        IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        [.. rows.Where(row => ReferenceEquals(FirstPath(row, paths, schema), path))];

    // The first matching path wins. Two byField routes on different fields can both match, and compile accepts that.
    private static PathDeclaration? FirstPath(
        ConsolidatedRow row,
        IReadOnlyList<PathDeclaration> paths,
        IReadOnlyDictionary<string, FieldDeclaration> schema)
    {
        foreach (var candidate in paths)
        {
            if (OnRoute(candidate.Route, row, schema))
                return candidate;
        }

        return null;
    }

    // An unknown route rule matches no row. Compile rejects that rule; a document that skipped compile drops the
    // path.
    // A blank cell reads as the empty text, so `"equals": ""` takes it, as a condition and a side-by-side filter read
    // the same cell (#337).
    private static bool OnRoute(RouteDeclaration route, ConsolidatedRow row, IReadOnlyDictionary<string, FieldDeclaration> schema) =>
        route.Rule switch
        {
            "route.all" => true,
            "route.byField" when route.Field is { } name && schema.TryGetValue(name, out var field) =>
                (MatchKeys.ValueText(row.Context, field) ?? "") == route.EqualsValue,
            _ => false,
        };
}
