using System.Collections.Immutable;

namespace RecDiffy;

internal abstract record PassIndex(string PassId)
{
    public abstract MatchProposal? NextReady();

    public abstract IReadOnlyList<MatchProposal> ReadyProposals { get; }

    public abstract (PassIndex Index, MatchCounters Counters) Release(IReadOnlyList<int> taken, MatchCounters counters);

    public abstract IReadOnlyList<ResidueCluster> Residue();

    public abstract ImmutableHashSet<int> WorkspaceFree { get; }
}
