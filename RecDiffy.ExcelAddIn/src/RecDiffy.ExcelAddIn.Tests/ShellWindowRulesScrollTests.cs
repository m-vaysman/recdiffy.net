using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn.Tests;

// Issue #310: the Rules tab must let its field grid scroll sideways, so the remove button
// of each field stays reachable at the window's default and minimum widths.
public class ShellWindowRulesScrollTests
{
    [Fact]
    public void The_Rules_tab_scrolls_sideways_when_its_field_grid_is_wider_than_the_window() => OnStaThread(() =>
    {
        var window = new ShellWindow();

        var rulesTab = Descendants(window).OfType<TabItem>().Single(tab => tab.Header as string == "Rules");
        var scroller = Assert.IsType<ScrollViewer>(rulesTab.Content);

        Assert.Equal(ScrollBarVisibility.Auto, scroller.HorizontalScrollBarVisibility);
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()
            .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static void OnStaThread(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
