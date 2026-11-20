using Microsoft.Extensions.DependencyInjection;

namespace RecDiffy.ThirteenF.Tests;

public sealed class LiveSecSmokeTests
{
    [Fact(Skip = "Optional live SEC EDGAR smoke test; unskip locally to run against sec.gov.")]
    public async Task Reconciles_two_consecutive_quarters_from_live_sec_edgar()
    {
        var services = new ServiceCollection();
        services.AddThirteenF("smoke-test@recdiffy.net");
        await using var provider = services.BuildServiceProvider();
        var thirteenF = provider.GetRequiredService<IThirteenFService>();

        var managers = await thirteenF.SearchManagersAsync("BERKSHIRE HATHAWAY", 5, CancellationToken.None);
        Assert.NotEmpty(managers);

        var berkshire = Assert.Single(managers, hit => hit.Cik == "0001067983");
        var filings = await thirteenF.GetFilingsAsync(berkshire.Cik, 2025, CancellationToken.None);
        Assert.True(filings.Count >= 2);

        var comparison = await thirteenF.CompareAsync(berkshire.Cik, filings[1], filings[0], null, 1, 25, CancellationToken.None);
        Assert.True(comparison.TotalRows > 0);
        Assert.True(comparison.CurrentTotalUsd > 0m);
    }
}
