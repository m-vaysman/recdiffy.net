using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed class DiffPlan
{
    public required DiffStep[] Steps { get; init; }

    public required MidpointMode Midpoint { get; init; }

    public static DiffPlan From(PathDeclaration path, IReadOnlyList<FieldDeclaration> fields, MidpointMode midpoint) =>
        From(path, fields, midpoint, AttributionRegistry.Empty);

    public static DiffPlan From(PathDeclaration path, IReadOnlyList<FieldDeclaration> fields, MidpointMode midpoint, AttributionRegistry attributions) => new()
    {
        Midpoint = midpoint,
        Steps = [.. fields
            .Where(field => field.Role is FieldRole.Key or FieldRole.Economic)
            .Select(field => Step(field, path, attributions))],
    };

    private static DiffStep Step(FieldDeclaration field, PathDeclaration path, AttributionRegistry attributions)
    {
        var declared = path.Diff?.Fields.TryGetValue(field.Name, out var diff) == true ? diff : null;
        var attribution = declared?.Attributed ?? declared?.Tolerance.Attributed;
        return new DiffStep(field, declared?.Tolerance, attribution, attributions.Find(attribution), declared?.ApplicableWhen);
    }
}

internal sealed record DiffStep(FieldDeclaration Field, ToleranceDeclaration? Tolerance, string? Attribution, IAttribution? Formula, WhenClause? ApplicableWhen);

internal static class FieldDiffer
{
    private static readonly DecimalComparer Numbers = default;
    private static readonly DateComparer Dates = default;

    // The scan calls Measure, not Finish. Finishing an attribution here would read unmeasured sibling scores as NotCompared.
    public static FieldDiff[] Compare(RowContext left, RowContext right, DiffPlan plan)
    {
        for (var index = 0; index < plan.Steps.Length; index++)
        {
            if (Measure(plan.Steps[index], left, right, plan.Midpoint).Outcome is not FieldOutcome.Exact and not FieldOutcome.NotCompared)
                return Materialize(left, right, plan);
        }

        return Array.Empty<FieldDiff>();
    }

    // Every score is measured before any attribution is finished, so the field declaration order never lets a root be
    // read unmeasured (NotCompared). A Finished attribution is finished last, from its roots' finished outcomes: an
    // attributed root counts with the outcome its own attribution gives it (spec v3 §6, §7.4, §10.1; #218).
    private static FieldDiff[] Materialize(RowContext left, RowContext right, DiffPlan plan)
    {
        var scores = new FieldScore[plan.Steps.Length];
        var measured = new FieldOutcome[plan.Steps.Length];
        for (var index = 0; index < plan.Steps.Length; index++)
        {
            scores[index] = Measure(plan.Steps[index], left, right, plan.Midpoint);
            measured[index] = scores[index].Outcome;
        }

        var outcomes = new FieldOutcome[plan.Steps.Length];
        for (var index = 0; index < plan.Steps.Length; index++)
        {
            outcomes[index] = plan.Steps[index].Formula is { Stage: AttributionStage.Finished }
                ? measured[index]
                : Finish(plan.Steps[index], scores[index], new RootOutcomes(plan.Steps, measured), plan, left, right);
        }

        for (var index = 0; index < plan.Steps.Length; index++)
        {
            if (plan.Steps[index].Formula is { Stage: AttributionStage.Finished })
                outcomes[index] = Finish(plan.Steps[index], scores[index], new RootOutcomes(plan.Steps, outcomes), plan, left, right);
        }

        var diffs = new List<FieldDiff>();
        for (var index = 0; index < plan.Steps.Length; index++)
        {
            if (outcomes[index] is FieldOutcome.Exact or FieldOutcome.NotCompared)
                continue;
            var step = plan.Steps[index];
            var score = scores[index];
            diffs.Add(new FieldDiff(
                new FieldId(step.Field.Name),
                outcomes[index],
                Value(left, step.Field.Name),
                Value(right, step.Field.Name),
                new Delta(step.Field.Type switch { FieldType.Date => DeltaKind.Days, FieldType.Decimal => DeltaKind.Number, _ => DeltaKind.None }, score.Number, score.Days),
                new ToleranceId(step.Tolerance?.Kind ?? "Exact"),
                new AttributionId(step.Attribution ?? ""),
                Roots(step, new RootOutcomes(plan.Steps, outcomes))));
        }

        return [.. diffs];
    }

    // roots holds the outcome each root field counts with.
    private static FieldOutcome Finish(DiffStep step, FieldScore score, RootOutcomes roots, DiffPlan plan, RowContext left, RowContext right) =>
        score.Outcome is FieldOutcome.Exact or FieldOutcome.NotCompared
            ? score.Outcome
            : step.Formula switch
            {
                { } attribution when attribution.Explains(left, right, step.Field, score.Number, plan.Midpoint) => attribution.Outcome(roots),
                _ => score.Outcome,
            };

    private static ImmutableArray<string> Roots(DiffStep step, RootOutcomes outcomes) => step.Formula switch
    {
        { } attribution => attribution.AttributedTo(outcomes),
        null => [],
    };

