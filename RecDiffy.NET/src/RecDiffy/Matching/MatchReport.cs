using RecDiffy.Results;

namespace RecDiffy;

internal sealed record CommittedMatch(string PassId, string Key, IReadOnlyList<ConsolidatedRow> Left, IReadOnlyList<ConsolidatedRow> Right);

internal sealed record ResidueCluster(Alignment Alignment, string? Code, string? PassId, IReadOnlyList<ConsolidatedRow> Rows);

internal sealed record MatchReport(IReadOnlyList<CommittedMatch> Committed, IReadOnlyList<ResidueCluster> Residue, MatchCounters Counters);

internal sealed record MatchCounters(int IndexBuilds = 0, int SubsetSearches = 0, int DirtyResearches = 0, int Components = 0)
{
    public MatchCounters Built() => this with { IndexBuilds = IndexBuilds + 1 };

    public MatchCounters SawComponent() => this with { Components = Components + 1 };

    public MatchCounters SawSearch(bool dirty) => this with
    {
        SubsetSearches = SubsetSearches + 1,
        DirtyResearches = DirtyResearches + (dirty ? 1 : 0),
    };
}
