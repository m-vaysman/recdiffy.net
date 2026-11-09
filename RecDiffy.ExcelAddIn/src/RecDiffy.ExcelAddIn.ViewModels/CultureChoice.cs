using System.Globalization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record CultureChoice(string Name, string Label)
{
    public static CultureChoice Invariant { get; } = new(string.Empty, "Invariant culture");

    public CultureInfo ToCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(Name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public static CultureChoice Of(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.Name.Length == 0 ? Invariant : new(culture.Name, $"{culture.DisplayName} · {culture.Name}");
    }

    public static IReadOnlyList<CultureChoice> ListWith(CultureChoice required)
    {
        ArgumentNullException.ThrowIfNull(required);
        var specific = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(Of)
            .OrderBy(choice => choice.Label, StringComparer.CurrentCulture);
        IReadOnlyList<CultureChoice> all = [Invariant, .. specific];
        return all.Contains(required) ? all : [required, .. all];
    }

    public static CultureChoice Find(IReadOnlyList<CultureChoice> choices, string name)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(name);
        return choices.FirstOrDefault(choice => string.Equals(choice.Name, name, StringComparison.Ordinal)) ?? new(name, name);
    }
}
