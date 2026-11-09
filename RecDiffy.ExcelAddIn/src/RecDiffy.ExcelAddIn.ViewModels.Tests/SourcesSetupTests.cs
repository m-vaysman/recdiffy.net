using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SourcesSetupTests
{
    [Fact]
    public void Both_cultures_start_as_the_culture_excel_runs_with()
    {
        var sources = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(null));

        Assert.Equal("en-US", sources.Left.Culture.Name);
        Assert.Equal("en-US", sources.Right.Culture.Name);
        Assert.Equal("en-US", SetupProfiles.CultureOf(sources.ToSetup().Profile, Side.Left));
        Assert.Contains(sources.ExcelCulture, sources.Cultures);
    }

    [Fact]
    public async Task Everything_set_on_the_sources_tab_is_in_the_setup()
    {
        var sources = await Configured();

        var setup = sources.ToSetup();

        Assert.Equal(new SideSetup("Your records", new WorkbookSource("Book.xlsx", @"C:\data\Book.xlsx", "Trades", "$A$1:$C$3")), setup.Left);
        Assert.Equal(
            new SideSetup("Broker", new CsvSource(@"C:\data\trades.csv", HasHeader: false, Delimiter: ';', Quote: '\'', Escape: '\\', Encoding: "windows-1252")),
            setup.Right);
        Assert.Equal("de-DE", SetupProfiles.CultureOf(setup.Profile, Side.Left));
        Assert.Equal(string.Empty, SetupProfiles.CultureOf(setup.Profile, Side.Right));
    }

    [Fact]
    public async Task A_setup_applied_to_a_new_window_gives_back_the_same_setup()
    {
        var setup = (await Configured()).ToSetup();
        var fresh = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(null));

        fresh.Apply(setup);

        Assert.Equal(setup, fresh.ToSetup());
        Assert.Equal(setup, SetupJson.Read(SetupJson.Write(fresh.ToSetup())));
    }

    [Fact]
    public async Task Applying_a_setup_reads_csv_files_again_but_asks_for_workbook_data_again()
    {
        var setup = (await Configured()).ToSetup();
        var csv = FakeCsv.Reading();
        var excel = FakeExcel.ReadingTable();
        var fresh = Make.Sources(excel, new FakeDialog(null), csv);

        fresh.Apply(setup);

        Assert.Equal([Assert.IsType<CsvSource>(setup.Right.Source)], csv.Peeked);
        Assert.Equal(0, excel.Reads);
        Assert.False(fresh.Left.HasData);
        Assert.Equal("Book.xlsx · Trades · $A$1:$C$3 · choose the data again to read it", fresh.Left.Summary);
    }

    [Fact]
    public void A_setup_without_declared_cultures_uses_excels()
    {
        var sources = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(null));
        var undeclared = new ReconciliationSetup(new SideSetup(string.Empty, new NoSource()), new SideSetup(string.Empty, new NoSource()), SetupProfiles.Draft());

        sources.Apply(undeclared);

        Assert.Equal(sources.ExcelCulture, sources.Left.Culture);
    }

    private static async Task<SourcesViewModel> Configured()
    {
        var sources = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\trades.csv"));
        sources.Left.Alias = "Your records";
        await sources.Left.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        sources.Left.Culture = CultureChoice.Find(sources.Cultures, "de-DE");
        sources.Right.Alias = "Broker";
        await sources.Right.ChooseCsvFileCommand.ExecuteAsync(null).ConfigureAwait(true);
        sources.Right.CsvHasHeader = false;
        sources.Right.CsvDelimiter = DelimiterChoice.For(';');
        sources.Right.CsvQuote = QuoteChoice.For('\'');
        sources.Right.CsvEscape = EscapeChoice.All[1];
        sources.Right.CsvEncoding = EncodingChoice.For("windows-1252");
        sources.Right.Culture = CultureChoice.Invariant;
        return sources;
    }
}
