namespace RecDiffy;

internal sealed class GroupPass(string passId, IReadOnlyList<string> block, IReadOnlyList<string> conserve, Side many, int maxGroup) : CascadePass
{
    public override string PassId => passId;

    public override PassCardinality Cardinality => many == Side.Left ? PassCardinality.ManyToOne : PassCardinality.OneToMany;

    public override int MaxGroup => maxGroup;

    public override (PassIndex Index, MatchCounters Counters) Build(MatchWorkspace workspace, MatchCounters counters) =>
        GroupIndex.Create(passId, block, conserve, many, maxGroup, workspace, counters);

    public override void Propose(in PassContext context, ProposalSink sink) =>
        Publish(sink, Build(context.Workspace, new MatchCounters()).Index.ReadyProposals);

    public override IReadOnlyList<ResidueCluster> Residue(MatchWorkspace workspace) =>
        Build(workspace, new MatchCounters()).Index.Residue();
}
