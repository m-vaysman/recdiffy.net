using RecDiffy.ThirteenF;

namespace RecDiffy.ThirteenFDemo.Demo;

public enum DeltaKind
{
    None,
    Flat,
    Up,
    Down,
}

public sealed record DeltaDisplay(string Text, DeltaKind Kind)
{
    public string CssClass => Kind switch
    {
        DeltaKind.Up => "delta-up",
        DeltaKind.Down => "delta-down",
        DeltaKind.Flat => "delta-flat",
        _ => "delta-none",
    };
}

public sealed record DefaultFilingSelection(
    int PriorYear,
    IReadOnlyList<FilingRef> PriorFilings,
    FilingRef? Prior,
    int CurrentYear,
    IReadOnlyList<FilingRef> CurrentFilings,
    FilingRef? Current);

public sealed record PageWindow(
    int Page,
    int PageSize,
    int TotalRows,
    int TotalPages,
    int FirstRow,
    int LastRow)
{
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public static class HoldingsPresentation
{
    public static IReadOnlyList<int> SupportedPageSizes { get; } = [25, 50, 100];

    public static IReadOnlyList<int> SelectableYears(int currentYear) =>
        [.. Enumerable.Range(2013, Math.Max(1, currentYear - 2013 + 1)).Reverse()];

    public static IReadOnlyList<FilingRef> OrderFilingsNewestFirst(IEnumerable<FilingRef> filings)
    {
        ArgumentNullException.ThrowIfNull(filings);

        return
        [
            .. filings
                .OrderByDescending(filing => filing.PeriodOfReport)
                .ThenByDescending(filing => filing.Filed)
                .ThenByDescending(filing => filing.AccessionNumber, StringComparer.Ordinal),
        ];
    }

    public static (FilingRef? Prior, FilingRef? Current) PickLatestTwoPeriods(IReadOnlyList<FilingRef> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);

        if (newestFirst.Count == 0)
        {
            return (null, null);
        }

        var current = newestFirst[0];
        var prior = newestFirst.FirstOrDefault(candidate => candidate.PeriodOfReport < current.PeriodOfReport)
            ?? newestFirst.Skip(1).FirstOrDefault();

        return (prior, current);
    }

    public static string QuarterLabel(DateOnly periodOfReport)
    {
        var quarter = ((periodOfReport.Month - 1) / 3) + 1;
        return $"Q{quarter.ToInvariant()} {periodOfReport.Year.ToInvariant()}";
    }

    public static string FilingOptionLabel(FilingRef filing)
    {
        ArgumentNullException.ThrowIfNull(filing);

        return $"{QuarterLabel(filing.PeriodOfReport)} · {filing.PeriodOfReport.ToInvariant("yyyy-MM-dd")} · {filing.FormType} (filed {filing.Filed.ToInvariant("yyyy-MM-dd")})";
    }

    public static string FormatUsd(decimal value, int scale = 0)
    {
        var safeScale = Math.Clamp(scale, 0, 4);
        var formatted = decimal.Abs(value).ToInvariant($"N{safeScale.ToInvariant()}");
        return value < 0m ? $"-${formatted}" : $"${formatted}";
    }

    public static string FormatNullableUsd(decimal? value, int scale = 0) =>
        value is { } amount ? FormatUsd(amount, scale) : "—";

    public static string FormatNullableShares(decimal? shares, int scale = 0)
    {
        if (shares is not { } count)
        {
            return "—";
        }

        var safeScale = Math.Clamp(scale, 0, 4);
        return count.ToInvariant($"N{safeScale.ToInvariant()}");
    }

    public static int ColumnScale(IEnumerable<decimal?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return Math.Clamp(
            values
                .Where(value => value.HasValue)
                .Select(value => (int)value.GetValueOrDefault().Scale)
                .DefaultIfEmpty(0)
                .Max(),
            0,
            4);
    }

    public static decimal? TotalChangePct(decimal priorTotalUsd, decimal currentTotalUsd) =>
        priorTotalUsd == 0m
            ? null
            : decimal.Round((currentTotalUsd - priorTotalUsd) / priorTotalUsd * 100m, 2, MidpointRounding.AwayFromZero);

    public static DeltaDisplay FormatPercentChange(decimal? pct) => pct switch
    {
        null => new("—", DeltaKind.None),
        > 0m => new($"▲ +{pct.Value.ToInvariant("N2")}%", DeltaKind.Up),
        < 0m => new($"▼ {pct.Value.ToInvariant("N2")}%", DeltaKind.Down),
        _ => new("0.00%", DeltaKind.Flat),
    };

    public static DeltaDisplay FormatDollarDelta(decimal priorTotalUsd, decimal currentTotalUsd)
    {
        var delta = currentTotalUsd - priorTotalUsd;
        return delta switch
        {
            > 0m => new($"▲ +{FormatUsd(delta)}", DeltaKind.Up),
            < 0m => new($"▼ {FormatUsd(delta)}", DeltaKind.Down),
            _ => new("$0", DeltaKind.Flat),
        };
    }

    public static string StatusBadgeClass(HoldingStatus status) => status switch
    {
        HoldingStatus.Unchanged => "unchanged",
        HoldingStatus.Changed => "warn",
        HoldingStatus.New => "ok",
        HoldingStatus.Exited => "bad",
        _ => "",
    };

    public static string StatusText(HoldingStatus status) => status switch
    {
        HoldingStatus.Unchanged => "Unchanged",
        HoldingStatus.Changed => "Changed",
        HoldingStatus.New => "New · new position",
        HoldingStatus.Exited => "Exited · position closed",
        _ => status.ToInvariant(),
    };

    public static int TotalHoldingsCount(QuarterComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        return comparison.Unchanged + comparison.Changed + comparison.New + comparison.Exited;
    }

    public static PageWindow ComputePageWindow(int totalRows, int page, int pageSize)
    {
        var safeSize = Math.Max(1, pageSize);
        var totalPages = totalRows <= 0 ? 1 : ((totalRows - 1) / safeSize) + 1;
        var safePage = Math.Clamp(page, 1, totalPages);
        var firstRow = totalRows == 0 ? 0 : ((safePage - 1) * safeSize) + 1;
        var lastRow = totalRows == 0 ? 0 : Math.Min(totalRows, safePage * safeSize);

        return new PageWindow(safePage, safeSize, Math.Max(0, totalRows), totalPages, firstRow, lastRow);
    }

    public static string FriendlySecError(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(operation);

        return exception switch
        {
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } =>
                $"SEC EDGAR rate-limited the request while {operation}. Wait a few seconds and try again.",
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
                $"SEC EDGAR declined the request while {operation} (HTTP 403). Check the configured SEC User-Agent header and try again.",
            HttpRequestException =>
                $"Could not reach SEC EDGAR while {operation}. Check network connectivity or try again shortly.",
            OperationCanceledException =>
                $"SEC EDGAR timed out while {operation}. Large 13F filings can take a moment — please try again.",
            _ =>
                $"SEC 13F data could not be loaded while {operation}: {exception.Message}",
        };
    }
}
