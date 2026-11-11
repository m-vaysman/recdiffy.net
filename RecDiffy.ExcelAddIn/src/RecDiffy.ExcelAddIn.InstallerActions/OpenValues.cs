using System.Globalization;
using System.Text.RegularExpressions;

namespace RecDiffy.ExcelAddIn.InstallerActions;

public sealed class OpenChanges(IReadOnlyList<KeyValuePair<string, string>> writes, IReadOnlyList<string> deletes)
{
    public IReadOnlyList<KeyValuePair<string, string>> Writes { get; } = writes;

    public IReadOnlyList<string> Deletes { get; } = deletes;

    public bool IsEmpty => Writes.Count == 0 && Deletes.Count == 0;
}

public static class OpenValues
{
    public const string OptionsKey = @"Software\Microsoft\Office\16.0\Excel\Options";

    private static readonly Regex OpenName = new(
        "^OPEN(?<index>[0-9]*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    public static string NameAt(int index) =>
        index == 0 ? "OPEN" : "OPEN" + index.ToString(CultureInfo.InvariantCulture);

    public static string Command(string addInPath) => $"/R \"{addInPath}\"";

    public static bool IsOpenName(string name) => OpenName.IsMatch(name);

    public static IReadOnlyList<string> Ordered(IReadOnlyDictionary<string, string> values) =>
        [.. values
            .Select(pair => (Match: OpenName.Match(pair.Key), Command: pair.Value))
            .Where(item => item.Match.Success)
            .OrderBy(item => IndexOf(item.Match))
            .Select(item => item.Command)];

    public static bool Loads(string command, string pathOrFolder) =>
        command.IndexOf(pathOrFolder, StringComparison.OrdinalIgnoreCase) >= 0;

    public static IReadOnlyList<string> WithAddIn(IReadOnlyList<string> commands, string addInPath) =>
        commands.Any(command => Loads(command, addInPath)) ? commands : [.. commands, Command(addInPath)];

    public static IReadOnlyList<string> WithoutFolder(IReadOnlyList<string> commands, string installFolder) =>
        [.. commands.Where(command => !Loads(command, installFolder))];

    public static OpenChanges Changes(IReadOnlyDictionary<string, string> current, IReadOnlyList<string> commands)
    {
        var wanted = commands.Select((command, index) => new KeyValuePair<string, string>(NameAt(index), command)).ToList();
        var writes = wanted
            .Where(pair => !current.TryGetValue(pair.Key, out var existing) || !string.Equals(existing, pair.Value, StringComparison.Ordinal))
            .ToList();
        var deletes = current.Keys
            .Where(IsOpenName)
            .Where(name => !wanted.Any(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return new OpenChanges(writes, deletes);
    }

    private static int IndexOf(Match match) =>
        match.Groups["index"].Value is { Length: > 0 } digits ? int.Parse(digits, CultureInfo.InvariantCulture) : 0;
}
