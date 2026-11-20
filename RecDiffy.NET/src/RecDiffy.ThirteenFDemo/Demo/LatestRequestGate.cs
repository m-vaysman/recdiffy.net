namespace RecDiffy.ThirteenFDemo.Demo;

public sealed class LatestRequestGate : IDisposable
{
    private CancellationTokenSource? _current;

    public CancellationToken Next()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return _current.Token;
    }

    public void Cancel()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }

    public void Dispose()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }
}
