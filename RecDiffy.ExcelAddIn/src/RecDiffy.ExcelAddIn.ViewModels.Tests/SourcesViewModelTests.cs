using System.ComponentModel;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SourcesViewModelTests
{
    [Theory]
    [InlineData("Left", "", "Left")]
    [InlineData("Left", "   ", "Left")]
    [InlineData("Left", "Your records", "Left · Your records")]
    [InlineData("Right", "  Broker confirmations  ", "Right · Broker confirmations")]
    public void A_side_heading_shows_the_alias_only_when_there_is_one(string side, string alias, string expected) =>
        Assert.Equal(expected, SourcesViewModel.SideHeading(side, alias));

    [Fact]
    public void Typing_an_alias_updates_that_side_only()
    {
        var sources = new SourcesViewModel();
        var changed = Changes(sources);

        sources.LeftAlias = "Your records";

        Assert.Equal("Left · Your records", sources.LeftHeading);
        Assert.Equal("Right", sources.RightHeading);
        Assert.Equal([nameof(SourcesViewModel.LeftAlias), nameof(SourcesViewModel.LeftHeading)], changed);
    }

    [Fact]
    public void Sources_are_not_complete_before_any_data_is_selected() =>
        Assert.False(new SourcesViewModel().IsComplete);

    private static List<string?> Changes(INotifyPropertyChanged source)
    {
        var names = new List<string?>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }
}
