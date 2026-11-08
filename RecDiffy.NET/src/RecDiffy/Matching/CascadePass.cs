namespace RecDiffy;

internal abstract class CascadePass : IMatchPass
{
    public abstract string PassId { get; }

    public abstract PassCardinality Cardinality { get; }

    public abstract int MaxGroup { get; }

    public abstract void Propose(in PassContext context, ProposalSink sink);

    public abstract IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace);

    public abstract (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters);

    // Only a proposal whose rows are still free is kept, and the ordinal-smallest key is the one returned.
    public MatchProposal? ProposeFirst(MatchWorkspace workspace)
    {
        var sink = new ProposalSink();
        Propose(new PassContext(workspace), sink);
        return sink.Proposals
            .Where(proposal => proposal.Left.All(workspace.Free.Contains) && proposal.Right.All(workspace.Free.Contains))
            .OrderBy(proposal => proposal.Key, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    protected static void Publish(ProposalSink sink, IEnumerable<MatchProposal> proposals)
    {
        foreach (var proposal in proposals)
            sink.Propose(proposal.Left, proposal.Right, proposal.Key);
    }
}
