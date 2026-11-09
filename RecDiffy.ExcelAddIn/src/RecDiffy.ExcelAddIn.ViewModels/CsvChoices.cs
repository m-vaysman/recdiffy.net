namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record DelimiterChoice(char? Character, string Label)
{
    public static IReadOnlyList<DelimiterChoice> All { get; } =
    [
        new(',', "Comma"),
        new(';', "Semicolon"),
        new('\t', "Tab"),
        new('|', "Pipe"),
        new(null, "Detect from the header row"),
    ];

    public static DelimiterChoice For(char? character) =>
        All.FirstOrDefault(choice => choice.Character == character) ?? new(character, $"'{character}'");
}

public sealed record EncodingChoice(string Name, string Label)
{
    public static IReadOnlyList<EncodingChoice> All { get; } =
    [
        new("utf-8", "UTF-8"),
        new("utf-16", "UTF-16 LE"),
        new("utf-16BE", "UTF-16 BE"),
        new("windows-1252", "Windows-1252"),
        new("iso-8859-1", "ISO-8859-1"),
    ];

    public static EncodingChoice For(string name) =>
        All.FirstOrDefault(choice => string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase)) ?? new(name, name);
}

public sealed record QuoteChoice(char Character, string Label)
{
    public static IReadOnlyList<QuoteChoice> All { get; } =
    [
        new('"', "Double quote (\")"),
        new('\'', "Single quote (')"),
    ];

    public static QuoteChoice For(char character) =>
        All.FirstOrDefault(choice => choice.Character == character) ?? new(character, $"'{character}'");
}

public sealed record EscapeChoice(bool DoublesQuote, char Character, string Label)
{
    public static IReadOnlyList<EscapeChoice> All { get; } =
    [
        new(true, '"', "Doubled quote"),
        new(false, '\\', "Backslash (\\)"),
    ];

    public static EscapeChoice For(char escape, char quote) =>
        escape == quote ? All[0] : All.FirstOrDefault(choice => choice.Character == escape) ?? new(false, escape, $"'{escape}'");

    public char EscapeFor(char quote) => DoublesQuote ? quote : Character;
}
