using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy;

public interface IReconciliationResult
{
    RunInfo Run { get; }

    ImmutableArray<ResultEntry> Entries { get; }

    ImmutableArray<RowResult> Rows { get; }

    RunSummary Summary { get; }

    ISideBySideView BuildSideBySide(SideBySideSpec spec);

    CanonicalRow Projection(GroupId entry, Side side);

    CanonicalRow Canonical(SourceRef row);

    IReadOnlyList<TraceStep> Trace(SourceRef row);

    string? Raw(SourceRef row, string column);

    string Format(in CanonicalValue value);
}
