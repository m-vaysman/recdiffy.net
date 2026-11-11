namespace RecDiffy;

internal sealed class PairPass(string passId, IReadOnlyList<string> fields, bool veto, int maxGroup, bool sharedOnly = false) : CascadePass
{
    public override string PassId => passId;

    public override PassCardinality Cardinality => PassCardinality.OneToOne;

    public override int MaxGroup => maxGroup;

    public override (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters) =>
        PairIndex.Create(passId, fields, veto, workspace, counters, sharedOnly);

    // Fresh counters, rebuilt from the workspace free set. The commit path uses Build and keeps the index.
    public override void Propose(in PassContext context, ProposalSink sink) =>
        Publish(sink, Build(context.Workspace, new MatchCounters()).Index.ReadyProposals);

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        Build(workspace, new MatchCounters()).Index.Residue();
}
