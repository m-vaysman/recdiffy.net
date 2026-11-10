using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.Results;
using static RecDiffy.ExcelAddIn.ViewModels.Tests.ResultSheetFixtures;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SettingsTests
{
    [Fact]
    public void Both_settings_are_on_by_default()
    {
        var settings = new SettingsViewModel();

        Assert.True(settings.IncludeExactMatches);
        Assert.True(settings.OffersExplanation);
        Assert.Null(settings.Problem);
    }

    [Fact]
    public void Settings_start_from_the_store_and_each_change_is_saved()
    {
        var store = new FakeSettingsStore(new AddInSettings(IncludeExactMatches: false, OffersExplanation: true));
        var settings = new SettingsViewModel(store);
        Assert.False(settings.IncludeExactMatches);
        Assert.Empty(store.Saved);

        settings.OffersExplanation = false;
        settings.IncludeExactMatches = true;

        Assert.Equal([new AddInSettings(false, false), new AddInSettings(true, false)], store.Saved);
    }

    [Fact]
    public void A_setting_that_cannot_be_saved_still_applies_and_says_why()
    {
        var settings = new SettingsViewModel(new FakeSettingsStore(AddInSettings.Default, "Access to the path is denied."));

        settings.IncludeExactMatches = false;

        Assert.False(settings.IncludeExactMatches);
        Assert.Equal("Your settings apply until Excel closes, but they couldn't be saved: Access to the path is denied.", settings.Problem);
    }

    [Fact]
    public void Session_settings_keep_what_was_saved()
    {
        var store = new SessionSettings();

        Assert.Null(store.Save(new AddInSettings(false, false)));

        Assert.Equal(new AddInSettings(false, false), store.Load());
    }

    [Fact]
    public async Task Leaving_out_exact_matches_changes_the_table_not_the_results_or_counts()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        await shell.RunCommand.ExecuteAsync(null);
        var results = shell.Results;
        var run = results.Run;
        Assert.Equal(results.Run!.Rows, results.ListedRows);

        shell.Settings.IncludeExactMatches = false;

        Assert.False(results.IncludeExactMatches);
        Assert.Same(run, results.Run);
        Assert.Equal(results.Run.Rows.Count - 2, results.ListedRows.Count);
        Assert.DoesNotContain(results.ListedRows, row => row.Entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact });
        Assert.Equal("2 exact matches aren't listed. The counts include them. Turn them on in Settings.", results.HiddenExactMatches);
        shell.Settings.IncludeExactMatches = true;
        Assert.Equal(results.Run.Rows, results.ListedRows);
        Assert.Null(results.HiddenExactMatches);
    }

    [Fact]
    public async Task Hiding_exact_matches_clears_a_selected_exact_match()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        await shell.RunCommand.ExecuteAsync(null);
        var results = shell.Results;
        results.Selected = results.Run!.Rows.First(row => ResultSheets.IsExactMatch(row.Entry));

        shell.Settings.IncludeExactMatches = false;

        Assert.Null(results.Selected);
    }

    [Fact]
    public async Task Hiding_exact_matches_keeps_a_selected_break()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        await shell.RunCommand.ExecuteAsync(null);
        var results = shell.Results;
        var selected = results.Run!.Rows.First(row => row.IsBreak);
        results.Selected = selected;

        shell.Settings.IncludeExactMatches = false;

        Assert.Same(selected, results.Selected);
    }

    [Fact]
    public async Task A_shell_starts_with_exact_matches_left_out_when_the_settings_say_so()
    {
        var settings = new SettingsViewModel(new FakeSettingsStore(new AddInSettings(IncludeExactMatches: false, OffersExplanation: true)));
        var shell = await ValidShell(FakeOutput.Writing(), settings);

        await shell.RunCommand.ExecuteAsync(null);

        Assert.False(shell.Results.IncludeExactMatches);
        Assert.Equal(shell.Results.Run!.Rows.Count - 2, shell.Results.ListedRows.Count);
    }

    [Fact]
    public async Task Turning_off_the_explanation_hides_it_and_keeps_its_data()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        Assert.False(shell.ShowsExplanation);
        await shell.RunCommand.ExecuteAsync(null);
        Assert.True(shell.ShowsExplanation);
        var results = shell.Results;
        results.Selected = results.Run!.Rows[0];
        var explanation = Flattened(results.Explanation);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.Settings.OffersExplanation = false;

        Assert.False(shell.ShowsExplanation);
        Assert.Contains(nameof(ShellViewModel.ShowsExplanation), changed, StringComparer.Ordinal);
        Assert.Equal(explanation, Flattened(results.Explanation));
        Assert.NotEmpty(results.Explanation);
    }

    private static IReadOnlyList<(string Title, string Lines)> Flattened(IReadOnlyList<ExplanationSection> sections) =>
        [.. sections.Select(section => (section.Title, string.Join("|", section.Lines)))];

    [Fact]
    public void A_shell_needs_its_output_and_settings()
    {
        var sources = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\Book.xlsx"));

        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(sources, FakeRuns.Unused(), null!, new SettingsViewModel()));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(sources, FakeRuns.Unused(), FakeOutput.Writing(), null!));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(null!));
    }
}
