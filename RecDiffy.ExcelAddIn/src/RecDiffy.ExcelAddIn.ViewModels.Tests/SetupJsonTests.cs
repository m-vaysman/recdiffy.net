using System.Text.Json;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SetupJsonTests
{
    private static readonly ReconciliationSetup Setup = new(
        new SideSetup("Your records", new WorkbookSource("Book.xlsx", @"C:\data\Book.xlsx", "Trades", "$A$1:$G$6")),
        new SideSetup("Broker", new CsvSource(@"C:\data\broker.csv", HasHeader: false, Delimiter: ';', Quote: '\'', Escape: '\\', Encoding: "windows-1252")),
        SetupProfiles.WithCultures(SetupProfiles.Draft(), "de-DE", string.Empty));

    [Fact]
    public void A_setup_round_trips_through_json_unchanged()
    {
        var read = SetupJson.Read(SetupJson.Write(Setup));

        Assert.Equal(Setup.Profile.ToJson(), read.Profile.ToJson());
        Assert.Equal(Setup, read);
    }

    [Fact]
    public void The_json_names_each_source_kind_and_keeps_the_cultures_in_the_profile()
    {
        using var json = JsonDocument.Parse(SetupJson.Write(Setup));
        var root = json.RootElement;

        Assert.Equal(ReconciliationSetup.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("workbook", root.GetProperty("left").GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal("csv", root.GetProperty("right").GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal("de-DE", root.GetProperty("profile").GetProperty("sources").GetProperty("left").GetProperty("culture").GetString());
        Assert.Equal(string.Empty, root.GetProperty("profile").GetProperty("sources").GetProperty("right").GetProperty("culture").GetString());
    }

    [Theory]
    [InlineData('\t')]
    [InlineData(null)]
    public void Tab_and_detected_delimiters_round_trip(char? delimiter)
    {
        var setup = Setup with { Right = new SideSetup(string.Empty, CsvSource.At(@"C:\data\broker.tsv") with { Delimiter = delimiter }) };

        Assert.Equal(setup, SetupJson.Read(SetupJson.Write(setup)));
    }

    [Fact]
    public void A_workbook_that_was_never_saved_has_no_path_and_round_trips()
    {
        var setup = Setup with { Left = new SideSetup(string.Empty, new WorkbookSource("Book1", null, "Sheet1", "$A$1:$C$9")) };

        var read = SetupJson.Read(SetupJson.Write(setup));

        Assert.Equal(setup, read);
        Assert.Null(Assert.IsType<WorkbookSource>(read.Left.Source).Path);
    }

    [Fact]
    public void An_empty_side_round_trips()
    {
        var setup = Setup with { Left = new SideSetup(string.Empty, new NoSource()) };

        Assert.Equal(setup, SetupJson.Read(SetupJson.Write(setup)));
    }

    [Fact]
    public void Setups_with_different_cultures_are_not_equal() =>
        Assert.NotEqual(Setup, Setup with { Profile = SetupProfiles.WithCultures(Setup.Profile, "fr-FR", string.Empty) });
}
