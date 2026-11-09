using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex))]
    [NotifyPropertyChangedFor(nameof(ShowsReviewRules))]
    private ShellTab selectedTab = ShellTab.Sources;

    public ShellViewModel(SourcesViewModel sources)
    {
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        Sources.PropertyChanged += OnSourcesChanged;
    }

    public SourcesViewModel Sources { get; }

    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set => SelectedTab = Enum.IsDefined((ShellTab)value) ? (ShellTab)value : SelectedTab;
    }

    public bool ShowsReviewRules => SelectedTab is ShellTab.Sources;

    public bool CanRun => Sources.IsComplete;

    public string Status => (Sources.IsReading, CanRun) switch
    {
        (true, _) => $"{Sources.ReadingMessage}…",
        (_, true) => "Ready",
        _ => "Select the left and right data to begin.",
    };

    [RelayCommand]
    private void ReviewRules() => SelectedTab = ShellTab.Rules;

    private void OnSourcesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SourcesViewModel.IsComplete) or nameof(SourcesViewModel.IsReading)))
            return;

        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(Status));
    }
}
