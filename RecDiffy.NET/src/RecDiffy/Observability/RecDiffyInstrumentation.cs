using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RecDiffy;

internal static class RecDiffyInstrumentation
{
    public static ActivitySource Source { get; } = new("RecDiffy");

    public static Meter Meter { get; } = new("RecDiffy");

    public static Counter<int> RowsRead { get; } = Meter.CreateCounter<int>("recdiffy.rows.read");

    public static Counter<int> Commits { get; } = Meter.CreateCounter<int>("recdiffy.commits");

    public static Counter<int> Breaks { get; } = Meter.CreateCounter<int>("recdiffy.breaks");
}
