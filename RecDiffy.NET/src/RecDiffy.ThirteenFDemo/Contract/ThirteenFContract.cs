// Temporary copy of the RecDiffy.ThirteenF contract. Delete this file when RecDiffy.ThirteenF is referenced.

namespace RecDiffy.ThirteenF;

public sealed record ManagerHit(string Cik, string Name);
public sealed record FilingRef(string Cik, DateOnly PeriodOfReport, DateOnly Filed, string AccessionNumber, string FormType);
public enum HoldingStatus { Unchanged, Changed, New, Exited }
public sealed record HoldingRow(string Cusip, string Issuer, string TitleOfClass, string? PutCall, HoldingStatus Status,
    decimal? PriorShares, decimal? CurrentShares, decimal? SharesChangePct,
    decimal? PriorValueUsd, decimal? CurrentValueUsd, decimal? ValueChangePct);
public sealed record QuarterComparison(ManagerHit Manager, FilingRef Prior, FilingRef Current,
    int Unchanged, int Changed, int New, int Exited,
    decimal PriorTotalUsd, decimal CurrentTotalUsd,
    IReadOnlyList<HoldingRow> Rows, int TotalRows, int Page, int PageSize);
public interface IThirteenFService
{
    Task<IReadOnlyList<ManagerHit>> SearchManagersAsync(string prefix, int take, CancellationToken ct);
    Task<IReadOnlyList<FilingRef>> GetFilingsAsync(string cik, int year, CancellationToken ct);
    Task<QuarterComparison> CompareAsync(string cik, FilingRef prior, FilingRef current, HoldingStatus? filter, int page, int pageSize, CancellationToken ct);
}
