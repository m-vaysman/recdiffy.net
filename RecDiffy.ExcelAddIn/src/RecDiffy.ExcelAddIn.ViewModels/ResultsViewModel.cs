using CommunityToolkit.Mvvm.ComponentModel;

namespace RecDiffy.ExcelAddIn.ViewModels;

public enum RunState
{
    NotRun,
    Running,
    Completed,
    Canceled,
    Failed,
}

public sealed partial class ResultsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(ShowsEmptyNotice))]
    private RunState state = RunState.NotRun;

    [ObservableProperty]
    private string stage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate), nameof(ProgressValue))]
    private double? fraction;

    [ObservableProperty]
    private string? message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults), nameof(ShowsEmptyNotice))]
    private RunView? run;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Explanation), nameof(HasSelection))]
    private ResultRow? selected;

    [ObservableProperty]
    private bool isExplanationOpen;

    public bool IsRunning => State is RunState.Running;

    public bool HasResults => Run is not null;

    public bool ShowsEmptyNotice => Run is null && State is RunState.NotRun;

    public bool HasSelection => Selected is not null;

    public bool IsIndeterminate => Fraction is null;

    public double ProgressValue => Fraction ?? 0;

    public IReadOnlyList<ExplanationSection> Explanation => Selected?.Explain() ?? [];

    public void Start() =>
        (State, Stage, Fraction, Message) = (RunState.Running, "Starting the run", null, null);

    public void Report(RunProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (State is not RunState.Running)
            return;

        (Stage, Fraction) = (progress.Stage, progress.Fraction);
    }

    public void Finish(RunOutcome outcome, IReadOnlyList<string> keyFields)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(keyFields);
        (State, Message, Run, Selected, IsExplanationOpen) = outcome switch
        {
            RunOutcome.Completed completed => (RunState.Completed, (string?)null, RunView.Of(completed, keyFields), (ResultRow?)null, false),
            RunOutcome.Canceled => (RunState.Canceled, Kept("The run was canceled."), Run, Selected, IsExplanationOpen),
            RunOutcome.Failed failed => (RunState.Failed, Kept(failed.Reason), Run, Selected, IsExplanationOpen),
            _ => throw new InvalidOperationException("A run is completed, canceled or failed."),
        };
        (Stage, Fraction) = (string.Empty, null);
    }

    private string Kept(string what) =>
        Run is null ? what : $"{what} The results below are from the previous run.";
}
