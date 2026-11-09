using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private ReconciliationSetup? _lastRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex))]
    [NotifyPropertyChangedFor(nameof(ShowsReviewRules))]
    private ShellTab selectedTab = ShellTab.Sources;

    public ShellViewModel(SourcesViewModel sources)
    {
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        Rules = new RulesViewModel(sources);
        Sources.PropertyChanged += OnSourcesChanged;
        Rules.PropertyChanged += OnRulesChanged;
    }

    public SourcesViewModel Sources { get; }

    public RulesViewModel Rules { get; }

    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set => SelectedTab = Enum.IsDefined((ShellTab)value) ? (ShellTab)value : SelectedTab;
    }

    public bool ShowsReviewRules => SelectedTab is ShellTab.Sources;

    public bool CanRun => Sources.IsComplete && Rules.IsValid;

    public string Status => (Sources.IsReading, Sources.IsComplete, Rules.ProblemCount) switch
    {
        (true, _, _) => $"{Sources.ReadingMessage}…",
        (_, false, _) => "Select the left and right data to begin.",
        (_, true, 0) => "Ready",
        (_, true, 1) => "1 rule problem to fix before running.",
        (_, true, var count) => $"{count} rule problems to fix before running.",
    };

    public bool IsChangedSinceRun => _lastRun is null || !_lastRun.Equals(ToSetup());

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
        OnPropertyChanged(nameof(IsChangedSinceRun));
    }

    [RelayCommand]
    private void ReviewRules() => SelectedTab = ShellTab.Rules;

    private void OnSourcesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SourcesViewModel.IsComplete) or nameof(SourcesViewModel.IsReading)))
            return;

        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChangedSinceRun));
    }

    private void OnRulesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(RulesViewModel.ProblemCount) or nameof(RulesViewModel.Problems)))
            return;

        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChangedSinceRun));
    }
}
