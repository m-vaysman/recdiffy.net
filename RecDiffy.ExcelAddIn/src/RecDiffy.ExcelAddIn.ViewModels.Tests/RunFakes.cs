using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

internal sealed class FakeRuns(Func<RunRequest, IProgress<RunProgress>, CancellationToken, Task<RunOutcome>> run) : IReconciliationRuns
{
    public List<RunRequest> Requests { get; } = [];

    public static FakeRuns Unused() => new((_, _, _) => throw new InvalidOperationException("This test doesn't run a reconciliation."));

    public static FakeRuns Returning(RunOutcome outcome) => new((_, _, _) => Task.FromResult(outcome));

    public Task<RunOutcome> RunAsync(RunRequest request, IProgress<RunProgress> progress, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return run(request, progress, cancellationToken);
    }
}
