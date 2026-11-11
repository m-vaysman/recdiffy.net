using System.Globalization;
using System.Text.RegularExpressions;

namespace RecDiffy.ExcelAddIn.ViewModels;

public static class LogText
{
    private static readonly Regex QuotedPath = new(
        """(?<quote>['"])(?<path>(?:[A-Za-z]:\\|\\\\)[^'"]*)\k<quote>""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    public static string Format(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.At:yyyy-MM-dd HH:mm:ss.fff zzz}  {Label(entry.Severity),-11}  {entry.Area,-10}  {entry.Message}");
    }

    public static string Label(LogSeverity severity) => severity switch
    {
        LogSeverity.Information => "Information",
        LogSeverity.Warning => "Warning",
        LogSeverity.Error => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "A log entry is information, a warning or an error."),
    };

    public static string FileName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetFileName(path) is { Length: > 0 } name ? name : path;
    }

    public static string Sanitize(string text, IEnumerable<string?> paths, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(userProfile);
        var known = paths
            .OfType<string>()
            .Where(path => path.Length > 0)
            .OrderByDescending(path => path.Length)
            .Aggregate(text, (current, path) => current.Replace(path, FileName(path), StringComparison.OrdinalIgnoreCase));
        var quoted = QuotedPath.Replace(known, match => $"{match.Groups["quote"].Value}{FileName(match.Groups["path"].Value)}{match.Groups["quote"].Value}");
        return userProfile.Length > 0 ? quoted.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : quoted;
    }

    public static string Sanitize(string text, params IEnumerable<string?> paths) =>
        Sanitize(text, paths, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string Failure(Exception exception, params IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return $"{exception.GetType().Name}: {Sanitize(exception.Message, paths)}";
    }

    public static string Seconds(TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalSeconds:0.00} s");

    public static string Count(long count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {(count == 1 ? one : many)}");
}
