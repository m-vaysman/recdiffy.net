using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed class DiffPlan
{
    public required DiffStep[] Steps { get; init; }

    public required MidpointMode Midpoint { get; init; }

    public static DiffPlan From(PathDeclaration path, IReadOnlyList<FieldDeclaration> fields, MidpointMode midpoint) => new()
    {
        Midpoint = midpoint,
        Steps = [.. fields
            .Where(field => field.Role is FieldRole.Key or FieldRole.Economic)
            .Select(field => Step(field, path))],
    };

    private static DiffStep Step(FieldDeclaration field, PathDeclaration path)
    {
        var declared = path.Diff?.Fields.TryGetValue(field.Name, out var diff) == true ? diff : null;
        return new DiffStep(field, declared?.Tolerance, declared?.Attributed ?? declared?.Tolerance.Attributed, declared?.ApplicableWhen);
    }
}

internal sealed record DiffStep(FieldDeclaration Field, ToleranceDeclaration? Tolerance, string? Attribution, WhenClause? ApplicableWhen);

internal static class FieldDiffer
{
    private static readonly DecimalComparer Numbers = default;
    private static readonly DateComparer Dates = default;
    private static readonly string[] NetCashRoots = ["Quantity", "Price", "Accrued", "Fees"];

    // The scan calls Measure, not Finish. Finishing netCash here would read unmeasured sibling scores as NotCompared.
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
    // read unmeasured (NotCompared). netCash is finished last, from its roots' finished outcomes: an attributed root such
    // as Accrued counts with the outcome its own attribution gives it (spec v3 §6, §7.4, §10.1; #218).
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
            outcomes[index] = plan.Steps[index].Attribution is "trading.netCash"
                ? measured[index]
                : Finish(plan.Steps[index], scores[index], measured, plan, left, right);
        }

        for (var index = 0; index < plan.Steps.Length; index++)
        {
            if (plan.Steps[index].Attribution is "trading.netCash")
                outcomes[index] = Finish(plan.Steps[index], scores[index], outcomes, plan, left, right);
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
                Roots(step, outcomes, plan)));
        }

        return [.. diffs];
    }

    // roots holds the outcome each root field counts with.
    private static FieldOutcome Finish(DiffStep step, FieldScore score, FieldOutcome[] roots, DiffPlan plan, RowContext left, RowContext right) =>
        score.Outcome is FieldOutcome.Exact or FieldOutcome.NotCompared
            ? score.Outcome
            : step.Attribution switch
            {
                "trading.proRataToQuantity" when ProRata(left, right, step.Field, plan.Midpoint) => Outcome(roots, plan, "Quantity"),
                "trading.netCash" when NetExplained(left, right, score.Number, plan.Midpoint) => WorstMoved(roots, plan),
                _ => score.Outcome,
            };

    private static ImmutableArray<string> Roots(DiffStep step, FieldOutcome[] outcomes, DiffPlan plan) => step.Attribution switch
    {
        "trading.proRataToQuantity" => ["Quantity"],
        "trading.netCash" => [.. Moved(outcomes, plan)],
        _ => [],
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

    // The left quantity is the base. A zero left quantity is unexplained, and so is an expected value past the decimal
    // range (#224).
    private static bool ProRata(RowContext left, RowContext right, FieldDeclaration field, MidpointMode midpoint)
    {
        if (left.IsNull("Quantity") || right.IsNull("Quantity") || left.IsNull(field.Name) || right.IsNull(field.Name))
            return false;
        var leftQuantity = left.Values["Quantity"].Number;
        if (leftQuantity == 0m)
            return false;
        var expected = CheckedDecimal.Divide(CheckedDecimal.Multiply(left.Values[field.Name].Number, right.Values["Quantity"].Number), leftQuantity);
        return expected is { } value && Rounding.Round(value, field.Scale, midpoint) == Rounding.Round(right.Values[field.Name].Number, field.Scale, midpoint);
    }

    // Each step is the one the expression took before #224, in the same order, so a value that fits is unchanged. A step
    // past the decimal range leaves net cash unexplained.
    private static bool NetExplained(RowContext left, RowContext right, decimal delta, MidpointMode midpoint)
    {
        var asset = left.Display("AssetClass") ?? right.Display("AssetClass");
        var principal = CheckedDecimal.Subtract(Principal(right, asset, midpoint), Principal(left, asset, midpoint));
        var withAccrued = CheckedDecimal.Subtract(CheckedDecimal.Add(principal, Number(right, "Accrued")), Number(left, "Accrued"));
        var fees = CheckedDecimal.Multiply(FeeSign(left, right), CheckedDecimal.Subtract(Number(right, "Fees"), Number(left, "Fees")));
        var expected = CheckedDecimal.Add(withAccrued, fees);
        return expected is { } value && Rounding.Round(value, 2, midpoint) == Rounding.Round(delta, 2, midpoint);
    }

    // Bonds divide quantity times price by 100. A missing asset class does not. A product past the decimal range is null.
    private static decimal? Principal(RowContext row, string? asset, MidpointMode midpoint) =>
        CheckedDecimal.Multiply(Number(row, "Quantity"), Number(row, "Price")) switch
        {
            { } raw => Rounding.Round(asset is "Bond" ? raw / 100m : raw, 2, midpoint),
            null => null,
        };

    // SELL, REC_FIXED and REC make the fee negative. A missing side on both rows leaves it positive.
    private static int FeeSign(RowContext left, RowContext right) =>
        (left.Display("Side") ?? right.Display("Side")) is "SELL" or "REC_FIXED" or "REC" ? -1 : 1;

    // A null field contributes zero, so a missing Accrued can still explain net cash.
    private static decimal Number(RowContext row, string field) => row.IsNull(field) ? 0m : row.Values[field].Number;

    // A root that is not a step is a Break.
    private static FieldOutcome Outcome(FieldOutcome[] outcomes, DiffPlan plan, string field)
    {
        for (var index = 0; index < plan.Steps.Length; index++)
        {
            if (plan.Steps[index].Field.Name == field)
                return outcomes[index];
        }

        return FieldOutcome.Break;
    }

    // When no root has moved, the explained net is still a Break.
    private static FieldOutcome WorstMoved(FieldOutcome[] outcomes, DiffPlan plan)
    {
        var worst = FieldOutcome.Exact;
        foreach (var name in NetCashRoots)
        {
            var outcome = Outcome(outcomes, plan, name);
            if (outcome is FieldOutcome.Break)
                return FieldOutcome.Break;
            if (outcome is FieldOutcome.WithinTolerance)
                worst = FieldOutcome.WithinTolerance;
        }

        return worst is FieldOutcome.Exact ? FieldOutcome.Break : worst;
    }

    // The moved roots in the fixed root order, so the field declaration order cannot reorder AttributedTo (#218).
    private static IEnumerable<string> Moved(FieldOutcome[] outcomes, DiffPlan plan) =>
        NetCashRoots.Where(name => Enumerable.Range(0, plan.Steps.Length).Any(index => plan.Steps[index].Field.Name == name && outcomes[index] is not FieldOutcome.Exact and not FieldOutcome.NotCompared));

    // A null cell is reported as text, whatever type the field has.
    private static CanonicalValue Value(RowContext row, string field) => row.IsNull(field)
        ? new CanonicalValue(FieldType.Text, true, 0m, default, 0)
        : new CanonicalValue(row.Values[field].Type, false, row.Values[field].Number, row.Values[field].Date, row.Values[field].Interned, row.Display(field));

    private static decimal Abs(decimal value) => value < 0m ? -value : value;

    private readonly record struct FieldScore(FieldOutcome Outcome, decimal Number, int Days);
}
