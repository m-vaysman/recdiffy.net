using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace RecDiffy.ThirteenF.Tests;

internal static class TestFixtures
{
    public static string ReadText(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName), Encoding.UTF8);

    public static byte[] GzipUtf8(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }
}

internal sealed class FakeSecMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    private readonly ConcurrentQueue<(Uri Uri, string UserAgent)> _requests = new();

    public IReadOnlyCollection<(Uri Uri, string UserAgent)> Requests => _requests;

    public int CountFor(string pathSubstring) =>
        _requests.Count(item => item.Uri.AbsoluteUri.Contains(pathSubstring, StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userAgent = request.Headers.UserAgent.ToString();
        _requests.Enqueue((request.RequestUri!, userAgent));
        return responder(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json)),
        };

    public static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(xml)),
        };

    public static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };

    public static HttpResponseMessage Status(HttpStatusCode statusCode) =>
        new(statusCode)
        {
            Content = new ByteArrayContent([]),
        };
}

internal sealed class AutoAdvancingTimeProvider(DateTimeOffset initialUtc) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = initialUtc;
    private readonly List<TimeSpan> _delays = [];

    public IReadOnlyList<TimeSpan> RecordedDelays
    {
        get
        {
            lock (_gate)
            {
                return [.. _delays];
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime > TimeSpan.Zero)
        {
            lock (_gate)
            {
                _delays.Add(dueTime);
                _now += dueTime;
            }
        }

        if (dueTime != Timeout.InfiniteTimeSpan)
            callback(state);

        return NoOpTimer.Instance;
    }

    private sealed class NoOpTimer : ITimer
    {
        public static readonly NoOpTimer Instance = new();

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
