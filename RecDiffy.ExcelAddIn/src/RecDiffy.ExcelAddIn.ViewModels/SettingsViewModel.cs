using CommunityToolkit.Mvvm.ComponentModel;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record AddInSettings(bool IncludeExactMatches, bool OffersExplanation)
{
    public static AddInSettings Default { get; } = new(IncludeExactMatches: true, OffersExplanation: true);
}

public interface ISettingsStore
{
    AddInSettings Load();

    string? Save(AddInSettings settings);
}

public sealed class SessionSettings : ISettingsStore
{
    private AddInSettings _settings = AddInSettings.Default;

    public AddInSettings Load() => _settings;

    public string? Save(AddInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        return null;
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;

    [ObservableProperty]
    private bool includeExactMatches;

    [ObservableProperty]
    private bool offersExplanation;

    [ObservableProperty]
    private string? problem;

    public SettingsViewModel()
        : this(new SessionSettings())
    {
    }

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var loaded = store.Load();
        (includeExactMatches, offersExplanation) = (loaded.IncludeExactMatches, loaded.OffersExplanation);
    }

    public AddInSettings ToSettings() => new(IncludeExactMatches, OffersExplanation);

    partial void OnIncludeExactMatchesChanged(bool value) => Save();

    partial void OnOffersExplanationChanged(bool value) => Save();

    private void Save() =>
        Problem = _store.Save(ToSettings()) is { } reason ? $"Your settings apply until Excel closes, but they couldn't be saved: {reason}" : null;
}
