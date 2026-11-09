using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class SourcesViewModel : ObservableObject
{
    private ProfileDocument _profile = SetupProfiles.Draft();

    public SourcesViewModel(SourceServices services, CultureInfo excelCulture)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(excelCulture);
        ExcelCulture = CultureChoice.Of(excelCulture);
        Cultures = CultureChoice.ListWith(ExcelCulture);
        Left = new SideViewModel("Left", services, ExcelCulture);
        Right = new SideViewModel("Right", services, ExcelCulture);
        Left.PropertyChanged += OnSideChanged;
        Right.PropertyChanged += OnSideChanged;
    }

    public CultureChoice ExcelCulture { get; }

    public IReadOnlyList<CultureChoice> Cultures { get; }

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

    public ReconciliationSetup ToSetup() =>
        new(Left.ToSetup(), Right.ToSetup(), SetupProfiles.WithCultures(_profile, Left.Culture.Name, Right.Culture.Name));

    public void Apply(ReconciliationSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _profile = setup.Profile;
        Left.Apply(setup.Left, CultureFor(setup.Profile, Side.Left));
        Right.Apply(setup.Right, CultureFor(setup.Profile, Side.Right));
    }

    private CultureChoice CultureFor(ProfileDocument profile, Side side) =>
        SetupProfiles.CultureOf(profile, side) is { } name ? CultureChoice.Find(Cultures, name) : ExcelCulture;

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
