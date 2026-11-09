using System.ComponentModel;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class ShellViewModelTests
{
    [Fact]
    public void A_new_shell_opens_on_sources_and_cannot_run()
    {
        var shell = new ShellViewModel(new SourcesViewModel());

        Assert.Equal(ShellTab.Sources, shell.SelectedTab);
        Assert.Equal(0, shell.SelectedTabIndex);
        Assert.True(shell.ShowsReviewRules);
        Assert.False(shell.CanRun);
        Assert.Equal("Select the left and right data to begin.", shell.Status);
    }

    [Fact]
    public void Review_rules_moves_to_the_rules_tab_and_hides_itself()
    {
        var shell = new ShellViewModel(new SourcesViewModel());
        var changed = Changes(shell);

        shell.ReviewRulesCommand.Execute(null);

        Assert.Equal(ShellTab.Rules, shell.SelectedTab);
        Assert.Equal(1, shell.SelectedTabIndex);
        Assert.False(shell.ShowsReviewRules);
        Assert.Equal(
            [nameof(ShellViewModel.SelectedTab), nameof(ShellViewModel.SelectedTabIndex), nameof(ShellViewModel.ShowsReviewRules)],
            changed);
    }

    [Theory]
    [InlineData(0, ShellTab.Sources)]
    [InlineData(1, ShellTab.Rules)]
    [InlineData(2, ShellTab.Results)]
    [InlineData(3, ShellTab.Settings)]
    public void The_tab_index_from_the_view_selects_that_tab(int index, ShellTab expected)
    {
        var shell = new ShellViewModel(new SourcesViewModel()) { SelectedTabIndex = index };

        Assert.Equal(expected, shell.SelectedTab);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void A_tab_index_outside_the_tabs_keeps_the_current_tab(int index)
    {
        var shell = new ShellViewModel(new SourcesViewModel()) { SelectedTab = ShellTab.Results };

        shell.SelectedTabIndex = index;

        Assert.Equal(ShellTab.Results, shell.SelectedTab);
    }

    [Fact]
    public void A_shell_needs_its_sources() =>
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(null!));

    private static List<string?> Changes(INotifyPropertyChanged source)
    {
        var names = new List<string?>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }
}
