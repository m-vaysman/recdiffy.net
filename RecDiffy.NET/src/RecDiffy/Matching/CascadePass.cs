namespace RecDiffy;

internal abstract class CascadePass : IMatchPass
{
    public abstract string PassId { get; }

    public abstract PassCardinality Cardinality { get; }

    public abstract int MaxGroup { get; }

    public abstract void Propose(in PassContext context, ProposalSink sink);

    public abstract IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace);

    public abstract (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters);

    protected static void Publish(ProposalSink sink, IEnumerable<MatchProposal> proposals)
    {
        foreach (var proposal in proposals)
            sink.Propose(proposal.Left, proposal.Right, proposal.Key);
    }
}
