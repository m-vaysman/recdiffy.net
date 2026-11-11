using RecDiffy.Results;

namespace RecDiffy.IO.Tests.OracleMatrix;

// One case of an oracle file, in the file's own words: its label, and the left and right row keys it names.
// A lifecycle case names its surviving report; a break names the field the oracle blames; a near-match its deltas.
internal sealed record OracleCase(string Id, string Label, IReadOnlyList<string> Left, IReadOnlyList<string> Right)
{
    public string? Survivor { get; init; }

    public IReadOnlyList<string> BreakFields { get; init; } = [];

    public IReadOnlyList<OracleDelta> Deltas { get; init; } = [];

    public CaseKind Kind => OracleLabels.Kind(Label);
}

// A field difference the oracle states, in its own field vocabulary. Delta is right minus left, as the oracle writes it.
internal sealed record OracleDelta(string Field, decimal Delta);

internal enum CaseKind
{
    Exact,
    WithinTolerance,
    Break,
    Allocation,
    MultiLeg,
    Lifecycle,
    LeftOnly,
    RightOnly,
    Ambiguous,
}

internal sealed record Axes(Alignment Alignment, Composition Composition, FieldOutcome Outcome);

internal sealed class UnknownOracleLabelException(string label)
    : Exception($"Oracle label '{label}' is not in the spec's §6.5 vocabulary, so the test cannot say what the engine should do.")
{
    public string Label { get; } = label;
}

// Spec v3 §6.5: every oracle's vocabulary mapped onto the result axes. A label not listed here fails the test.
// The last group is not in §6.5: Qwen and DeepSeek have no outcome labels, so their adapters use the oracle's own
// scenario name or section name as the label, and those are mapped here by what the section holds.
internal static class OracleLabels
{
    public static CaseKind Kind(string label) => label switch
    {
        "EXACT_MATCH_AFTER_NORMALIZATION" or "MATCH_EXACT_AFTER_NORMALIZATION" or "MATCHED_EXACT" or "EXACT" or "NORMALIZED" => CaseKind.Exact,
        "MATCH_WITHIN_TOLERANCE" or "MATCHED_WITHIN_TOLERANCE" => CaseKind.WithinTolerance,
        "BREAK_1_TO_1" or "MATCH_WITH_FIELD_BREAKS" or "MATCHED_WITH_ECONOMIC_BREAK" or "BREAK" or "FIELD_BREAK" => CaseKind.Break,
        "MANY_TO_ONE_MATCH" or "MATCHED_N_TO_1" or "NETTED_ALLOCATION_MATCH" or "MATCH_AGGREGATED (allocations)" => CaseKind.Allocation,
        "TWO_TO_ONE_NET_MATCH" or "MATCHED_MULTI_LEG" or "MULTI_LEG_PACKAGE_MATCH" or "MATCH_AGGREGATED (swaps)" => CaseKind.MultiLeg,
        "CHAIN_COLLAPSES_TO_EXACT_MATCH" or "MATCH_AFTER_LIFECYCLE_COLLAPSE" or "MATCHED_AFTER_LIFECYCLE_COLLAPSE" or "EXACT_MATCH_AFTER_LIFECYCLE_COLLAPSE" => CaseKind.Lifecycle,
        "UNMATCHED_INTERNAL" or "LEFT_ONLY_UNMATCHED" or "INTERNAL_ONLY" => CaseKind.LeftOnly,
        "UNMATCHED_COUNTERPARTY" or "RIGHT_ONLY_ALLEGED" or "COUNTERPARTY_ONLY" => CaseKind.RightOnly,
        "AMBIGUOUS" => CaseKind.Ambiguous,

        "known_exact_matches" or "exact_matches" => CaseKind.Exact,
        "within_tolerance_near_match" or "near_matches" => CaseKind.WithinTolerance,
        "true_economic_field_break" or "economic_breaks" => CaseKind.Break,
        "n_to_1_allocation_split" or "n_to_1_allocation_splits" => CaseKind.Allocation,
        "multi_leg_swap_fx_representation" or "multi_leg_netting" => CaseKind.MultiLeg,
        "cancel_correct_lifecycle_chain" or "lifecycle_chains" => CaseKind.Lifecycle,
        "unmatched_internal_orphan" or "unmatched_internal" => CaseKind.LeftOnly,
        "unmatched_counterparty_alleged_trade" or "unmatched_counterparty" => CaseKind.RightOnly,

        _ => throw new UnknownOracleLabelException(label),
    };

    // An allocation is a package only where the profile links its rows before matching (fixture 2.0's BLOCK links).
    // Where a group pass discovers it (Sonnet), it is ManyToOne without Package.
    public static Axes Expected(CaseKind kind, bool allocationsArePackages) => kind switch
    {
        CaseKind.Exact => new(Alignment.Matched, Composition.OneToOne, FieldOutcome.Exact),
        CaseKind.WithinTolerance => new(Alignment.Matched, Composition.OneToOne, FieldOutcome.WithinTolerance),
        CaseKind.Break => new(Alignment.Matched, Composition.OneToOne, FieldOutcome.Break),
        CaseKind.Allocation when allocationsArePackages => new(Alignment.Matched, Composition.ManyToOne | Composition.Package, FieldOutcome.Exact),
        CaseKind.Allocation => new(Alignment.Matched, Composition.ManyToOne, FieldOutcome.Exact),
        CaseKind.MultiLeg => new(Alignment.Matched, Composition.ManyToOne | Composition.Package, FieldOutcome.Exact),
        CaseKind.Lifecycle => new(Alignment.Matched, Composition.OneToOne | Composition.LifecycleCollapsed, FieldOutcome.Exact),
        CaseKind.LeftOnly => new(Alignment.LeftOnly, Composition.None, FieldOutcome.NotCompared),
        CaseKind.RightOnly => new(Alignment.RightOnly, Composition.None, FieldOutcome.NotCompared),
        CaseKind.Ambiguous => new(Alignment.Ambiguous, Composition.None, FieldOutcome.NotCompared),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
