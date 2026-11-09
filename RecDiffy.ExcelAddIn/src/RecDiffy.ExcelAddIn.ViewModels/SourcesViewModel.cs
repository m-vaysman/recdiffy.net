using CommunityToolkit.Mvvm.ComponentModel;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class SourcesViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftHeading))]
    private string leftAlias = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightHeading))]
    private string rightAlias = string.Empty;

    public string LeftHeading => SideHeading("Left", LeftAlias);

    public string RightHeading => SideHeading("Right", RightAlias);

    public bool IsComplete => false;

    public static string SideHeading(string side, string alias)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(alias);
        return alias.Trim() is { Length: > 0 } trimmed ? $"{side} · {trimmed}" : side;
    }
}
