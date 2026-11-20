using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace RecDiffy.ThirteenF;

internal static class SecSharedHttp
{
    private static readonly SocketsHttpHandler Handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    };

    public static readonly HttpClient Client = new(Handler, disposeHandler: false)
    {
        Timeout = TimeSpan.FromSeconds(60),
    };
}

internal sealed class SecThrottle(TimeProvider timeProvider, TimeSpan minInterval)
{
    public static readonly SecThrottle ProcessDefault = new(TimeProvider.System, TimeSpan.FromMilliseconds(125));

    private readonly Lock _gate = new();
    private DateTimeOffset _nextSlotUtc = DateTimeOffset.MinValue;

    public ValueTask WaitTurnAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        TimeSpan delay;
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            var slot = now >= _nextSlotUtc ? now : _nextSlotUtc;
            _nextSlotUtc = slot + minInterval;
            delay = slot - now;
        }

        return delay > TimeSpan.Zero
            ? new ValueTask(Task.Delay(delay, timeProvider, ct))
            : ValueTask.CompletedTask;
    }
}

internal sealed class SecResponseCache
{
    public static readonly SecResponseCache ProcessDefault = new();

    private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    public bool TryGet(Uri uri, out byte[] bytes) => _entries.TryGetValue(uri.AbsoluteUri, out bytes!);

    public void Store(Uri uri, byte[] bytes) => _entries[uri.AbsoluteUri] = bytes;
}

internal sealed class SecTransport(HttpClient httpClient, string userAgent, SecThrottle throttle, SecResponseCache cache)
{
    public string UserAgent => userAgent;

    public static string FormatUserAgent(string userAgentContact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgentContact);
        return $"RecDiffy 13F demo {userAgentContact.Trim()}";
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(uri, ct).ConfigureAwait(false);
        return DecodeUtf8OrDecompressGzip(bytes);
    }

    public async Task<byte[]> GetBytesAsync(Uri uri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (cache.TryGet(uri, out var cached))
            return cached;

        await throttle.WaitTurnAsync(ct).ConfigureAwait(false);
        if (cache.TryGet(uri, out cached))
            return cached;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateStatusException(uri, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        cache.Store(uri, bytes);
        return bytes;
    }

    internal static string DecodeUtf8OrDecompressGzip(byte[] bytes)
    {
        if (bytes is [0x1F, 0x8B, ..])
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        return DecodeText(bytes);
    }

    private static string DecodeText(byte[] bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3),
        _ => Encoding.UTF8.GetString(bytes),
    };

    private static HttpRequestException CreateStatusException(Uri uri, HttpStatusCode statusCode)
    {
        var message = statusCode switch
        {
            HttpStatusCode.Forbidden =>
                $"SEC EDGAR rejected the request with 403 Forbidden ({uri}). Provide a valid User-Agent contact per SEC fair-access policy and verify the client IP is not blocked.",
            HttpStatusCode.NotFound =>
                $"SEC EDGAR resource was not found with 404 Not Found ({uri}). Verify the CIK, accession number, or filing document path.",
            HttpStatusCode.TooManyRequests =>
                $"SEC EDGAR rate limit exceeded with 429 Too Many Requests ({uri}). Automated traffic must stay under 10 requests per second.",
            _ =>
                $"SEC EDGAR request failed with HTTP {((int)statusCode).ToInvariant()} ({statusCode}) for {uri}.",
        };
        return new HttpRequestException(message, inner: null, statusCode);
    }
}