    // Both null cells are Exact, and one null cell is a Break.
    // When applicableWhen is set, the field is skipped only if both sides miss it.
    private static FieldScore Measure(DiffStep step, RowContext left, RowContext right, MidpointMode midpoint)
    {
        if (step.ApplicableWhen is not null && WhenMatcher.Holds(step.ApplicableWhen, left) is false && WhenMatcher.Holds(step.ApplicableWhen, right) is false)
            return new FieldScore(FieldOutcome.NotCompared, 0m, 0);
        var missingLeft = left.IsNull(step.Field.Name);
        var missingRight = right.IsNull(step.Field.Name);
        if (missingLeft && missingRight)
            return new FieldScore(FieldOutcome.Exact, 0m, 0);
        if (missingLeft || missingRight)
            return new FieldScore(FieldOutcome.Break, 0m, 0);
        return step.Field.Type switch
        {
            FieldType.Decimal => Decimal(step, left, right, midpoint),
            FieldType.Date => Date(step, left, right),
            FieldType.Symbol => Text(left.Pool.Symbol(left.Values[step.Field.Name].Interned), right.Pool.Symbol(right.Values[step.Field.Name].Interned)),
            FieldType.Identifier => Text(left.Pool.Identifier(left.Values[step.Field.Name].Interned).Value, right.Pool.Identifier(right.Values[step.Field.Name].Interned).Value),
            FieldType.Text => Text(left.Values[step.Field.Name].Text, right.Values[step.Field.Name].Text),
            _ => new FieldScore(FieldOutcome.Break, 0m, 0),
        };
    }

    private static FieldScore Decimal(DiffStep step, RowContext left, RowContext right, MidpointMode midpoint)
    {
        var scaledLeft = Rounding.Round(left.Values[step.Field.Name].Number, step.Field.Scale, midpoint);
        var scaledRight = Rounding.Round(right.Values[step.Field.Name].Number, step.Field.Scale, midpoint);
        Numbers.Compare(in scaledLeft, in scaledRight, out var delta);
        if (delta.Number == 0m)
            return new FieldScore(FieldOutcome.Exact, 0m, 0);
        return new FieldScore(Within(step, left, right, scaledLeft, scaledRight, delta.Number) ? FieldOutcome.WithinTolerance : FieldOutcome.Break, delta.Number, 0);
    }

    // Only a Days tolerance allows a gap. Any other kind, including a missing tolerance, allows zero days.
    private static FieldScore Date(DiffStep step, RowContext left, RowContext right)
    {
        var scaledLeft = left.Values[step.Field.Name].Date;
        var scaledRight = right.Values[step.Field.Name].Date;
        Dates.Compare(in scaledLeft, in scaledRight, out var delta);
        if (delta.Days == 0)
            return new FieldScore(FieldOutcome.Exact, 0m, 0);
        var allowed = step.Tolerance is { Kind: "Days", Days: { } days } ? days : 0;
        return new FieldScore(Math.Abs(delta.Days) <= allowed ? FieldOutcome.WithinTolerance : FieldOutcome.Break, 0m, delta.Days);
    }

    private static FieldScore Text(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal) ? new FieldScore(FieldOutcome.Exact, 0m, 0) : new FieldScore(FieldOutcome.Break, 0m, 0);

    // The tolerance when must hold on both sides. One side failing it leaves a Break.
    private static bool Within(DiffStep step, RowContext left, RowContext right, decimal scaledLeft, decimal scaledRight, decimal delta)
    {
        if (step.Tolerance is not { } tolerance || tolerance.Kind is "Exact" or "Attributed")
            return false;
        if (tolerance.When is not null && (WhenMatcher.Holds(tolerance.When, left) is false || WhenMatcher.Holds(tolerance.When, right) is false))
            return false;
        var abs = delta < 0m ? -delta : delta;
        return tolerance.Kind switch
        {
            "Absolute" => abs <= tolerance.Absolute.GetValueOrDefault(),
            "Relative" => Relative(abs, scaledLeft, scaledRight, tolerance.Relative.GetValueOrDefault()),
            "AbsoluteOrRelative" => abs <= tolerance.Absolute.GetValueOrDefault() || Relative(abs, scaledLeft, scaledRight, tolerance.Relative.GetValueOrDefault()),
            _ => false,
        };
    }

    // An allowance past the decimal range cannot be computed, so the delta is not within it (#224).
    private static bool Relative(decimal abs, decimal left, decimal right, decimal ratio)
    {
        var scale = Abs(left) > Abs(right) ? Abs(left) : Abs(right);
        return scale is not 0m && CheckedDecimal.Multiply(ratio, scale) is { } allowed && abs <= allowed;
    }

    // A null cell is reported as text, whatever type the field has.
    private static CanonicalValue Value(RowContext row, string field) => row.IsNull(field)
        ? new CanonicalValue(FieldType.Text, true, 0m, default, 0)
        : new CanonicalValue(row.Values[field].Type, false, row.Values[field].Number, row.Values[field].Date, row.Values[field].Interned, row.Display(field));

    private static decimal Abs(decimal value) => value < 0m ? -value : value;

    private readonly record struct FieldScore(FieldOutcome Outcome, decimal Number, int Days);
}
