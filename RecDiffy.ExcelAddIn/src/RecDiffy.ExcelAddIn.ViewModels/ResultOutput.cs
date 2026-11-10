using System.Globalization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record WriteProgress(int Written, int Total);

public abstract record WriteOutcome
{
    private WriteOutcome()
    {
    }

    public sealed record Completed(string Workbook, IReadOnlyList<string> Sheets, int Results) : WriteOutcome;

    public sealed record Canceled(string Workbook, IReadOnlyList<string> Sheets, int Written, int Total) : WriteOutcome;

    public sealed record Failed(string Reason, int Written, int Total) : WriteOutcome;
}

public interface IResultOutput
{
    Task<WriteOutcome> WriteAsync(ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken);
}

public sealed record ShownRun(RunOutcome.Completed Run, IReadOnlyList<string> KeyFields);

public static class ResultOutputs
{
    public static IResultOutput Unavailable { get; } = new UnavailableOutput();

    public static string Describe(WriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome switch
        {
            WriteOutcome.Completed written => string.Create(CultureInfo.CurrentCulture, $"Wrote {Results(written.Results)} to {Where(written.Workbook, written.Sheets)}."),
            WriteOutcome.Canceled canceled => string.Create(
                CultureInfo.CurrentCulture,
                $"The write was canceled after {canceled.Written:N0} of {Results(canceled.Total)}. {Where(canceled.Workbook, canceled.Sheets)} keeps what was written and says it's incomplete."),
            WriteOutcome.Failed { Written: 0 } failed => $"The results weren't written: {failed.Reason}",
            WriteOutcome.Failed failed => string.Create(
                CultureInfo.CurrentCulture,
                $"The write stopped after {failed.Written:N0} of {Results(failed.Total)}: {failed.Reason}"),
            _ => throw new InvalidOperationException("A write is written, canceled or failed."),
        };
    }

    private static string Results(int count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "result" : "results")}");

    private static string Where(string workbook, IReadOnlyList<string> sheets) => sheets switch
    {
        [] => workbook,
        [var only] => $"[{workbook}]{only}",
        _ => $"{workbook}, on sheets {string.Join(", ", sheets.Take(sheets.Count - 1))} and {sheets[^1]}",
    };

    private sealed class UnavailableOutput : IResultOutput
    {
        public Task<WriteOutcome> WriteAsync(ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            return Task.FromResult<WriteOutcome>(new WriteOutcome.Failed("writing to a worksheet isn't available here.", 0, sheet.Results));
        }
    }
}
