using System.Collections.Immutable;

namespace RecDiffy;

/// <summary>Every row pushed so far, and the last view of them. Each push or snapshot returns a new batch.</summary>
/// <param name="Version">How many pushes the batch holds.</param>
public sealed record RecBatch(CompiledProfile Profile, ReferenceDataSet Reference, ImmutableList<RecRow> Left, ImmutableList<RecRow> Right, int Version)
{
    internal RecView? Last { get; init; }

    /// <summary>Whether the last snapshot covers every push, so the next snapshot runs nothing.</summary>
    public bool IsCurrent => Last is { } view && view.Version == Version;
}
