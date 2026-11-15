using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;
using Xunit.Abstractions;

namespace RecDiffy.ExcelAddIn.Tests;

// Issue #310: the Rules tab must let its field grid scroll sideways, so the remove button
// of each field stays reachable at the window's default and minimum widths.
public class ShellWindowRulesScrollTests(ITestOutputHelper output)
{
    private const string LongProblem =
        "Choose the left and right data on the Sources tab first, then pair the columns for each field you add, "
        + "so every key matches the records you expect to see on both sides of the reconciliation.";

    private const string LongNotice =
        "This setup came from a file this version only reads. Its rules are shown here, but they can't be changed in this window, "
        + "so change them in the file and open it again.";

    [Fact]
    public void The_Rules_tab_scrolls_sideways_when_its_field_grid_is_wider_than_the_window() => OnStaThread(() =>
    {
        var window = new ShellWindow();

        var rulesTab = Descendants(window).OfType<TabItem>().Single(tab => tab.Header as string == "Rules");
        var scroller = Assert.IsType<ScrollViewer>(rulesTab.Content);

        Assert.Equal(ScrollBarVisibility.Auto, scroller.HorizontalScrollBarVisibility);
    });

    [Fact]
    public void No_wrapping_text_on_the_Rules_tab_is_clipped_or_wider_than_the_viewport() => OnStaThread(() =>
    {
        using var laid = RulesTabLaidOut(withField: true);
        var viewport = laid.Scroller.ViewportWidth;

        var wrapping = Visuals(laid.Scroller).OfType<TextBlock>()
            .Where(text => text.TextWrapping == TextWrapping.Wrap && text.IsVisible)
            .Select(text => (Text: Shorten(text.Text), Right: InScroller(text, laid.Scroller).Right, Clip: LayoutInformation.GetLayoutClip(text)))
            .ToList();
        output.WriteLine($"Viewport {Format(viewport)} px; visible wrapping text blocks {wrapping.Count}");
        foreach (var item in wrapping)
        {
            output.WriteLine($"  right edge {Format(item.Right)} px, layout clip {(item.Clip is null ? "none" : "set")}  {item.Text}");
        }

        Assert.NotEmpty(wrapping);
        Assert.All(wrapping, item => Assert.True(item.Right <= viewport + 0.5, $"'{item.Text}' ends at {Format(item.Right)} px, viewport {Format(viewport)} px."));
        Assert.All(wrapping, item => Assert.Null(item.Clip));
    });

    [Fact]
    public void Add_field_lies_inside_the_viewport_without_scrolling() => OnStaThread(() =>
    {
        using var laid = RulesTabLaidOut(withField: true);
        var viewport = laid.Scroller.ViewportWidth;

        var button = Visuals(laid.Scroller).OfType<Button>().Single(item => item.Content as string == "Add field");
        var bounds = InScroller(button, laid.Scroller);
        output.WriteLine($"Viewport {Format(viewport)} px; Add field {Format(bounds.Left)} to {Format(bounds.Right)} px");

        Assert.True(bounds.Left >= -0.5 && bounds.Right <= viewport + 0.5, $"Add field spans {Format(bounds.Left)} to {Format(bounds.Right)} px, viewport {Format(viewport)} px.");
    });

    [Fact]
    public void A_field_row_remove_button_is_inside_the_viewport_or_reachable_by_scrolling() => OnStaThread(() =>
    {
        using var laid = RulesTabLaidOut(withField: true);
        var viewport = laid.Scroller.ViewportWidth;
        var remove = Visuals(laid.Scroller).OfType<Button>().Single(item => AutomationProperties.GetName(item) == "Remove field");

        var initial = InScroller(remove, laid.Scroller);
        output.WriteLine($"Viewport {Format(viewport)} px; field remove at offset 0 spans {Format(initial.Left)} to {Format(initial.Right)} px; scrollable {Format(laid.Scroller.ScrollableWidth)} px");
        if (initial.Left >= -0.5 && initial.Right <= viewport + 0.5)
        {
            return;
        }

        laid.Scroller.ScrollToRightEnd();
        laid.Window.UpdateLayout();
        var scrolled = InScroller(remove, laid.Scroller);
        output.WriteLine($"After scrolling to the right end: field remove spans {Format(scrolled.Left)} to {Format(scrolled.Right)} px");
        Assert.True(scrolled.Left >= -0.5 && scrolled.Right <= viewport + 0.5, "The field remove button can't be reached by scrolling sideways.");
    });

