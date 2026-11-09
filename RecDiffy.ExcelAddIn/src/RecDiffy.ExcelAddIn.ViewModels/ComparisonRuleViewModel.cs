using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class ComparisonRuleViewModel(string field) : ObservableObject
{
    [ObservableProperty]
    private string columns = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesAmount), nameof(UsesPercentage), nameof(UsesDays))]
    private ComparisonKind kind = ComparisonKind.Exact;

    [ObservableProperty]
    private IReadOnlyList<ComparisonChoice> kinds = ComparisonChoice.For(FieldType.Text);

    [ObservableProperty]
    private string amountText = string.Empty;

    [ObservableProperty]
    private string percentageText = string.Empty;

    [ObservableProperty]
    private string daysText = string.Empty;

    [ObservableProperty]
    private string? problem;

    public string Field { get; } = field ?? throw new ArgumentNullException(nameof(field));

    public bool UsesAmount => Kind is ComparisonKind.Absolute or ComparisonKind.AbsoluteOrRelative;

    public bool UsesPercentage => Kind is ComparisonKind.Relative or ComparisonKind.AbsoluteOrRelative;

    public bool UsesDays => Kind is ComparisonKind.Days;

    public static decimal? AmountOf(string text, CultureInfo culture) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, culture, out var amount) ? amount : null;

    public static int? DaysOf(string text) =>
        int.TryParse(text, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var days) && days >= 0 ? days : null;

    public void Apply(ComparisonRule rule, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(culture);
        (Kind, AmountText, PercentageText, DaysText) = (
            rule.Kind,
            rule.Absolute?.ToString(culture) ?? string.Empty,
            (rule.Relative * 100m)?.ToString("0.############################", culture) ?? string.Empty,
            rule.Days?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    public ComparisonRule ToRule(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new ComparisonRule(
            Field,
            Kind,
            UsesAmount ? AmountOf(AmountText, culture) : null,
            UsesPercentage ? AmountOf(PercentageText, culture) / 100m : null,
            UsesDays ? DaysOf(DaysText) : null);
    }
}
