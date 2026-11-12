using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy;

// A row belongs to one key. Release rewrites only keys that contained a removed row.
internal sealed record PairIndex(
    string PassId,
    IReadOnlyList<string> Fields,
    bool ApplyVeto,
    MatchWorkspace Workspace,
    ImmutableDictionary<MatchKey, ImmutableArray<int>> Left,
    ImmutableDictionary<MatchKey, ImmutableArray<int>> Right,
    ImmutableDictionary<int, MatchKey> KeyOf,
    ImmutableDictionary<MatchKey, MatchProposal> Ready,
    ImmutableSortedSet<MatchKey> ReadyOrder) : PassIndex(PassId)
{
    public override ImmutableHashSet<int> WorkspaceFree => Workspace.Free;

    public override IReadOnlyList<MatchProposal> ReadyProposals => [.. ReadyOrder.Select(key => Ready[key])];

    // Commit order is the ordinal key, not the row index.
    public override MatchProposal? NextReady() =>
        ReadyOrder.IsEmpty ? null : Ready[ReadyOrder.Min];

    public static (PairIndex Index, MatchCounters Counters) Create(
        string passId,
        IReadOnlyList<string> fields,
        bool veto,
        MatchWorkspace workspace,
        MatchCounters counters,
        bool sharedOnly = false)
    {
        var placed = Placed(workspace, fields, sharedOnly);
        var keyed = placed.ToImmutableDictionary(row => row.Index, row => row.Key);
        var seeded = new PairIndex(
            passId,
            fields,
            veto,
            workspace,
            SideMap(placed, Side.Left),
            SideMap(placed, Side.Right),
            keyed,
            ImmutableDictionary.Create<MatchKey, MatchProposal>(MatchKey.OrdinalEquality),
            ImmutableSortedSet.Create(MatchKey.Ordinal));
        var ready = RefreshAll(seeded, seeded.Left.Keys.Union(seeded.Right.Keys, MatchKey.OrdinalEquality));
        return (ready, counters.Built());
    }

    public override (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters)
    {
        var dirty = taken.Where(KeyOf.ContainsKey).Select(index => KeyOf[index]).Distinct(MatchKey.OrdinalEquality);
        var removed = taken.Aggregate(this, (state, index) => state.Without(index));
        return (RefreshAll(removed, dirty), counters);
    }

    private static PairIndex RefreshAll(PairIndex index, IEnumerable<MatchKey> keys)
    {
        var current = index;
        foreach (var key in keys)
            current = current.Refresh(key);
        return current;
    }

    public override IReadOnlyList<ResidueCluster> Residue() =>
        [.. Left.Keys.Union(Right.Keys, MatchKey.OrdinalEquality).OrderBy(key => key, MatchKey.Ordinal).SelectMany(Cluster)];

    // A key with rows on only one side is not residue. Those rows stay free for a later pass.
    private IEnumerable<ResidueCluster> Cluster(MatchKey key) =>
        Sides(key) is { Left: { Length: > 0 } left, Right: { Length: > 0 } right } && (left.Length is not 1 || right.Length is not 1)
            ? [new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows([.. left, .. right]), key.Encoded)]
            : [];

    private PairIndex Without(int index) =>
        KeyOf.TryGetValue(index, out var key) is false
            ? this
            : Workspace.Items[index].Side switch
            {
                Side.Left => this with { Left = Left.SetItem(key, Left[key].Remove(index)), KeyOf = KeyOf.Remove(index) },
                _ => this with { Right = Right.SetItem(key, Right[key].Remove(index)), KeyOf = KeyOf.Remove(index) },
            };

    private PairIndex Refresh(MatchKey key)
    {
        var cleared = this with { Ready = Ready.Remove(key), ReadyOrder = ReadyOrder.Remove(key) };
        var (left, right) = cleared.Sides(key);
        return left.Length is 1 && right.Length is 1
            ? cleared with
            {
                Ready = cleared.Ready.SetItem(key, new MatchProposal([left[0]], [right[0]], key.Encoded)),
                ReadyOrder = cleared.ReadyOrder.Add(key),
            }
            : cleared;
    }

    // A row stays when any opposite row does not veto it, including a row the other side then drops.
    // One clean pair can remain ambiguous.
    private (int[] Left, int[] Right) Sides(MatchKey key)
    {
        var left = Members(Left, key);
        var right = Members(Right, key);
        return (
            [.. left.Where(index => right.Any(other => ApplyVeto is false || Veto(index, other) is false)).OrderBy(index => index)],
            [.. right.Where(index => left.Any(other => ApplyVeto is false || Veto(other, index) is false)).OrderBy(index => index)]);
    }

    private bool Veto(int left, int right) =>
        MatchKeys.Vetoed(Workspace.Items[left], Workspace.Items[right], Workspace.SharedField, Workspace.Schema);

    private IReadOnlyList<ConsolidatedRow> Rows(IReadOnlyList<int> indexes) =>
        [.. indexes.Select(index => Workspace.Items[index].Row)];

    private static ImmutableArray<int> Members(ImmutableDictionary<MatchKey, ImmutableArray<int>> side, MatchKey key) =>
        side.TryGetValue(key, out var members) ? members : [];

    private static ImmutableDictionary<MatchKey, ImmutableArray<int>> SideMap(IReadOnlyList<PlacedRow> rows, Side side) =>
        rows.Where(row => row.Side == side)
            .GroupBy(row => row.Key, MatchKey.OrdinalEquality)
            .ToImmutableDictionary(group => group.Key, group => group.Select(row => row.Index).ToImmutableArray(), MatchKey.OrdinalEquality);

    // A null key part is not placed. The row stays free for a later pass.
    private static IReadOnlyList<PlacedRow> Placed(MatchWorkspace workspace, IReadOnlyList<string> fields, bool sharedOnly) =>
        [.. workspace.Free
            .Select(index => MatchKeys.Compose(workspace.Items[index], fields, workspace.Schema, sharedOnly) is { } key
                ? new PlacedRow(index, workspace.Items[index].Side, key)
                : null)
            .OfType<PlacedRow>()];

    private sealed record PlacedRow(int Index, Side Side, MatchKey Key);
}
