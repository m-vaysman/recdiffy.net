namespace RecDiffy.ExcelAddIn.ViewModels;

public enum ComparisonKind
{
    Exact,
    Absolute,
    Relative,
    AbsoluteOrRelative,
    Days,
}

public sealed record FieldRule(string Name, string? LeftColumn, string? RightColumn, FieldType Type, FieldRole Role, int Scale, IReadOnlyList<string> DateFormats);

public sealed record PassRule(IReadOnlyList<string> Key);

public sealed record ComparisonRule(string Field, ComparisonKind Kind, decimal? Absolute, decimal? Relative, int? Days);

public sealed record RulesState(IReadOnlyList<FieldRule> Fields, IReadOnlyList<PassRule> Passes, IReadOnlyList<ComparisonRule> Comparisons)
{
    public static RulesState Empty { get; } = new([], [], []);
}
