using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy;

internal sealed record IntakeRow(IReadOnlyList<SourceRef> Members, RowContext Context, bool Quarantined);

internal static class Intake
{
    public static (IReadOnlyList<IntakeRow> Left, IReadOnlyList<IntakeRow> Right) Normalize(
        CompiledProfile profile,
        IReadOnlyList<RawRecord> left,
        IReadOnlyList<RawRecord> right,
        ReferenceDataSet reference)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(reference);
        // instrument.resolve is not in v1 (#259): no rule reads RowContext.Instruments, so no identifier master is built.
        return (
            NormalizeSide(left, profile.LeftRules, ActionField(profile, "left"), reference),
            NormalizeSide(right, profile.RightRules, ActionField(profile, "right"), reference));
    }

    private static string? ActionField(CompiledProfile profile, string side) =>
        profile.Document.Consolidation.TryGetValue(side, out var plan) ? plan.Lifecycle?.ActionField : null;

    // Missing-key rows stay in front, in file order. Collapsed groups follow them.
    private static IReadOnlyList<IntakeRow> NormalizeSide(
        IReadOnlyList<RawRecord> rows,
        IReadOnlyList<CompiledRule> rules,
        string? actionField,
        ReferenceDataSet reference) =>
        [.. rows.Where(row => row.MissingKey).Select(row => QuarantineMissing(row, rules, actionField, reference)), .. Collapse(rows.Where(row => row.MissingKey is false).ToArray()).Select(group => Normalize(group, rules, actionField, reference))];

    private static IntakeRow Normalize(DuplicateGroup group, IReadOnlyList<CompiledRule> rules, string? actionField, ReferenceDataSet reference) =>
        group.Conflict ? Quarantine(group, rules, actionField, reference) : Accept(group, rules, actionField, reference);

    // A warning does not quarantine the row. Any Invalid diagnostic does.
    private static IntakeRow Accept(DuplicateGroup group, IReadOnlyList<CompiledRule> rules, string? actionField, ReferenceDataSet reference)
    {
        var context = Warn(Apply(group.Row, rules, actionField, reference), group.Members.Count);
        return new IntakeRow(group.Members, context, context.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid));
    }

    private static RowContext Warn(RowContext row, int copies) =>
        copies > 1 ? row.Diagnose("DuplicateRowCollapsed", DiagnosticSeverity.Warning, "duplicates", null, row.RowKey) : row;

    private static RowContext Apply(RawRecord row, IReadOnlyList<CompiledRule> rules, string? actionField, ReferenceDataSet reference) =>
        rules.Aggregate(Start(row, reference), (current, rule) =>
            SkipOnCancel(rule, current, actionField) || !WhenMatcher.Holds(rule.When, current) ? current : Invoke(rule, current));

    // Spec v3 §3.3: a cancel event (CANC) carries only a lifecycle event and skips the trade rules.
    private static bool SkipOnCancel(CompiledRule rule, RowContext row, string? actionField) =>
        rule.Rule.Phase is not RulePhase.Identify && actionField is not null && row.Display(actionField) is "CANC";

    // Any source name other than "right" is the left side.
    private static RowContext Start(RawRecord row, ReferenceDataSet reference) =>
        RowContext.Create(row.Ref.RowKey, row.Ref.Source is "right" ? Side.Right : Side.Left, row.Cells) with
        {
            Reference = reference,
        };

    private static RowContext Invoke(CompiledRule rule, RowContext row)
    {
        var next = row;
        rule.Rule.Apply(ref next);
        return next;
    }

    // A keyless row is normalized like any row, so lifecycle can read its action and target and quarantine the chain it
    // names (spec v3 §5.1 rule 5; #215). It is quarantined for its missing key alone, so no rule's code is kept (§3.4).
    private static IntakeRow QuarantineMissing(RawRecord row, IReadOnlyList<CompiledRule> rules, string? actionField, ReferenceDataSet reference) =>
        new(
            [row.Ref],
            (Apply(row, rules, actionField, reference) with { Diagnostics = [] })
                .Diagnose("MissingKey", DiagnosticSeverity.Invalid, "intake", null, null),
            true);

    // A conflicting copy is normalized like any row, so lifecycle can read its action and target and quarantine its
    // whole chain (spec v3 §3.2, §5.1 rule 5; #163). The copy itself is quarantined with one DuplicateConflict.
    private static IntakeRow Quarantine(DuplicateGroup group, IReadOnlyList<CompiledRule> rules, string? actionField, ReferenceDataSet reference) =>
        new(
            group.Members,
            Apply(group.Row, rules, actionField, reference).Diagnose("DuplicateConflict", DiagnosticSeverity.Invalid, "duplicates", null, group.Row.Ref.RowKey),
            true);

    private static IReadOnlyList<DuplicateGroup> Collapse(IReadOnlyList<RawRecord> rows) =>
        [.. rows.GroupBy(row => row.Ref.RowKey, StringComparer.Ordinal).SelectMany(group => Groups(group.ToArray()))];

    // One shared fingerprint collapses to the first row and keeps every source ref.
    // Two fingerprints quarantine each copy.
    private static IEnumerable<DuplicateGroup> Groups(RawRecord[] copies) =>
        copies.Select(copy => copy.Fingerprint()).Distinct(StringComparer.Ordinal).Count() switch
        {
            1 when copies.Length is 1 => [new DuplicateGroup(copies[0], [copies[0].Ref], false)],
            1 => [new DuplicateGroup(copies[0], [.. copies.Select(copy => copy.Ref)], false)],
            _ => copies.Select(copy => new DuplicateGroup(copy, [copy.Ref], true)),
        };

    private readonly record struct DuplicateGroup(RawRecord Row, IReadOnlyList<SourceRef> Members, bool Conflict);
}

internal static class RawFingerprints
{
    // Columns are sorted by name before the join, so dictionary order cannot split identical rows.
    public static string Fingerprint(this RawRecord row) =>
        Delimited.Join('\u001f', row.Cells.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Delimited.Join('=', [pair.Key, Identity(pair.Value) ?? ""])));

    // Numbers are equal by value (spec v3 §3.2), so 100 and 100.00 fingerprint alike; anything else is invariant text (#171).
    private static string? Identity(object? value) => value switch
    {
        decimal number => (number / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        _ => InvariantText.Of(value),
    };
}
