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
        var master = InstrumentMaster.FromRows(left.Select(row => row.Cells).ToArray(), ["ISIN", "CUSIP", "SEDOL", "Ticker"]);
        return (
            NormalizeSide(left, profile.LeftRules, reference, master),
            NormalizeSide(right, profile.RightRules, reference, master));
    }

    private static IReadOnlyList<IntakeRow> NormalizeSide(
        IReadOnlyList<RawRecord> rows,
        IReadOnlyList<CompiledRule> rules,
        ReferenceDataSet reference,
        InstrumentMaster master) =>
        [.. Collapse(rows).Select(group => group.Conflict
            ? Quarantine(group)
            : new IntakeRow(group.Members, Warn(Apply(group.Row, rules, reference, master), group.Members.Count), false))];

    private static RowContext Warn(RowContext row, int copies) =>
        copies > 1 ? row.Diagnose("DuplicateRowCollapsed", DiagnosticSeverity.Warning, "duplicates", null, row.RowKey) : row;

    private static RowContext Apply(RawRecord row, IReadOnlyList<CompiledRule> rules, ReferenceDataSet reference, InstrumentMaster master) =>
        rules.Aggregate(Start(row, reference, master), (current, rule) => WhenMatcher.Holds(rule.When, current) ? Invoke(rule, current) : current);

    private static RowContext Start(RawRecord row, ReferenceDataSet reference, InstrumentMaster master) =>
        RowContext.Create(row.Ref.RowKey, row.Ref.Source is "right" ? Side.Right : Side.Left, row.Cells) with
        {
            Reference = reference,
            Instruments = master,
        };

    private static RowContext Invoke(CompiledRule rule, RowContext row)
    {
        var next = row;
        rule.Rule.Apply(ref next);
        return next;
    }

    private static IntakeRow Quarantine(DuplicateGroup group) =>
        new(
            group.Members,
            group.Members.Aggregate(
                Start(group.Row, ReferenceDataSet.Empty, InstrumentMaster.Empty),
                (row, _) => row.Diagnose("DuplicateConflict", DiagnosticSeverity.Invalid, "duplicates", null, group.Row.Ref.RowKey)),
            true);

    private static IReadOnlyList<DuplicateGroup> Collapse(IReadOnlyList<RawRecord> rows) =>
        [.. rows.GroupBy(row => row.Ref.RowKey, StringComparer.Ordinal).SelectMany(group => Groups(group.ToArray()))];

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
    public static string Fingerprint(this RawRecord row) =>
        string.Join("\u001f", row.Cells.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
}
