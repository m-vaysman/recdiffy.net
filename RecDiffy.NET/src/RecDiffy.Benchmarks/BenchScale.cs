namespace RecDiffy.Benchmarks;

// Initial COBBLER1 sample. Rows are per side.
// Raise Small and Large to grow CascadeScaleBench and ExecutionModeBench together.
// LaunchCount, WarmupCount, and IterationCount are the short job both of those classes use.
internal static class BenchScale
{
    public const int Small = 1_000;
    public const int Large = 4_000;

    public const int LaunchCount = 1;
    public const int WarmupCount = 1;
    public const int IterationCount = 3;
}
