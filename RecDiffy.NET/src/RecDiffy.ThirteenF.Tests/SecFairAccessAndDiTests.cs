using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace RecDiffy.ThirteenF.Tests;

public sealed class SecFairAccessAndDiTests
{
    [Fact]
    public async Task Transport_sends_formatted_user_agent_throttles_under_ten_requests_per_second_and_caches_responses()
    {
        using var handler = new FakeSecMessageHandler((_, _) => Task.FromResult(FakeSecMessageHandler.Json("{}")));
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var throttle = new SecThrottle(clock, TimeSpan.FromMilliseconds(125));
        var cache = new SecResponseCache();
        var transport = new SecTransport(http, SecTransport.FormatUserAgent("compliance@example.org"), throttle, cache);

        var uri1 = new Uri("https://data.sec.gov/submissions/CIK0000000001.json");
        var uri2 = new Uri("https://data.sec.gov/submissions/CIK0000000002.json");
        var uri3 = new Uri("https://data.sec.gov/submissions/CIK0000000003.json");

        await transport.GetStringAsync(uri1, CancellationToken.None);
        await transport.GetStringAsync(uri2, CancellationToken.None);
        await transport.GetStringAsync(uri3, CancellationToken.None);
        await transport.GetStringAsync(uri1, CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, recorded => Assert.Equal("RecDiffy 13F demo compliance@example.org", recorded.UserAgent));
        Assert.Equal([TimeSpan.FromMilliseconds(125), TimeSpan.FromMilliseconds(125)], clock.RecordedDelays);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "403 Forbidden")]
    [InlineData(HttpStatusCode.NotFound, "404 Not Found")]
    [InlineData(HttpStatusCode.TooManyRequests, "429 Too Many Requests")]
    public async Task Status_errors_throw_HttpRequestException_with_status_code_and_clear_message(HttpStatusCode code, string expectedPhrase)
    {
        using var handler = new FakeSecMessageHandler((_, _) => Task.FromResult(FakeSecMessageHandler.Status(code)));
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "ops@example.com", clock);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.GetFilingsAsync("0001067983", 2025, CancellationToken.None));

        Assert.Equal(code, ex.StatusCode);
        Assert.Contains(expectedPhrase, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_is_honoured_before_sending_requests()
    {
        using var handler = new FakeSecMessageHandler((_, _) => Task.FromResult(FakeSecMessageHandler.Json("{}")));
        using var http = new HttpClient(handler);
        var clock = new AutoAdvancingTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var service = new SecThirteenFService(http, "ops@example.com", clock);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchManagersAsync("PO", 5, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetFilingsAsync("0001067983", 2025, cts.Token));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void AddThirteenF_registers_singleton_service_and_validates_contact()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddThirteenF("   "));

        services.AddThirteenF("demo-contact@example.com");
        using var provider = services.BuildServiceProvider();

        var viaInterface = provider.GetRequiredService<IThirteenFService>();
        var viaConcrete = provider.GetRequiredService<SecThirteenFService>();

        Assert.Same(viaConcrete, viaInterface);
    }
}
