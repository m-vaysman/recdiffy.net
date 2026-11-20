using System.Net;
using System.Text.RegularExpressions;

namespace RecDiffy.ThirteenF.Tests;

public sealed class DecimalAndEdgeCaseTests
{
    private static readonly Regex FloatingPointTypeToken = new(
        @"\b(?:double|float|Half|Double|Single)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    [Fact]
    public void ThirteenF_library_sources_never_use_double_float_or_Half()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var libraryDir = Path.Combine(dir.FullName, "RecDiffy.NET", "src", "RecDiffy.ThirteenF");
        var csFiles = Directory.EnumerateFiles(libraryDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(csFiles);
        var violations = csFiles
            .Where(path => FloatingPointTypeToken.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(100, 125, "25.00")]
    [InlineData(300, 200, "-33.33")]
    [InlineData(300, 500, "66.67")]
    [InlineData(100, 0, "-100.00")]
    [InlineData(1000, 1000, "0.00")]
    public void ComputePercentChange_uses_decimal_rounded_to_two_places(int prior, int current, string expected)
    {
        var actual = ThirteenFReconciler.ComputePercentChange(prior, current);

        Assert.NotNull(actual);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), actual.Value);
    }

    [Fact]
    public void ComputePercentChange_returns_null_when_prior_is_zero_or_missing_or_current_is_missing()
    {
        Assert.Null(ThirteenFReconciler.ComputePercentChange(null, 100m));
        Assert.Null(ThirteenFReconciler.ComputePercentChange(0m, 100m));
        Assert.Null(ThirteenFReconciler.ComputePercentChange(0m, 0m));
        Assert.Null(ThirteenFReconciler.ComputePercentChange(100m, null));
    }

    [Fact]
    public void Shares_only_change_and_billion_dollar_positions_reconcile_without_precision_loss()
    {
        var manager = new ManagerHit("0001067983", "Synthetic Mega Fund");
        var prior = SecParsers.AggregateHoldings(
        [
            new RawHoldingEntry("777777707", "SPLIT CO", "COM", null, "SH", 1_000m, 200_000m, 0),
            new RawHoldingEntry("888888808", "MEGA CAP INC", "COM", null, "SH", 900_000_000m, 150_000_000_000m, 1),
        ]);
        var current = SecParsers.AggregateHoldings(
        [
            new RawHoldingEntry("777777707", "SPLIT CO", "COM", null, "SH", 2_000m, 200_000m, 0),
            new RawHoldingEntry("888888808", "MEGA CAP INC", "COM", null, "SH", 900_000_000m, 165_000_000_000m, 1),
        ]);

        var full = ThirteenFReconciler.Reconcile(manager, prior, current, CancellationToken.None);

        Assert.Equal(2, full.Changed);
        Assert.Equal(150_000_200_000m, full.PriorTotalUsd);
        Assert.Equal(165_000_200_000m, full.CurrentTotalUsd);

        var split = Assert.Single(full.AllRows, row => row.Cusip == "777777707");
        Assert.Equal(HoldingStatus.Changed, split.Status);
        Assert.Equal(100.00m, split.SharesChangePct);
        Assert.Equal(0.00m, split.ValueChangePct);

        var mega = Assert.Single(full.AllRows, row => row.Cusip == "888888808");
        Assert.Equal(HoldingStatus.Changed, mega.Status);
        Assert.Equal(0.00m, mega.SharesChangePct);
        Assert.Equal(10.00m, mega.ValueChangePct);
    }

    [Fact]
    public async Task Cross_2023_cutoff_new_holdings_amendment_normalizes_each_filing_by_its_own_filed_date()
    {
        const string SubmissionsJson = """
            {
              "cik": "0009000077",
              "name": "Cross Cutoff Advisors LP",
              "filings": {
                "recent": {
                  "accessionNumber": ["0009000077-24-000002", "0009000077-22-000001", "0009000077-23-000001"],
                  "filingDate": ["2024-02-14", "2022-11-14", "2023-02-14"],
                  "reportDate": ["2022-09-30", "2022-09-30", "2022-12-31"],
                  "acceptanceDateTime": ["2024-02-14T16:00:00Z", "2022-11-14T16:00:00Z", "2023-02-14T16:00:00Z"],
                  "form": ["13F-HR/A", "13F-HR", "13F-HR"],
                  "primaryDocument": ["primary_doc.xml", "primary_doc.xml", "primary_doc.xml"]
                },
                "files": []
              }
            }
            """;

        const string Base2022ThousandsXml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>BASE HOLDING</nameOfIssuer>
                <titleOfClass>COM</titleOfClass>
                <cusip>123456789</cusip>
                <value>500</value>
                <shrsOrPrnAmt><sshPrnamt>5000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
              </infoTable>
            </informationTable>
            """;

        const string Amendment2024DollarsXml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>CONFIDENTIAL EXPIRED HOLDING</nameOfIssuer>
                <titleOfClass>COM</titleOfClass>
                <cusip>987654321</cusip>
                <value>250000</value>
                <shrsOrPrnAmt><sshPrnamt>2500</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
              </infoTable>
            </informationTable>
            """;

        var coverNew = TestFixtures.ReadText("cover-new-holdings.xml");
        var indexJson = TestFixtures.ReadText("filing-index.json");

        using var handler = new FakeSecMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/submissions/CIK0009000077.json" => FakeSecMessageHandler.Json(SubmissionsJson),
                "/Archives/edgar/data/9000077/000900007724000002/primary_doc.xml" => FakeSecMessageHandler.Xml(coverNew),
                "/Archives/edgar/data/9000077/000900007722000001/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000077/000900007722000001/infotable.xml" => FakeSecMessageHandler.Xml(Base2022ThousandsXml),
                "/Archives/edgar/data/9000077/000900007724000002/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000077/000900007724000002/infotable.xml" => FakeSecMessageHandler.Xml(Amendment2024DollarsXml),
                "/Archives/edgar/data/9000077/000900007723000001/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000077/000900007723000001/infotable.xml" => FakeSecMessageHandler.Xml(Amendment2024DollarsXml),
                _ => FakeSecMessageHandler.Status(HttpStatusCode.NotFound),
            });
        });
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "ops@example.com", clock);

        var filings2022 = await service.GetFilingsAsync("9000077", 2022, CancellationToken.None);
        Assert.Equal(2, filings2022.Count);

        var q3 = filings2022[1];
        var q4 = filings2022[0];
        var comparison = await service.CompareAsync("9000077", q3, q4, null, 1, 10, CancellationToken.None);

        Assert.Equal(750_000m, comparison.PriorTotalUsd);
        Assert.Equal(250_000m, comparison.CurrentTotalUsd);
    }

    [Theory]
    [InlineData("1067983", "0001067983", "1067983")]
    [InlineData("0000107136", "0000107136", "107136")]
    [InlineData("CIK0001067983", "0001067983", "1067983")]
    [InlineData(" cik 93751 ", "0000093751", "93751")]
    public void NormalizeCik_formats_ten_digit_and_archive_directory_segments(string raw, string expectedPadded, string expectedArchive)
    {
        var padded = SecText.NormalizeCik(raw);
        Assert.Equal(expectedPadded, padded);
        Assert.Equal(expectedArchive, SecText.ArchiveCikSegment(padded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000000000")]
    [InlineData("12345678901")]
    [InlineData("CIK-ABC")]
    public void NormalizeCik_rejects_invalid_inputs(string invalid)
    {
        Assert.ThrowsAny<ArgumentException>(() => SecText.NormalizeCik(invalid));
    }
}
