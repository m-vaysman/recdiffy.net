using System.Net;

namespace RecDiffy.ThirteenF.Tests;

public sealed class LiveWiringBugFixTests
{
    [Fact]
    public async Task SearchManagersAsync_matches_padded_unpadded_and_prefixed_cik_queries()
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

        var padded = await service.SearchManagersAsync("0009000001", 5, CancellationToken.None);
        var paddedHit = Assert.Single(padded);
        Assert.Equal(new ManagerHit("0009000001", "Polaris Synthetic Capital LP"), paddedHit);

        var unpadded = await service.SearchManagersAsync("9000004", 5, CancellationToken.None);
        var unpaddedHit = Assert.Single(unpadded);
        Assert.Equal(new ManagerHit("0009000004", "NORTHWIND CAPITAL MANAGEMENT LP"), unpaddedHit);

        var cikPrefixed = await service.SearchManagersAsync("CIK 9000002", 5, CancellationToken.None);
        var cikPrefixedHit = Assert.Single(cikPrefixed);
        Assert.Equal(new ManagerHit("0009000002", "PORTAGE HARBOR ADVISORS LLC"), cikPrefixedHit);
    }

    [Fact]
    public async Task SearchManagersAsync_shares_inflight_directory_download_across_cancelled_and_concurrent_keystrokes()
    {
        var q4Bytes = TestFixtures.GzipUtf8(TestFixtures.ReadText("master-2026-q4.idx"));
        var emptyIdx = TestFixtures.GzipUtf8("CIK|Company Name|Form Type|Date Filed|Filename\n");
        var q4Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQ4 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handler = new FakeSecMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/Archives/edgar/full-index/2026/QTR4/master.gz")
            {
                q4Started.TrySetResult();
                await releaseQ4.Task.ConfigureAwait(false);
                return FakeSecMessageHandler.Bytes(q4Bytes);
            }

            return FakeSecMessageHandler.Bytes(emptyIdx);
        });
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        using var firstKeystrokeCts = new CancellationTokenSource();
        var firstSearch = service.SearchManagersAsync("PO", 10, firstKeystrokeCts.Token);
        await q4Started.Task;

        await firstKeystrokeCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstSearch);

        var secondSearch = service.SearchManagersAsync("POL", 10, CancellationToken.None);
        var thirdSearch = service.SearchManagersAsync("PON", 10, CancellationToken.None);

        releaseQ4.TrySetResult();

        var secondHits = await secondSearch;
        var thirdHits = await thirdSearch;

        Assert.Equal("Polaris Synthetic Capital LP", Assert.Single(secondHits).Name);
        Assert.Equal("PONDEROSA RIDGE ASSET MANAGEMENT LLC", Assert.Single(thirdHits).Name);
        Assert.Equal(1, handler.CountFor("QTR4/master.gz"));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task SearchManagersAsync_resets_shared_directory_task_after_transient_failure()
    {
        var q4Bytes = TestFixtures.GzipUtf8(TestFixtures.ReadText("master-2026-q4.idx"));
        var emptyIdx = TestFixtures.GzipUtf8("CIK|Company Name|Form Type|Date Filed|Filename\n");
        var shouldFail = true;

        using var handler = new FakeSecMessageHandler((request, _) =>
        {
            if (shouldFail)
                return Task.FromResult(FakeSecMessageHandler.Status(HttpStatusCode.ServiceUnavailable));

            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path == "/Archives/edgar/full-index/2026/QTR4/master.gz"
                ? FakeSecMessageHandler.Bytes(q4Bytes)
                : FakeSecMessageHandler.Bytes(emptyIdx));
        });
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "unit-test@example.com", clock);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.SearchManagersAsync("PO", 5, CancellationToken.None));

        shouldFail = false;

        var hits = await service.SearchManagersAsync("POL", 5, CancellationToken.None);
        Assert.Equal("Polaris Synthetic Capital LP", Assert.Single(hits).Name);
    }

    [Fact]
    public void ParseInformationTableXml_strips_internal_whitespace_in_cusip_so_quarters_reconcile()
    {
        const string PriorSpacedCusipXml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>APPLE INC</nameOfIssuer>
                <titleOfClass>COM</titleOfClass>
                <cusip>037833 10 0</cusip>
                <value>500000</value>
                <shrsOrPrnAmt><sshPrnamt>2000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
              </infoTable>
            </informationTable>
            """;

        const string CurrentCompactCusipXml = """
            <informationTable xmlns="http://www.sec.gov/edgar/document/thirteenf/informationtable">
              <infoTable>
                <nameOfIssuer>APPLE INC</nameOfIssuer>
                <titleOfClass>COM</titleOfClass>
                <cusip>037833100</cusip>
                <value>550000</value>
                <shrsOrPrnAmt><sshPrnamt>2000</sshPrnamt><sshPrnamtType>SH</sshPrnamtType></shrsOrPrnAmt>
              </infoTable>
            </informationTable>
            """;

        var prior = SecParsers.AggregateHoldings(
            SecParsers.ParseInformationTableXml(PriorSpacedCusipXml, new DateOnly(2026, 8, 14)));
        var current = SecParsers.AggregateHoldings(
            SecParsers.ParseInformationTableXml(CurrentCompactCusipXml, new DateOnly(2026, 11, 16)));

        var reconciled = ThirteenFReconciler.Reconcile(
            new ManagerHit("0001067983", "BERKSHIRE HATHAWAY INC"),
            prior,
            current,
            CancellationToken.None);

        Assert.Equal(0, reconciled.New);
        Assert.Equal(0, reconciled.Exited);
        Assert.Equal(1, reconciled.Changed);
        var row = Assert.Single(reconciled.AllRows);
        Assert.Equal("037833100", row.Cusip);
    }
}
