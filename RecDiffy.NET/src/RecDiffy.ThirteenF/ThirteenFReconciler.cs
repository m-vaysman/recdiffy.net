using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.ThirteenF;

internal sealed record FullComparison(
    ManagerHit Manager,
    int Unchanged,
    int Changed,
    int New,
    int Exited,
    decimal PriorTotalUsd,
    decimal CurrentTotalUsd,
    ImmutableArray<HoldingRow> AllRows)
{
    public QuarterComparison Slice(FilingRef prior, FilingRef current, HoldingStatus? filter, int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var filtered = filter is { } status
            ? [.. AllRows.Where(row => row.Status == status)]
            : AllRows;

        var skip = (long)(page - 1) * pageSize;
        IReadOnlyList<HoldingRow> pageRows = skip >= filtered.Length
            ? []
            : [.. filtered.Skip((int)skip).Take(pageSize)];

        return new QuarterComparison(
            Manager,
            prior,
            current,
            Unchanged,
            Changed,
            New,
            Exited,
            PriorTotalUsd,
            CurrentTotalUsd,
            pageRows,
            filtered.Length,
            page,
            pageSize);
    }
}

internal static class ThirteenFReconciler
{
    public static FullComparison Reconcile(
        ManagerHit manager,
        ImmutableArray<AggregatedHolding> priorHoldings,
        ImmutableArray<AggregatedHolding> currentHoldings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var result = RecBuilder.Between(priorHoldings, currentHoldings)
            .Named("sec-13f-holdings", "1.0.0")
            .MatchOn(left => left.Cusip, right => right.Cusip)
                .And(left => left.PutCallKey, right => right.PutCallKey)
                .And(left => left.AmountType, right => right.AmountType)
            .Compare(left => left.Shares, right => right.Shares)
            .Compare(left => left.ValueUsd, right => right.ValueUsd)
            .Run(ct);

        if (result.Result.Summary is { Quarantined: > 0 } or { Ambiguous: > 0 })
            throw new InvalidOperationException("Unexpected quarantined or ambiguous rows during 13F reconciliation.");

        var matchedRows = result.Matched.Select(MapMatched);
        var newRows = result.RightOnly.Select(MapNew);
        var exitedRows = result.LeftOnly.Select(MapExited);

        var orderedRows = matchedRows
            .Concat(newRows)
            .Concat(exitedRows)
            .OrderByDescending(row => row.CurrentValueUsd ?? row.PriorValueUsd ?? 0m)
            .ThenByDescending(row => row.PriorValueUsd ?? 0m)
            .ThenBy(row => row.Cusip, StringComparer.Ordinal)
            .ThenBy(row => row.PutCall ?? "", StringComparer.Ordinal)
            .ThenBy(row => row.TitleOfClass, StringComparer.Ordinal)
            .ThenByDescending(row => row.CurrentShares ?? row.PriorShares ?? 0m)
            .ToImmutableArray();

        var unchanged = orderedRows.Count(row => row.Status is HoldingStatus.Unchanged);
        var changed = orderedRows.Count(row => row.Status is HoldingStatus.Changed);
        var added = orderedRows.Count(row => row.Status is HoldingStatus.New);
        var exited = orderedRows.Count(row => row.Status is HoldingStatus.Exited);

        var priorTotalUsd = priorHoldings.Aggregate(0m, static (sum, item) => checked(sum + item.ValueUsd));
        var currentTotalUsd = currentHoldings.Aggregate(0m, static (sum, item) => checked(sum + item.ValueUsd));

        return new FullComparison(
            manager,
            unchanged,
            changed,
            added,
            exited,
            priorTotalUsd,
            currentTotalUsd,
            orderedRows);
    }

    public static decimal? ComputePercentChange(decimal? prior, decimal? current) =>
        prior is { } p and not 0m && current is { } c
            ? decimal.Round((c - p) / p * 100m, 2, MidpointRounding.ToEven)
            : null;

    private static HoldingRow MapMatched(RecMatch<AggregatedHolding, AggregatedHolding> match)
    {
        var prior = match.Left[0];
        var current = match.Right[0];
        var status = match.Outcome is FieldOutcome.Break
            ? HoldingStatus.Changed
            : HoldingStatus.Unchanged;

        var issuer = current.Issuer.Length > 0 ? current.Issuer : prior.Issuer;
        var titleOfClass = current.TitleOfClass.Length > 0 ? current.TitleOfClass : prior.TitleOfClass;
        var putCall = current.PutCall ?? prior.PutCall;

        return new HoldingRow(
            current.Cusip,
            issuer,
            titleOfClass,
            putCall,
            status,
            prior.Shares,
            current.Shares,
            ComputePercentChange(prior.Shares, current.Shares),
            prior.ValueUsd,
            current.ValueUsd,
            ComputePercentChange(prior.ValueUsd, current.ValueUsd));
    }

    private static HoldingRow MapNew(AggregatedHolding current) =>
        new(
            current.Cusip,
            current.Issuer,
            current.TitleOfClass,
            current.PutCall,
            HoldingStatus.New,
            null,
            current.Shares,
            null,
            null,
            current.ValueUsd,
            null);

    private static HoldingRow MapExited(AggregatedHolding prior) =>
        new(
            prior.Cusip,
            prior.Issuer,
            prior.TitleOfClass,
            prior.PutCall,
            HoldingStatus.Exited,
            prior.Shares,
            null,
            null,
            prior.ValueUsd,
            null,
            null);
}
