using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy;

// A row belongs to one key. Release rewrites only keys that contained a removed row.
internal sealed record PairIndex(
    string PassId,
    IReadOnlyList<string> Fields,
    bool ApplyVeto,
    MatchWorkspace Workspace,
    ImmutableDictionary<string, ImmutableArray<int>> Left,
    ImmutableDictionary<string, ImmutableArray<int>> Right,
    ImmutableDictionary<int, string> KeyOf,
    ImmutableDictionary<string, MatchProposal> Ready,
    ImmutableSortedSet<string> ReadyOrder) : PassIndex(PassId)
{
    public override ImmutableHashSet<int> WorkspaceFree => Workspace.Free;

    public override IReadOnlyList<MatchProposal> ReadyProposals => [.. ReadyOrder.Select(key => Ready[key])];

    // Commit order is the ordinal key, not the row index.
    public override MatchProposal? NextReady() =>
        ReadyOrder.IsEmpty || ReadyOrder.Min is not { } key ? null : Ready[key];

    public static (PairIndex Index, MatchCounters Counters) Create(
        string passId,
        IReadOnlyList<string> fields,
        bool veto,
        MatchWorkspace workspace,
        MatchCounters counters)
    {
        var placed = Placed(workspace, fields);
        var keyed = placed.ToImmutableDictionary(row => row.Index, row => row.Key);
        var seeded = new PairIndex(
            passId,
            fields,
            veto,
            workspace,
            SideMap(placed, Side.Left),
            SideMap(placed, Side.Right),
            keyed,
            ImmutableDictionary.Create<string, MatchProposal>(StringComparer.Ordinal),
            ImmutableSortedSet.Create<string>(StringComparer.Ordinal));
        var ready = RefreshAll(seeded, seeded.Left.Keys.Union(seeded.Right.Keys, StringComparer.Ordinal));
        return (ready, counters.Built());
    }

    public override (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters)
    {
        var dirty = taken.Where(KeyOf.ContainsKey).Select(index => KeyOf[index]).Distinct(StringComparer.Ordinal);
        var removed = taken.Aggregate(this, (state, index) => state.Without(index));
        return (RefreshAll(removed, dirty), counters);
    }

    private static PairIndex RefreshAll(PairIndex index, IEnumerable<string> keys)
    {
        var current = index;
        foreach (var key in keys)
            current = current.Refresh(key);
        return current;
    }

    public override IReadOnlyList<ResidueCluster> Residue() =>
        [.. Left.Keys.Union(Right.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).SelectMany(Cluster)];

    // A key with rows on only one side is not residue. Those rows stay free for a later pass.
    private IEnumerable<ResidueCluster> Cluster(string key) =>
        Sides(key) is { Left: { Length: > 0 } left, Right: { Length: > 0 } right } && (left.Length is not 1 || right.Length is not 1)
            ? [new ResidueCluster(Alignment.Ambiguous, null, PassId, Rows([.. left, .. right]))]
            : [];

    private PairIndex Without(int index) =>
        KeyOf.TryGetValue(index, out var key) is false
            ? this
            : Workspace.Items[index].Side switch
            {
                Side.Left => this with { Left = Left.SetItem(key, Left[key].Remove(index)), KeyOf = KeyOf.Remove(index) },
                _ => this with { Right = Right.SetItem(key, Right[key].Remove(index)), KeyOf = KeyOf.Remove(index) },
            };

    private PairIndex Refresh(string key)
    {
        var cleared = this with { Ready = Ready.Remove(key), ReadyOrder = ReadyOrder.Remove(key) };
        var (left, right) = cleared.Sides(key);
        return left.Length is 1 && right.Length is 1
            ? cleared with
            {
                Ready = cleared.Ready.SetItem(key, new MatchProposal([left[0]], [right[0]], key)),
                ReadyOrder = cleared.ReadyOrder.Add(key),
            }
            : cleared;
    }

    // A row stays when any opposite row does not veto it, including a row the other side then drops.
    // One clean pair can remain ambiguous.
    private (int[] Left, int[] Right) Sides(string key)
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

    private static ImmutableArray<int> Members(ImmutableDictionary<string, ImmutableArray<int>> side, string key) =>
        side.TryGetValue(key, out var members) ? members : [];

    private static ImmutableDictionary<string, ImmutableArray<int>> SideMap(IReadOnlyList<PlacedRow> rows, Side side) =>
        rows.Where(row => row.Side == side)
            .GroupBy(row => row.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Select(row => row.Index).ToImmutableArray(), StringComparer.Ordinal);

    // A null key part is not placed. The row stays free for a later pass.
    private static IReadOnlyList<PlacedRow> Placed(MatchWorkspace workspace, IReadOnlyList<string> fields) =>
        [.. workspace.Free
            .Select(index => MatchKeys.Compose(workspace.Items[index], fields, workspace.Schema) is { } key
                ? new PlacedRow(index, workspace.Items[index].Side, key)
                : null)
            .OfType<PlacedRow>()];

    private sealed record PlacedRow(int Index, Side Side, string Key);
}
