using System.Net;

namespace RecDiffy.ThirteenF.Tests;

public sealed class ManagerSearchAndFilingsTests
{
    [Fact]
    public async Task SearchManagersAsync_filters_to_13f_filers_by_case_insensitive_prefix_deduplicates_latest_name_and_caches()
    {
        var q4Bytes = TestFixtures.GzipUtf8(TestFixtures.ReadText("master-2026-q4.idx"));
        var q3Bytes = TestFixtures.GzipUtf8(TestFixtures.ReadText("master-2026-q3.idx"));
        var emptyIdx = TestFixtures.GzipUtf8("CIK|Company Name|Form Type|Date Filed|Filename\n");

        using var handler = new FakeSecMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/Archives/edgar/full-index/2026/QTR4/master.gz" => FakeSecMessageHandler.Bytes(q4Bytes),
                "/Archives/edgar/full-index/2026/QTR3/master.gz" => FakeSecMessageHandler.Bytes(q3Bytes),
                "/Archives/edgar/full-index/2026/QTR2/master.gz" => FakeSecMessageHandler.Bytes(emptyIdx),
                "/Archives/edgar/full-index/2026/QTR1/master.gz" => FakeSecMessageHandler.Bytes(emptyIdx),
                _ => FakeSecMessageHandler.Status(HttpStatusCode.NotFound),
            });
        });
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        var hits = await service.SearchManagersAsync("po", 10, CancellationToken.None);

        Assert.Equal(3, hits.Count);
        Assert.Equal(new ManagerHit("0009000001", "Polaris Synthetic Capital LP"), hits[0]);
        Assert.Equal(new ManagerHit("0009000005", "PONDEROSA RIDGE ASSET MANAGEMENT LLC"), hits[1]);
        Assert.Equal(new ManagerHit("0009000002", "PORTAGE HARBOR ADVISORS LLC"), hits[2]);

        var requestCountAfterFirstLoad = handler.Requests.Count;
        Assert.Equal(4, requestCountAfterFirstLoad);

        var topTwo = await service.SearchManagersAsync("  PO ", 2, CancellationToken.None);
        Assert.Equal(2, topTwo.Count);
        Assert.Equal(requestCountAfterFirstLoad, handler.Requests.Count);

        var blank = await service.SearchManagersAsync("   ", 10, CancellationToken.None);
        Assert.Empty(blank);
    }

    [Fact]
    public async Task SearchManagersAsync_skips_missing_current_quarter_index_at_start_of_quarter()
    {
        var q3Bytes = TestFixtures.GzipUtf8(TestFixtures.ReadText("master-2026-q3.idx"));
        var emptyIdx = TestFixtures.GzipUtf8("CIK|Company Name|Form Type|Date Filed|Filename\n");

        using var handler = new FakeSecMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/Archives/edgar/full-index/2026/QTR4/master.gz" => FakeSecMessageHandler.Status(HttpStatusCode.NotFound),
                "/Archives/edgar/full-index/2026/QTR3/master.gz" => FakeSecMessageHandler.Bytes(q3Bytes),
                "/Archives/edgar/full-index/2026/QTR2/master.gz" => FakeSecMessageHandler.Bytes(emptyIdx),
                "/Archives/edgar/full-index/2026/QTR1/master.gz" => FakeSecMessageHandler.Bytes(emptyIdx),
                "/Archives/edgar/full-index/2025/QTR4/master.gz" => FakeSecMessageHandler.Bytes(emptyIdx),
                _ => FakeSecMessageHandler.Status(HttpStatusCode.NotFound),
            });
        });
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        var hits = await service.SearchManagersAsync("NO", 5, CancellationToken.None);

        var single = Assert.Single(hits);
        Assert.Equal("0009000004", single.Cik);
        Assert.Equal("NORTHWIND CAPITAL MANAGEMENT LP", single.Name);
    }

    [Fact]
    public async Task GetFilingsAsync_applies_amendment_rules_orders_newest_first_and_loads_archive_pages_only_for_older_years()
    {
        var rootJson = TestFixtures.ReadText("CIK0009000001.json");
        var archiveJson = TestFixtures.ReadText("CIK0009000001-submissions-001.json");
        var coverRestatement = TestFixtures.ReadText("cover-restatement.xml");
        var coverNewHoldings = TestFixtures.ReadText("cover-new-holdings.xml");

        using var handler = new FakeSecMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/submissions/CIK0009000001.json" => FakeSecMessageHandler.Json(rootJson),
                "/submissions/CIK0009000001-submissions-001.json" => FakeSecMessageHandler.Json(archiveJson),
                "/Archives/edgar/data/9000001/000900000126000005/primary_doc.xml" => FakeSecMessageHandler.Xml(coverRestatement),
                "/Archives/edgar/data/9000001/000900000126000003/primary_doc.xml" => FakeSecMessageHandler.Xml(coverNewHoldings),
                _ => FakeSecMessageHandler.Status(HttpStatusCode.NotFound),
            });
        });
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        var filings2026 = await service.GetFilingsAsync("9000001", 2026, CancellationToken.None);

        Assert.Equal(0, handler.CountFor("submissions-001.json"));
        Assert.Equal(3, filings2026.Count);

        Assert.Equal(
            new FilingRef("0009000001", new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 16), "0009000001-26-000005", "13F-HR/A"),
            filings2026[0]);
        Assert.Equal(
            new FilingRef("0009000001", new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 20), "0009000001-26-000003", "13F-HR/A"),
            filings2026[1]);
        Assert.Equal(
            new FilingRef("0009000001", new DateOnly(2026, 3, 31), new DateOnly(2026, 5, 15), "0009000001-26-000001", "13F-HR"),
            filings2026[2]);

        var filings2022 = await service.GetFilingsAsync("CIK0009000001", 2022, CancellationToken.None);

        Assert.Equal(1, handler.CountFor("submissions-001.json"));
        Assert.Equal(2, filings2022.Count);
        Assert.Equal(new DateOnly(2022, 12, 31), filings2022[0].PeriodOfReport);
        Assert.Equal(new DateOnly(2022, 9, 30), filings2022[1].PeriodOfReport);
    }

    [Fact]
    public void ApplyAmendmentRules_handles_restatement_after_new_holdings()
    {
        var period = new DateOnly(2026, 6, 30);
        SubmissionFiling[] filings =
        [
            new("0001-26-000001", period, new DateOnly(2026, 8, 10), "1", "13F-HR", "primary_doc.xml"),
            new("0001-26-000002", period, new DateOnly(2026, 8, 15), "2", "13F-HR/A", "primary_doc.xml", AmendmentKind.NewHoldings),
            new("0001-26-000003", period, new DateOnly(2026, 8, 20), "3", "13F-HR/A", "primary_doc.xml", AmendmentKind.Restatement),
            new("0001-26-000004", period, new DateOnly(2026, 8, 25), "4", "13F-HR/A", "primary_doc.xml", AmendmentKind.NewHoldings),
        ];

        var active = SecParsers.ApplyAmendmentRules(filings);

        Assert.Equal(["0001-26-000003", "0001-26-000004"], active.Select(item => item.AccessionNumber), StringComparer.Ordinal);
    }
}
