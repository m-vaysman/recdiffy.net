using RecDiffy.ApiBaseline;

namespace RecDiffy.IO.Tests;

// A change to the public surface of RecDiffy.IO fails here, on net8.0 and net10.0, with the lines that changed.
// Accepting an intended change is a manual step: copy the .actual.txt file the failure names over
// ApiBaseline/RecDiffy.IO.txt and commit it. RecDiffy.Tests holds the tests of the comparison itself.
public class PublicApiBaselineTests
{
    [Fact]
    public void The_file_reader_public_api_matches_its_baseline()
    {
        var failure = PublicApiSurface.Check(typeof(FileSources).Assembly, "RecDiffy.IO.Tests");
        Assert.True(failure is null, failure);
    }
}
