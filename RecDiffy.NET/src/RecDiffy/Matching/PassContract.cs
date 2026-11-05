namespace RecDiffy;

public enum PassCardinality
{
    OneToOne,
    ManyToOne,
    OneToMany,
}

// A pass proposes groups. The orchestrator commits them. The pass does not consume rows itself.
public interface IMatchPass
{
    string PassId { get; }

    PassCardinality Cardinality { get; }

    void Propose(in PassContext context, ProposalSink sink);
}

public readonly struct PassContext
{
    internal PassContext(MatchWorkspace workspace) => Workspace = workspace;

    internal MatchWorkspace Workspace { get; }

    public string PassId => Workspace.PassId;

    public bool IsFree(int item) => Workspace.Free.Contains(item);

    public Side SideOf(int item) => Workspace.Items[item].Side;

    public string? KeyOf(int item, IReadOnlyList<string> fields) =>
        MatchKeys.Compose(Workspace.Items[item], fields, Workspace.Schema);

    public bool Vetoed(int left, int right) =>
        MatchKeys.Vetoed(Workspace.Items[left], Workspace.Items[right], Workspace.SharedField, Workspace.Schema);

    public decimal? Number(int item, string field) =>
        Workspace.Items[item].Row.Context.IsNull(field) ? null : Workspace.Items[item].Row.Context.Values[field].Number;
}

public sealed class ProposalSink
{
    internal List<MatchProposal> Proposals { get; } = [];

    public void Propose(IReadOnlyList<int> left, IReadOnlyList<int> right, string key)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(key);
        Proposals.Add(new MatchProposal(left, right, key));
    }
}

internal sealed class MatchProposal(IReadOnlyList<int> left, IReadOnlyList<int> right, string key)
{
    public IReadOnlyList<int> Left { get; } = left;

    public IReadOnlyList<int> Right { get; } = right;

    public string Key { get; } = key;
}