    [Fact]
    public void Add_pass_and_the_pass_remove_button_lie_inside_the_viewport_without_scrolling() => OnStaThread(() =>
    {
        using var laid = RulesTabLaidOut(withField: true);
        var viewport = laid.Scroller.ViewportWidth;

        var addPass = Visuals(laid.Scroller).OfType<Button>().Single(item => item.Content as string == "Add pass");
        var remove = Visuals(laid.Scroller).OfType<Button>().Single(item => (AutomationProperties.GetName(item) ?? string.Empty).StartsWith("Remove Pass", StringComparison.Ordinal));
        var addBounds = InScroller(addPass, laid.Scroller);
        var removeBounds = InScroller(remove, laid.Scroller);
        output.WriteLine($"Viewport {Format(viewport)} px; Add pass {Format(addBounds.Left)} to {Format(addBounds.Right)} px; pass remove {Format(removeBounds.Left)} to {Format(removeBounds.Right)} px");

        Assert.True(addBounds.Left >= -0.5 && addBounds.Right <= viewport + 0.5, $"Add pass spans {Format(addBounds.Left)} to {Format(addBounds.Right)} px, viewport {Format(viewport)} px.");
        Assert.True(removeBounds.Left >= -0.5 && removeBounds.Right <= viewport + 0.5, $"The pass remove button spans {Format(removeBounds.Left)} to {Format(removeBounds.Right)} px, viewport {Format(viewport)} px.");
    });

    [Fact]
    public void With_no_fields_the_Rules_tab_does_not_scroll_sideways() => OnStaThread(() =>
    {
        using var laid = RulesTabLaidOut(withField: false);
        output.WriteLine($"Viewport {Format(laid.Scroller.ViewportWidth)} px; scrollable width {Format(laid.Scroller.ScrollableWidth)} px");

        Assert.True(laid.Scroller.ScrollableWidth <= 0.5, $"The tab scrolls {Format(laid.Scroller.ScrollableWidth)} px sideways with no fields.");
    });

    // Lays the window out off screen at 960 x 800 with the Rules tab selected. The view model has one pass,
    // one compared field (or, without fields, one comparison) and long problem messages. Layout runs twice
    // because the viewport width reaches the bindings only after one pass.
    private static Laid RulesTabLaidOut(bool withField)
    {
        var shell = new ShellViewModel(new SourcesViewModel(new SourceServices(new NoExcel(), new NoCsv(), new NoDialogs()), CultureInfo.InvariantCulture), new NoRuns());
        var rules = shell.Rules;
        if (withField)
        {
            rules.AddFieldCommand.Execute(null);
            var field = rules.Fields.Single();
            field.Name = "Amount";
            field.Role = FieldRole.Economic;
            field.Problem = LongProblem;
            rules.Comparisons.Single().Problem = LongProblem;
        }
        else
        {
            rules.Comparisons.Add(new ComparisonRuleViewModel("Amount") { Problem = LongProblem });
        }
        rules.AddPassCommand.Execute(null);
        rules.Passes.Single().Problem = LongProblem;
        rules.Problems = [LongProblem];
        rules.NotEditable = LongNotice;
        shell.SelectedTabIndex = 1;

        var window = new ShellWindow { DataContext = shell, Left = -30000, Top = -30000, ShowActivated = false };
        window.Show();
        try
        {
            window.UpdateLayout();
            window.UpdateLayout();
            var rulesTab = Descendants(window).OfType<TabItem>().Single(tab => tab.Header as string == "Rules");
            return new Laid(window, Assert.IsType<ScrollViewer>(rulesTab.Content));
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static Rect InScroller(FrameworkElement element, ScrollViewer scroller) =>
        element.TransformToAncestor(scroller).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()
            .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root) =>
        Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root))
            .Select(index => VisualTreeHelper.GetChild(root, index))
            .SelectMany(child => new[] { child }.Concat(Visuals(child)));

    private static string Format(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private sealed class Laid(ShellWindow window, ScrollViewer scroller) : IDisposable
    {
        public ShellWindow Window { get; } = window;

        public ScrollViewer Scroller { get; } = scroller;

        public void Dispose() => Window.Close();
    }

    private sealed class NoExcel : IExcelSources
    {
        public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RangeChoice> PickOpenRangeAsync(string prompt, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoCsv : ICsvSources
    {
        public Task<CsvPeek> PeekAsync(CsvSource source, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public string? ChooseWorkbook(string title) => null;

        public string? ChooseCsvFile(string title) => null;
    }

    private sealed class NoRuns : IReconciliationRuns
    {
        public Task<RunOutcome> RunAsync(RunRequest request, IProgress<RunProgress> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

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
