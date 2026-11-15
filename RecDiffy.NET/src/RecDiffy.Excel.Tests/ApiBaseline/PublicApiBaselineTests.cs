using RecDiffy.ApiBaseline;

namespace RecDiffy.Excel.Tests;

// A change to the public surface of RecDiffy.Excel fails here, on net8.0 and net10.0, with the lines that changed.
// Accepting an intended change is a manual step: copy the .actual.txt file the failure names over
// ApiBaseline/RecDiffy.Excel.txt and commit it. RecDiffy.Tests holds the tests of the comparison itself.
public class PublicApiBaselineTests
{
    [Fact]
    public void The_report_writer_public_api_matches_its_baseline()
    {
        var failure = PublicApiSurface.Check(typeof(RecDiffyExcel).Assembly, "RecDiffy.Excel.Tests");
        Assert.True(failure is null, failure);
    }
}
