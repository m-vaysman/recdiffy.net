using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IReconciliationRuns _runs;
    private ReconciliationSetup? _lastRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex))]
    [NotifyPropertyChangedFor(nameof(ShowsReviewRules))]
    private ShellTab selectedTab = ShellTab.Sources;

    public ShellViewModel(SourcesViewModel sources, IReconciliationRuns runs)
    {
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        Rules = new RulesViewModel(sources);
        Results = new ResultsViewModel();
        Sources.PropertyChanged += OnSourcesChanged;
        Rules.PropertyChanged += OnRulesChanged;
        Results.PropertyChanged += OnResultsChanged;
    }

    public SourcesViewModel Sources { get; }

    public RulesViewModel Rules { get; }

    public ResultsViewModel Results { get; }

    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set => SelectedTab = Enum.IsDefined((ShellTab)value) ? (ShellTab)value : SelectedTab;
    }

    public bool ShowsReviewRules => SelectedTab is ShellTab.Sources;

    public bool CanRun => Sources.IsComplete && Rules.IsValid && !Results.IsRunning;

    public string Status => (Results.IsRunning, Sources.IsReading, Sources.IsComplete, Rules.ProblemCount) switch
    {
        (true, _, _, _) => $"{Results.Stage}…",
        (_, true, _, _) => $"{Sources.ReadingMessage}…",
        (_, _, false, _) => "Select the left and right data to begin.",
        (_, _, true, 0) => "Ready",
        (_, _, true, 1) => "1 rule problem to fix before running.",
        (_, _, true, var count) => $"{count} rule problems to fix before running.",
    };

    public bool IsChangedSinceRun => _lastRun is null || !_lastRun.Equals(ToSetup());

    public bool ShowsStaleNotice => Results.HasResults && !Results.IsRunning && IsChangedSinceRun;

    public ReconciliationSetup ToSetup()
    {
        var sources = Sources.ToSetup();
        return sources with { Profile = SetupProfiles.Canonical(Rules.ToProfile(sources.Profile)) };
    }

    public void Apply(ReconciliationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Sources.Apply(setup);
        Rules.Apply(setup.Profile);
    }

    public void MarkRun()
    {
        _lastRun = ToSetup();
        OnRunChanged();
    }

    [RelayCommand]
    private void ReviewRules() => SelectedTab = ShellTab.Rules;

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var setup = ToSetup();
        if ((Sources.Left.ToRunSide(), Sources.Right.ToRunSide()) is not ({ } left, { } right))
            return;

        SelectedTab = ShellTab.Results;
        Results.Start();
        var outcome = await _runs
            .RunAsync(new RunRequest(setup.Profile, left, right), new Progress<RunProgress>(Results.Report), cancellationToken)
            .ConfigureAwait(true);
        if (outcome is RunOutcome.Completed)
            _lastRun = setup;

        Results.Finish(outcome, KeyFields(setup.Profile));
        OnRunChanged();
    }

    private static IReadOnlyList<string> KeyFields(ProfileDocument profile) =>
        [.. profile.Fields.Where(field => field.Role is FieldRole.Key).Select(field => field.Name)];

    private void OnSourcesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SourcesViewModel.IsComplete) or nameof(SourcesViewModel.IsReading)))
            return;

        OnRunChanged();
    }

    private void OnRulesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(RulesViewModel.ProblemCount) or nameof(RulesViewModel.Problems)))
            return;

        OnRunChanged();
    }

    private void OnResultsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ResultsViewModel.State) or nameof(ResultsViewModel.Stage) or nameof(ResultsViewModel.Run)))
            return;

        OnRunChanged();
    }

    private void OnRunChanged()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChangedSinceRun));
        OnPropertyChanged(nameof(ShowsStaleNotice));
        RunCommand.NotifyCanExecuteChanged();
    }
}
