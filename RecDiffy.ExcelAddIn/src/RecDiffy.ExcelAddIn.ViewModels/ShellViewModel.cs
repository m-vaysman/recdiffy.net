using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IReconciliationRuns _runs;
    private readonly IResultOutput _output;
    private ReconciliationSetup? _lastRun;
    private ShownRun? _shown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex))]
    [NotifyPropertyChangedFor(nameof(ShowsReviewRules))]
    private ShellTab selectedTab = ShellTab.Sources;

    public ShellViewModel(SourcesViewModel sources, IReconciliationRuns runs)
        : this(sources, runs, ResultOutputs.Unavailable, new SettingsViewModel())
    {
    }

    public ShellViewModel(SourcesViewModel sources, IReconciliationRuns runs, IResultOutput output, SettingsViewModel settings)
    {
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Rules = new RulesViewModel(sources);
        Results = new ResultsViewModel { IncludeExactMatches = settings.IncludeExactMatches };
        Sources.PropertyChanged += OnSourcesChanged;
        Rules.PropertyChanged += OnRulesChanged;
        Results.PropertyChanged += OnResultsChanged;
        Settings.PropertyChanged += OnSettingsChanged;
    }

    public SourcesViewModel Sources { get; }

    public RulesViewModel Rules { get; }

    public ResultsViewModel Results { get; }

    public SettingsViewModel Settings { get; }

    public bool CanWrite => _shown is not null && Results.HasResults && !Results.IsRunning && !Results.IsWriting;

    public bool ShowsExplanation => Results.HasResults && Settings.OffersExplanation;

    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set => SelectedTab = Enum.IsDefined((ShellTab)value) ? (ShellTab)value : SelectedTab;
    }

    public bool ShowsReviewRules => SelectedTab is ShellTab.Sources;

    public bool CanRun => Sources.IsComplete && Rules.IsValid && !Results.IsRunning && !Results.IsWriting;

    public string Status => (Results.IsWriting, Results.IsRunning, Sources.IsReading, Sources.IsComplete, Rules.ProblemCount) switch
    {
        (true, _, _, _, _) => $"{Results.WriteStage}…",
        (_, true, _, _, _) => $"{Results.Stage}…",
        (_, _, true, _, _) => $"{Sources.ReadingMessage}…",
        (_, _, _, false, _) => "Select the left and right data to begin.",
        (_, _, _, true, 0) => "Ready",
        (_, _, _, true, 1) => "1 rule problem to fix before running.",
        (_, _, _, true, var count) => $"{count} rule problems to fix before running.",
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
        if (outcome is RunOutcome.Completed completed)
            (_lastRun, _shown) = (setup, new ShownRun(completed, KeyFields(setup.Profile)));

        Results.Finish(outcome, KeyFields(setup.Profile));
        OnRunChanged();
    }

    [RelayCommand(CanExecute = nameof(CanWrite), IncludeCancelCommand = true)]
    private async Task WriteResultsAsync(CancellationToken cancellationToken)
    {
        if (_shown is not { } shown)
            return;

        var options = new ResultSheetOptions(Settings.IncludeExactMatches, IsChangedSinceRun);
        Results.StartWrite();
        var sheet = await Task.Run(() => ResultSheets.Build(shown.Run, shown.KeyFields, options), CancellationToken.None).ConfigureAwait(true);
        var outcome = await _output.WriteAsync(sheet, new Progress<WriteProgress>(Results.ReportWrite), cancellationToken).ConfigureAwait(true);
        Results.FinishWrite(outcome);
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
        if (e.PropertyName is not (nameof(ResultsViewModel.State) or nameof(ResultsViewModel.Stage) or nameof(ResultsViewModel.Run)
            or nameof(ResultsViewModel.IsWriting) or nameof(ResultsViewModel.WriteStage)))
            return;

        OnRunChanged();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Results.IncludeExactMatches = Settings.IncludeExactMatches;
        OnPropertyChanged(nameof(ShowsExplanation));
    }

    private void OnRunChanged()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChangedSinceRun));
        OnPropertyChanged(nameof(ShowsStaleNotice));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(ShowsExplanation));
        RunCommand.NotifyCanExecuteChanged();
        WriteResultsCommand.NotifyCanExecuteChanged();
    }
}
