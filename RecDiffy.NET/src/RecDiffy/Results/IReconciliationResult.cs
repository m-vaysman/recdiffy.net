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

    /// <summary>The row's canonical values after consolidation. This is not always the row as it was read.</summary>
    /// <remarks>
    /// A leg of a package returns the package's combined item, so every leg of one package returns the same values.
    /// Identical copies of one row key return the one row they were collapsed into. Every other row returns its own
    /// values. That includes each row of a lifecycle chain: the survivor, and the rows it replaced. The combined row of
    /// a many-to-one or one-to-many entry is not returned here; <see cref="Projection"/> returns it.
    /// </remarks>
    CanonicalRow Canonical(SourceRef row);

    /// <summary>The steps that wrote the row <see cref="Canonical"/> returns, in the order they ran.</summary>
    /// <remarks>For a leg of a package, this is the trace of the package's combined item, not of the leg alone.</remarks>
    IReadOnlyList<TraceStep> Trace(SourceRef row);

    /// <summary>One source cell of the row <see cref="Canonical"/> returns, as invariant text. Null when that row holds no value in the column.</summary>
    /// <remarks>
    /// For a leg of a package, this reads the package's combined item. That item keeps a column only when every leg
    /// holds the same text in it, so a column the legs hold differently returns null.
    /// </remarks>
    string? Raw(SourceRef row, string column);

    string Format(in CanonicalValue value);
}
