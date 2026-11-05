using RecDiffy.Results;

namespace RecDiffy;

public sealed record RunOptions(int? MaxDegreeOfParallelism = null, bool TelemetryIncludesRowKeys = false);

public static class Reconciler
{
    public static CompiledProfile Compile(ProfileDocument document, RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);
        return ProfileCompiler.Compile(document, registry) switch
        {
            { Errors: [] } outcome => outcome.Profile!,
            var failed => throw new ProfileCompileException(failed.Errors),
        };
    }

    public static ReconciliationResult Run(
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
        return RunPipeline.Execute(
            profile,
            left.Materialize("left", Key(profile, "left"), cancellationToken),
            right.Materialize("right", Key(profile, "right"), cancellationToken),
            referenceData,
            cancellationToken);
    }

    public static async Task<ReconciliationResult> RunAsync(
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
        return RunPipeline.Execute(profile, leftRows, rightRows, referenceData, cancellationToken);
    }

    private static string Key(CompiledProfile profile, string side) =>
        profile.Document.SourceKeys.TryGetValue(side, out var key) ? key : "";
}

internal static class RunPipeline
{
    public static ReconciliationResult Execute(
        CompiledProfile profile,
        IReadOnlyList<RawRecord> left,
        IReadOnlyList<RawRecord> right,
        ReferenceDataSet reference,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var normalized = Intake.Normalize(profile, left, right, reference);
        cancellation.ThrowIfCancellationRequested();
        var consolidatedLeft = Consolidate(profile, "left", normalized.Left);
        var consolidatedRight = Consolidate(profile, "right", normalized.Right);
        var path = profile.Document.Paths.FirstOrDefault(item => item.Route.Rule == "route.all") ?? profile.Document.Paths[0];
        var match = CascadeMatcher.Run(consolidatedLeft, consolidatedRight, path.Passes, profile.Document.Fields, profile.Document.Limits);
        cancellation.ThrowIfCancellationRequested();
        return ResultBuilder.Build(profile, reference, path, consolidatedLeft, consolidatedRight, match);
    }

    private static IReadOnlyList<ConsolidatedRow> Consolidate(CompiledProfile profile, string side, IReadOnlyList<IntakeRow> rows)
    {
        var plan = profile.Document.Consolidation.TryGetValue(side, out var found) ? found : null;
        var collapsed = Consolidation.Run(rows, plan?.Lifecycle, null);
        return plan is null ? collapsed : plan.Packages.Aggregate(collapsed, Packages.Pack);
    }
}
