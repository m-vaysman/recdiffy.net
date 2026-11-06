using System.Collections.Immutable;

namespace RecDiffy.Excel.Reports;

internal static class ReportRegistry
{
    public static ImmutableArray<ReportDefinition> All { get; } =
    [
        ExceptionBlotter.Definition,
        SideBySideCompare.Definition,
        RawVsNormalized.Definition,
        LifecycleChains.Definition,
        Allocations.Definition,
        CashLadder.Definition,
        ControlTotals.Definition,
        Scorecard.Definition,
        CascadeFunnel.Definition,
        BreakHeatmap.Definition,
        Ageing.Definition,
        ToleranceUsage.Definition,
        AuditTrail.Definition,
    ];

    public static ReportDefinition Of(ReportKind kind) => All.Single(definition => definition.Kind == kind);
}
