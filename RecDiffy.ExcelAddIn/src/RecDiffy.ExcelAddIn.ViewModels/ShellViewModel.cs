using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class ShellViewModel(SourcesViewModel sources) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabIndex))]
    [NotifyPropertyChangedFor(nameof(ShowsReviewRules))]
    private ShellTab selectedTab = ShellTab.Sources;

    public SourcesViewModel Sources { get; } = sources ?? throw new ArgumentNullException(nameof(sources));

    public int SelectedTabIndex
    {
        get => (int)SelectedTab;
        set => SelectedTab = Enum.IsDefined((ShellTab)value) ? (ShellTab)value : SelectedTab;
    }

    public bool ShowsReviewRules => SelectedTab is ShellTab.Sources;

    public bool CanRun => Sources.IsComplete;

    public string Status => CanRun ? "Ready" : "Select the left and right data to begin.";

    [RelayCommand]
    private void ReviewRules() => SelectedTab = ShellTab.Rules;
}
