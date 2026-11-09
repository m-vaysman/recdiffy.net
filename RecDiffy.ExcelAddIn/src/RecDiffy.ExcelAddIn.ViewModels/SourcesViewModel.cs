using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class SourcesViewModel : ObservableObject
{
    public SourcesViewModel(IExcelSources excel, IWorkbookDialog dialog)
    {
        Left = new SideViewModel("Left", excel, dialog);
        Right = new SideViewModel("Right", excel, dialog);
        Left.PropertyChanged += OnSideChanged;
        Right.PropertyChanged += OnSideChanged;
    }

    public SideViewModel Left { get; }

    public SideViewModel Right { get; }

    public bool IsComplete => Left.HasData && Right.HasData;

    public SideViewModel? Reading => (Left.IsReading, Right.IsReading) switch
    {
        (true, _) => Left,
        (_, true) => Right,
        _ => null,
    };

    public bool IsReading => Reading is not null;

    public string ReadingMessage => Reading is { } side ? $"Reading the {side.Side.ToLowerInvariant()} data" : string.Empty;

    public double ReadingProgress => Reading?.Progress ?? 0;

    private void OnSideChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SideViewModel.HasData):
                OnPropertyChanged(nameof(IsComplete));
                break;
            case nameof(SideViewModel.IsReading):
                OnPropertyChanged(nameof(Reading));
                OnPropertyChanged(nameof(IsReading));
                OnPropertyChanged(nameof(ReadingMessage));
                OnPropertyChanged(nameof(ReadingProgress));
                break;
            case nameof(SideViewModel.Progress):
                OnPropertyChanged(nameof(ReadingProgress));
                break;
        }
    }
}
