using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// #92: a row no route accepts reaches the reports as its own entry. The blotter labels it "(not routed)" with the routes action, and the
// route step never shows as a pass in the funnel or the scorecard. The synthetic run routes only Bond rows here.
public class UnroutedEntryReportTests
{
    private static readonly Lazy<Facts.ReportFacts> Run = new(() =>
    {
        var json = SyntheticData.ProfileJson.Replace("\"route\": { \"rule\": \"route.all\" }", "\"route\": { \"rule\": \"route.byField\", \"field\": \"Asset\", \"equals\": \"Bond\" }", StringComparison.Ordinal);
        var profile = Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore());
        var result = Reconciler.Run(profile, SyntheticData.Source("ours", SyntheticData.Ours), SyntheticData.Source("theirs", SyntheticData.Theirs), ReferenceDataSet.Empty);
        return RecDiffyExcel.Context(result, new ExcelReportOptions { AsOf = SyntheticData.AsOf, Profile = profile }).Facts;
    });

    [Fact]
    public void An_unrouted_entry_is_an_open_item_labelled_not_routed()
    {
        var unrouted = Run.Value.OpenItems.Where(item => item.Entry is { Entry.PathId: "" }).ToArray();

        Assert.NotEmpty(unrouted);
        Assert.All(unrouted, item => Assert.Equal("(not routed)", item.Field));
        Assert.All(unrouted, item => Assert.Contains("check the profile routes", item.Action, StringComparison.Ordinal));
    }

    [Fact]
    public void The_route_step_is_not_a_pass() =>
        Assert.DoesNotContain("route", Run.Value.Passes, StringComparer.Ordinal);
}
