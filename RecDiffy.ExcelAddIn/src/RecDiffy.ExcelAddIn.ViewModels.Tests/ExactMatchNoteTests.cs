using RecDiffy.ExcelAddIn.ViewModels;
using static RecDiffy.ExcelAddIn.ViewModels.Tests.ResultSheetFixtures;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class ExactMatchNoteTests
{
    [Theory]
    [InlineData(0, "Not listed below. There aren't any.", null)]
    [InlineData(1, "Not listed below. The counts include it.", "1 exact match isn't listed. The counts include it. Turn exact matches on in Settings.")]
    [InlineData(2, "Not listed below. The counts include all 2 of them.", "2 exact matches aren't listed. The counts include them. Turn them on in Settings.")]
    public async Task The_notes_about_left_out_exact_matches_fit_how_many_there_are(int exact, string sheetNote, string? tableNote)
    {
        SourceTable left = new(["Ref", "Quantity"], [["T-1", 10m], ["T-2", 10m]]) { SheetRows = [2, 3] };
        SourceTable right = new(["Ref", "Quantity"], [["T-1", Quantity(exact, 1)], ["T-2", Quantity(exact, 2)]]);
        var completed = new List<RunOutcome.Completed>();
        var settings = new SettingsViewModel();
        var shell = await ValidShell(Engine(left, right, completed), FakeOutput.Writing(), settings, left, right, rules =>
        {
            rules.Fields[0].Type = FieldType.Identifier;
            rules.Fields[0].Role = FieldRole.Key;
            rules.Fields[1].Type = FieldType.Decimal;
            rules.Fields[1].Role = FieldRole.Economic;
            rules.Fields[1].ScaleText = "0";
        });
        await shell.RunCommand.ExecuteAsync(null);

        settings.IncludeExactMatches = false;
        var sheet = ResultSheets.Build(Assert.Single(completed), ["Ref"], new ResultSheetOptions(IncludeExactMatches: false, IsStale: false));

        Assert.Equal(exact, Assert.Single(completed).Result.Summary.Exact);
        Assert.Equal(sheetNote, TextOf(sheet.Pages[0].Rows[8].Cells[1]));
        Assert.Equal(tableNote, shell.Results.HiddenExactMatches);
    }

    private static decimal Quantity(int exact, int row) => row <= exact ? 10m : 11m;
}
