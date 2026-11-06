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
    CancellationToken Cancellation);

internal sealed record ActiveItem(int Index, Side Side, ConsolidatedRow Row);
