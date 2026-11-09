namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record TypeChoice(FieldType Type, string Label)
{
    public static IReadOnlyList<TypeChoice> All { get; } =
    [
        new(FieldType.Text, "Text"),
        new(FieldType.Identifier, "Identifier"),
        new(FieldType.Symbol, "Code"),
        new(FieldType.Decimal, "Number"),
        new(FieldType.Date, "Date"),
    ];
}

public sealed record RoleChoice(FieldRole Role, string Label)
{
    public static IReadOnlyList<RoleChoice> All { get; } =
    [
        new(FieldRole.Key, "Key"),
        new(FieldRole.Economic, "Compared"),
        new(FieldRole.Text, "Carried"),
    ];
}

public sealed record ComparisonChoice(ComparisonKind Kind, string Label)
{
    public static IReadOnlyList<ComparisonChoice> All { get; } =
    [
        new(ComparisonKind.Exact, "Exact"),
        new(ComparisonKind.Absolute, "Within an amount"),
        new(ComparisonKind.Relative, "Within a percentage"),
        new(ComparisonKind.AbsoluteOrRelative, "Within an amount or a percentage"),
        new(ComparisonKind.Days, "Within a number of days"),
    ];

    private static readonly IReadOnlyDictionary<FieldType, IReadOnlyList<ComparisonChoice>> ByType =
        Enum.GetValues<FieldType>().ToDictionary(type => type, type => (IReadOnlyList<ComparisonChoice>)[.. All.Where(choice => RulesProfiles.KindsFor(type).Contains(choice.Kind))]);

    public static IReadOnlyList<ComparisonChoice> For(FieldType type) => ByType[type];
}
