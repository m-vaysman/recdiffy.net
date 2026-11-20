using System.Globalization;
using System.Net;

namespace RecDiffy.ThirteenF.Tests;

public sealed class ReconciliationComparisonAndPagingTests
{
    [Fact]
    public async Task CompareAsync_reconciles_all_statuses_percent_changes_amendments_and_distinct_sh_prn()
    {
        using var handler = CreateScenarioHandler();
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        var filings = await service.GetFilingsAsync("0009000001", 2026, CancellationToken.None);
        var q1 = filings[2];
        var q2 = filings[1];
        var q3 = filings[0];

        var q1ToQ2 = await service.CompareAsync("0009000001", q1, q2, filter: null, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal("0009000001", q1ToQ2.Manager.Cik);
        Assert.Equal("Polaris Synthetic Capital LP", q1ToQ2.Manager.Name);
        Assert.Equal(1, q1ToQ2.Unchanged);
        Assert.Equal(2, q1ToQ2.Changed);
        Assert.Equal(2, q1ToQ2.New);
        Assert.Equal(2, q1ToQ2.Exited);
        Assert.Equal(2_550_000m, q1ToQ2.PriorTotalUsd);
        Assert.Equal(3_600_000m, q1ToQ2.CurrentTotalUsd);
        Assert.Equal(7, q1ToQ2.TotalRows);
        Assert.Equal(7, q1ToQ2.Rows.Count);

        var alpha = Assert.Single(q1ToQ2.Rows, row => row.Cusip == "111111101");
        Assert.Equal(HoldingStatus.Unchanged, alpha.Status);
        Assert.Equal(10_000m, alpha.PriorShares);
        Assert.Equal(10_000m, alpha.CurrentShares);
        Assert.Equal(0.00m, alpha.SharesChangePct);
        Assert.Equal(1_000_000m, alpha.PriorValueUsd);
        Assert.Equal(1_000_000m, alpha.CurrentValueUsd);
        Assert.Equal(0.00m, alpha.ValueChangePct);

        var beta = Assert.Single(q1ToQ2.Rows, row => row.Cusip == "222222202");
        Assert.Equal(HoldingStatus.Changed, beta.Status);
        Assert.Equal(4_500m, beta.PriorShares);
        Assert.Equal(3_000m, beta.CurrentShares);
        Assert.Equal(-33.33m, beta.SharesChangePct);
        Assert.Equal(900_000m, beta.PriorValueUsd);
        Assert.Equal(1_500_000m, beta.CurrentValueUsd);
        Assert.Equal(66.67m, beta.ValueChangePct);

        var epsilonFromNewHoldingsAmendment = Assert.Single(q1ToQ2.Rows, row => row.Cusip == "666666606");
        Assert.Equal(HoldingStatus.New, epsilonFromNewHoldingsAmendment.Status);
        Assert.Null(epsilonFromNewHoldingsAmendment.PriorShares);
        Assert.Equal(4_000m, epsilonFromNewHoldingsAmendment.CurrentShares);
        Assert.Null(epsilonFromNewHoldingsAmendment.SharesChangePct);
        Assert.Null(epsilonFromNewHoldingsAmendment.PriorValueUsd);
        Assert.Equal(800_000m, epsilonFromNewHoldingsAmendment.CurrentValueUsd);
        Assert.Null(epsilonFromNewHoldingsAmendment.ValueChangePct);

        var gammaExited = Assert.Single(q1ToQ2.Rows, row => row.Cusip == "333333303");
        Assert.Equal(HoldingStatus.Exited, gammaExited.Status);
        Assert.Equal(8_000m, gammaExited.PriorShares);
        Assert.Null(gammaExited.CurrentShares);
        Assert.Null(gammaExited.SharesChangePct);
        Assert.Equal(400_000m, gammaExited.PriorValueUsd);
        Assert.Null(gammaExited.CurrentValueUsd);
        Assert.Null(gammaExited.ValueChangePct);

        var deltaPrnExited = Assert.Single(q1ToQ2.Rows, row => row is { Cusip: "444444404", Status: HoldingStatus.Exited });
        Assert.Equal("CONV 3.5%", deltaPrnExited.TitleOfClass);
        var deltaShNew = Assert.Single(q1ToQ2.Rows, row => row is { Cusip: "444444404", Status: HoldingStatus.New });
        Assert.Equal("COM", deltaShNew.TitleOfClass);

        var zeroPrior = Assert.Single(q1ToQ2.Rows, row => row.Cusip == "555555505");
        Assert.Equal(HoldingStatus.Changed, zeroPrior.Status);
        Assert.Equal("Put", zeroPrior.PutCall);
        Assert.Equal(0m, zeroPrior.PriorShares);
        Assert.Equal(100m, zeroPrior.CurrentShares);
        Assert.Null(zeroPrior.SharesChangePct);
        Assert.Null(zeroPrior.ValueChangePct);

        var q2ToQ3 = await service.CompareAsync("0009000001", q2, q3, filter: null, page: 1, pageSize: 20, CancellationToken.None);
        Assert.DoesNotContain(q2ToQ3.Rows, row => row.Cusip == "999999909");
        Assert.Equal(0, handler.CountFor("000900000126000004/infotable.xml"));
        var q3Alpha = Assert.Single(q2ToQ3.Rows, row => row.Cusip == "111111101");
        Assert.Equal(HoldingStatus.Changed, q3Alpha.Status);
        Assert.Equal(25.00m, q3Alpha.SharesChangePct);
        Assert.Equal(25.00m, q3Alpha.ValueChangePct);
        var q3Epsilon = Assert.Single(q2ToQ3.Rows, row => row.Cusip == "666666606");
        Assert.Equal(HoldingStatus.Unchanged, q3Epsilon.Status);
    }

    [Fact]
    public async Task CompareAsync_caches_full_comparison_and_pages_and_filters_server_side_without_refetching()
    {
        using var handler = CreateScenarioHandler();
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        var filings = await service.GetFilingsAsync("0009000001", 2026, CancellationToken.None);
        var q1 = filings[2];
        var q2 = filings[1];

        var page1 = await service.CompareAsync("0009000001", q1, q2, filter: null, page: 1, pageSize: 3, CancellationToken.None);
        var requestsAfterFirstCompare = handler.Requests.Count;

        var page2 = await service.CompareAsync("0009000001", q1, q2, filter: null, page: 2, pageSize: 3, CancellationToken.None);
        var page3 = await service.CompareAsync("0009000001", q1, q2, filter: null, page: 3, pageSize: 3, CancellationToken.None);
        var page4Empty = await service.CompareAsync("0009000001", q1, q2, filter: null, page: 4, pageSize: 3, CancellationToken.None);
        var onlyExited = await service.CompareAsync("0009000001", q1, q2, filter: HoldingStatus.Exited, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Equal(requestsAfterFirstCompare, handler.Requests.Count);

        Assert.Equal(3, page1.Rows.Count);
        Assert.Equal(3, page2.Rows.Count);
        Assert.Single(page3.Rows);
        Assert.Empty(page4Empty.Rows);
        Assert.Equal(7, page1.TotalRows);
        Assert.Equal(2, page2.Page);
        Assert.Equal(3, page2.PageSize);

        var combinedPagedCusips = page1.Rows.Concat(page2.Rows).Concat(page3.Rows).Select(row => (row.Cusip, row.Status)).ToArray();
        Assert.Equal(7, combinedPagedCusips.Distinct().Count());

        Assert.Equal(2, onlyExited.TotalRows);
        Assert.Equal(2, onlyExited.Rows.Count);
        Assert.All(onlyExited.Rows, row => Assert.Equal(HoldingStatus.Exited, row.Status));
        Assert.Equal(1, onlyExited.Unchanged);
        Assert.Equal(2, onlyExited.Changed);
        Assert.Equal(2, onlyExited.New);
        Assert.Equal(2, onlyExited.Exited);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.CompareAsync("0009000001", q1, q2, filter: null, page: 0, pageSize: 10, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.CompareAsync("0009000001", q1, q2, filter: null, page: 1, pageSize: 0, CancellationToken.None));
    }

    [Fact]
    public async Task CompareAsync_is_culture_invariant_when_current_culture_uses_decimal_comma()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var handler = CreateScenarioHandler();
            using var http = new HttpClient(handler);
            var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
            var service = new SecThirteenFService(http, "unit-test@example.com", clock);

            var filings = await service.GetFilingsAsync("0009000001", 2026, CancellationToken.None);
            var comparison = await service.CompareAsync("0009000001", filings[2], filings[1], null, 1, 20, CancellationToken.None);

            Assert.Equal(2_550_000m, comparison.PriorTotalUsd);
            Assert.Equal(3_600_000m, comparison.CurrentTotalUsd);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    private static FakeSecMessageHandler CreateScenarioHandler()
    {
        var rootJson = TestFixtures.ReadText("CIK0009000001.json");
        var indexJson = TestFixtures.ReadText("filing-index.json");
        var coverRestatement = TestFixtures.ReadText("cover-restatement.xml");
        var coverNewHoldings = TestFixtures.ReadText("cover-new-holdings.xml");
        var q1Xml = TestFixtures.ReadText("holdings-q1-base.xml");
        var q2BaseXml = TestFixtures.ReadText("holdings-q2-base.xml");
        var q2NewXml = TestFixtures.ReadText("holdings-q2-new-holdings.xml");
        var q3ErroneousXml = TestFixtures.ReadText("holdings-q3-erroneous.xml");
        var q3RestatedXml = TestFixtures.ReadText("holdings-q3-restated.xml");

        return new FakeSecMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/submissions/CIK0009000001.json" => FakeSecMessageHandler.Json(rootJson),
                "/Archives/edgar/data/9000001/000900000126000005/primary_doc.xml" => FakeSecMessageHandler.Xml(coverRestatement),
                "/Archives/edgar/data/9000001/000900000126000003/primary_doc.xml" => FakeSecMessageHandler.Xml(coverNewHoldings),
                "/Archives/edgar/data/9000001/000900000126000001/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000001/000900000126000001/infotable.xml" => FakeSecMessageHandler.Xml(q1Xml),
                "/Archives/edgar/data/9000001/000900000126000002/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000001/000900000126000002/infotable.xml" => FakeSecMessageHandler.Xml(q2BaseXml),
                "/Archives/edgar/data/9000001/000900000126000003/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000001/000900000126000003/infotable.xml" => FakeSecMessageHandler.Xml(q2NewXml),
                "/Archives/edgar/data/9000001/000900000126000004/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000001/000900000126000004/infotable.xml" => FakeSecMessageHandler.Xml(q3ErroneousXml),
                "/Archives/edgar/data/9000001/000900000126000005/index.json" => FakeSecMessageHandler.Json(indexJson),
                "/Archives/edgar/data/9000001/000900000126000005/infotable.xml" => FakeSecMessageHandler.Xml(q3RestatedXml),
                _ => FakeSecMessageHandler.Status(HttpStatusCode.NotFound),
            });
        });
    }
}
