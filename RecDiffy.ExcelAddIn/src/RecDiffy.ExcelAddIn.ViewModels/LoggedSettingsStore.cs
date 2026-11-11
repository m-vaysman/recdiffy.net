namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class LoggedSettingsStore : ISettingsStore
{
    private readonly ISettingsStore _inner;
    private readonly LogTiming _timing;

    public LoggedSettingsStore(ISettingsStore inner, IAddInLog log, TimeProvider clock)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timing = new LogTiming(log, clock, LogAreas.Settings);
    }

    public AddInSettings Load() =>
        _timing.Run("Loading the settings", _inner.Load, settings => new LogLine(LogSeverity.Information, $"Settings loaded: {Describe(settings)}."));

    public string? Save(AddInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _timing.Run(
            "Saving the settings",
            () => _inner.Save(settings),
            problem => problem is null
                ? new LogLine(LogSeverity.Information, $"Settings saved: {Describe(settings)}.")
                : new LogLine(LogSeverity.Warning, $"Settings weren't saved: {problem}"));
    }

    private static string Describe(AddInSettings settings) =>
        $"exact matches {(settings.IncludeExactMatches ? "listed" : "left out")}, explanation {(settings.OffersExplanation ? "offered" : "hidden")}";
}
