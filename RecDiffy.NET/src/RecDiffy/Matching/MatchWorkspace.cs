using System.Collections.Immutable;

namespace RecDiffy;

internal sealed record MatchWorkspace(
    string PassId,
    IReadOnlyList<ActiveItem> Items,
    ImmutableHashSet<int> Free,
    IReadOnlyDictionary<string, FieldDeclaration> Schema,
    SearchLimits Limits,
    string? SharedField,
    int MaxGroupSize,
    CancellationToken Cancellation)
{
    // The keys composed for these items under this schema (#396). A copy with other items or another schema is not
    // served by the table: its indexes compose their keys afresh.
    public MatchKeys.Table Keys { get; init; } = MatchKeys.Table.Empty;
}

internal sealed record ActiveItem(int Index, Side Side, ConsolidatedRow Row);
