using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class FieldRuleViewModel(Action<FieldRuleViewModel> remove) : ObservableObject
{
    private readonly Action<FieldRuleViewModel> _remove = remove ?? throw new ArgumentNullException(nameof(remove));

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? leftColumn;

    [ObservableProperty]
    private string? rightColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDecimal), nameof(IsDate))]
    private FieldType type = FieldType.Text;

    [ObservableProperty]
    private FieldRole role = FieldRole.Text;

    [ObservableProperty]
    private string scaleText = "2";

    [ObservableProperty]
    private string dateFormatsText = string.Empty;

    [ObservableProperty]
    private string? problem;

    public bool IsDecimal => Type is FieldType.Decimal;

    public bool IsDate => Type is FieldType.Date;

    public static int? ScaleOf(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var scale) && scale is >= 0 and <= 28 ? scale : null;

    public static IReadOnlyList<string> FormatsOf(string text) =>
        [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static FieldRuleViewModel From(FieldRule rule, Action<FieldRuleViewModel> remove)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new FieldRuleViewModel(remove)
        {
            Name = rule.Name,
            LeftColumn = rule.LeftColumn,
            RightColumn = rule.RightColumn,
            Type = rule.Type,
            Role = rule.Role,
            ScaleText = rule.Type is FieldType.Decimal ? rule.Scale.ToString(CultureInfo.InvariantCulture) : "2",
            DateFormatsText = string.Join("; ", rule.DateFormats),
        };
    }

    public FieldRule ToRule() => new(
        Name.Trim(),
        LeftColumn,
        RightColumn,
        Type,
        Role,
        Type is FieldType.Decimal ? ScaleOf(ScaleText) ?? 0 : 0,
        Type is FieldType.Date ? FormatsOf(DateFormatsText) : []);

    [RelayCommand]
    private void Remove() => _remove(this);
}
