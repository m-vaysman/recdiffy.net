using RecDiffy.Results;

using SheetRow = System.Collections.Generic.IReadOnlyDictionary<string, object?>;

namespace RecDiffy.IO.Tests.OracleMatrix;

// AC-6 and AC-7 on every acceptance workbook (spec v3 §12.3), each with its own profile. The rows are read once and fed as
// objects, so each run differs from the baseline only in row order or in IDs. AC-7 is the one promise §12.2 makes for
// v2, Qwen and DeepSeek, whose IDs leak their matches.
public class OracleInvarianceTests
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>(StringComparer.Ordinal);

    private static ReconciliationResult Run(string name, IReadOnlyList<SheetRow> left, IReadOnlyList<SheetRow> right) =>
        OracleRuns.Of(name)(ObjectSources.From("left", left), ObjectSources.From("right", right));

    private static IReadOnlyList<string> Baseline(OracleDataset dataset) =>
        Invariance.Fingerprint(Run(dataset.Name, dataset.Left(), dataset.Right()), None, None);

    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void AC6_twenty_seeded_permutations_of_the_rows_give_the_same_result(string name)
    {
        var dataset = OracleDatasets.Named(name);
        var baseline = Baseline(dataset);

        var differences = Enumerable.Range(1, 20)
            .Select(seed => (Seed: seed, Lines: Invariance.Difference(baseline, Invariance.Fingerprint(Run(name, Invariance.Shuffle(dataset.Left(), seed), Invariance.Shuffle(dataset.Right(), seed + 1000)), None, None))))
            .Where(run => run.Lines.Count > 0)
            .Select(run => $"seed {run.Seed}:{Environment.NewLine}{string.Join(Environment.NewLine, run.Lines.Take(10))}")
            .ToArray();

        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.Take(3)));
    }

    // The main variant of §10.1: each side's row keys, and the right-side columns that name a right row key, renamed by
    // a bijection. Evidence identifiers such as UTIs are kept.
    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void AC7_row_keys_renamed_by_a_bijection_give_the_same_result_after_mapping_back(string name) =>
        AssertScrambled(OracleDatasets.Named(name), evidence: false);

    // The second variant: evidence identifiers are also renamed, by one bijection applied to both sides.
    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void AC7_evidence_identifiers_renamed_on_both_sides_give_the_same_result_after_mapping_back(string name) =>
        AssertScrambled(OracleDatasets.Named(name), evidence: true);

    private static void AssertScrambled(OracleDataset dataset, bool evidence)
    {
        var baseline = Baseline(dataset);
        var leftKeys = Invariance.Bijection(dataset.LeftRows(), "LK", 11);
        var rightKeys = Invariance.Bijection(dataset.RightRows(), "RK", 12);
        var evidenceIds = evidence
            ? Invariance.Bijection(Invariance.Values(dataset.Left().Concat(dataset.Right()), dataset.Evidence), "EV", 13)
            : None;
        var left = Invariance.Rename(Invariance.Rename(dataset.Left(), [dataset.LeftKey], leftKeys), dataset.Evidence, evidenceIds);
        var right = Invariance.Rename(Invariance.Rename(dataset.Right(), [dataset.RightKey, .. dataset.RightKeyReferences], rightKeys), dataset.Evidence, evidenceIds);

        var scrambled = Invariance.Fingerprint(Run(dataset.Name, left, right), Inverse(leftKeys), Inverse(rightKeys));

        var difference = Invariance.Difference(baseline, scrambled);
        Assert.True(difference.Count == 0, string.Join(Environment.NewLine, difference.Take(20)));
    }

    private static Dictionary<string, string> Inverse(IReadOnlyDictionary<string, string> map) =>
        map.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
}
