namespace RecDiffy.Capacity;

internal static class SourceInfo
{
    public static ReportSource Capture(CapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var commit = options.Commit ?? (Git(["rev-parse", "HEAD"]) is { Length: > 0 } sha ? sha : "unknown");
        var reference = options.Ref ?? (Git(["rev-parse", "--abbrev-ref", "HEAD"]) is { Length: > 0 } name ? name : "unknown");
        var status = options.Commit is null ? Git(["status", "--porcelain", "--untracked-files=no"]) : "";
        var tree = status switch
        {
            null => "unknown",
            "" => "clean",
            _ => "dirty",
        };
        return new ReportSource(reference, commit, tree);
    }

    private static string? Git(IReadOnlyList<string> arguments) => ProcessRunner.Run("git", arguments);
}
