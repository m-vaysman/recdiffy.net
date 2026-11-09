using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class KeyChoice(string field) : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public string Field { get; } = field ?? throw new ArgumentNullException(nameof(field));
}

public sealed partial class PassRuleViewModel(Action<PassRuleViewModel> remove) : ObservableObject
{
    private readonly Action<PassRuleViewModel> _remove = remove ?? throw new ArgumentNullException(nameof(remove));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Caption))]
    private int number = 1;

    [ObservableProperty]
    private string? problem;

    public ObservableCollection<KeyChoice> Keys { get; } = [];

    public string Title => $"Pass {Number}";

    public string Caption => Number == 1
        ? "Matches records whose chosen key fields are equal."
        : "Runs on the records still unmatched, and matches on these key fields.";

    public IReadOnlyList<string> SelectedKeys => [.. Keys.Where(key => key.IsSelected).Select(key => key.Field)];

    public PassRule ToRule() => new(SelectedKeys);

    public void Offer(IReadOnlyList<string> keyFields, IReadOnlyCollection<string> selected)
    {
        ArgumentNullException.ThrowIfNull(keyFields);
        ArgumentNullException.ThrowIfNull(selected);
        if (Keys.Select(key => key.Field).SequenceEqual(keyFields, StringComparer.Ordinal) && SelectedKeys.SequenceEqual(selected.Where(keyFields.Contains), StringComparer.Ordinal))
            return;

        foreach (var key in Keys)
            key.PropertyChanged -= OnKeyChanged;
        Keys.Clear();
        foreach (var field in keyFields)
        {
            var choice = new KeyChoice(field) { IsSelected = selected.Contains(field, StringComparer.Ordinal) };
            choice.PropertyChanged += OnKeyChanged;
            Keys.Add(choice);
        }

        OnPropertyChanged(nameof(SelectedKeys));
    }

    private void OnKeyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(SelectedKeys));

    [RelayCommand]
    private void Remove() => _remove(this);
}
