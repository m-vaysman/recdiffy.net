using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public sealed class LogTextTests
{
    private const string Profile = @"C:\Users\pat";

    [Fact]
    public void A_line_has_the_time_level_area_and_message_in_columns()
    {
        var entry = new LogEntry(new DateTimeOffset(2026, 11, 11, 9, 30, 5, 123, TimeSpan.FromHours(-5)), LogSeverity.Warning, LogAreas.Csv, "Rejected broker.csv.");

        Assert.Equal("2026-11-11 09:30:05.123 -05:00  Warning      CSV         Rejected broker.csv.", LogText.Format(entry));
    }

    [Fact]
    public void A_line_does_not_depend_on_the_culture()
    {
        var entry = LogEntries.At(0, LogSeverity.Information, "x");

        var lines = new[] { "en-US", "de-DE", "ar-SA" }.Select(name => ResultSheetFixtures.InCulture(name, () => LogText.Format(entry))).Distinct(StringComparer.Ordinal);

        _ = Assert.Single(lines);
    }

    [Fact]
    public void A_known_path_becomes_its_file_name()
    {
        Assert.Equal(
            "Couldn't read broker.csv: it is locked.",
            LogText.Sanitize(@"Couldn't read D:\shared\trades\broker.csv: it is locked.", [@"D:\shared\trades\broker.csv"], Profile));
    }

    [Fact]
    public void A_quoted_path_becomes_its_file_name()
    {
        Assert.Equal(
            "Could not find file 'broker.csv'. Also \"Book.xlsx\".",
            LogText.Sanitize(@"Could not find file 'D:\shared\broker.csv'. Also ""\\server\share\Book.xlsx"".", [], Profile));
    }

    [Fact]
    public void The_user_profile_folder_is_replaced()
    {
        Assert.Equal(
            @"Access to %USERPROFILE%\AppData\Roaming\RecDiffy is denied.",
            LogText.Sanitize(@"Access to C:\Users\Pat\AppData\Roaming\RecDiffy is denied.", [], Profile));
    }

    [Fact]
    public void Text_with_no_paths_is_unchanged()
    {
        Assert.Equal("Read 5 rows in 0.25 s.", LogText.Sanitize("Read 5 rows in 0.25 s.", [null, string.Empty], Profile));
    }

    [Fact]
    public void Times_and_counts_are_written_the_same_way_everywhere()
    {
        Assert.Equal("1.50 s", LogText.Seconds(TimeSpan.FromMilliseconds(1_500)));
        Assert.Equal("1 row", LogText.Count(1, "row", "rows"));
        Assert.Equal("12,345 rows", ResultSheetFixtures.InCulture("de-DE", () => LogText.Count(12_345, "row", "rows")));
    }
}
