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

    // Missing-key rows stay in front, in file order. Collapsed groups follow them.
    private static IReadOnlyList<IntakeRow> NormalizeSide(
        IReadOnlyList<RawRecord> rows,
        IReadOnlyList<CompiledRule> rules,
        ReferenceDataSet reference,
        InstrumentMaster master) =>
        [.. rows.Where(row => row.MissingKey).Select(QuarantineMissing), .. Collapse(rows.Where(row => row.MissingKey is false).ToArray()).Select(group => Normalize(group, rules, reference, master))];

    private static IntakeRow Normalize(DuplicateGroup group, IReadOnlyList<CompiledRule> rules, ReferenceDataSet reference, InstrumentMaster master) =>
        group.Conflict ? Quarantine(group) : Accept(group, rules, reference, master);

    // A warning does not quarantine the row. Any Invalid diagnostic does.
    private static IntakeRow Accept(DuplicateGroup group, IReadOnlyList<CompiledRule> rules, ReferenceDataSet reference, InstrumentMaster master)
    {
        var context = Warn(Apply(group.Row, rules, reference, master), group.Members.Count);
        return new IntakeRow(group.Members, context, context.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Invalid));
    }

    private static RowContext Warn(RowContext row, int copies) =>
        copies > 1 ? row.Diagnose("DuplicateRowCollapsed", DiagnosticSeverity.Warning, "duplicates", null, row.RowKey) : row;

    private static RowContext Apply(RawRecord row, IReadOnlyList<CompiledRule> rules, ReferenceDataSet reference, InstrumentMaster master) =>
        rules.Aggregate(Start(row, reference, master), (current, rule) => WhenMatcher.Holds(rule.When, current) ? Invoke(rule, current) : current);

    // Any source name other than "right" is the left side.
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

    private static IntakeRow QuarantineMissing(RawRecord row) =>
        new(
            [row.Ref],
            RowContext.Create(row.Ref.RowKey, row.Ref.Source is "right" ? Side.Right : Side.Left, row.Cells)
                .Diagnose("MissingKey", DiagnosticSeverity.Invalid, "intake", null, null),
            true);

    // Conflict copies skip the rules. Each copy adds one DuplicateConflict, against empty reference data.
    private static IntakeRow Quarantine(DuplicateGroup group) =>
        new(
            group.Members,
            group.Members.Aggregate(
                Start(group.Row, ReferenceDataSet.Empty, InstrumentMaster.Empty),
                (row, _) => row.Diagnose("DuplicateConflict", DiagnosticSeverity.Invalid, "duplicates", null, group.Row.Ref.RowKey)),
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
        Delimited.Join('\u001f', row.Cells.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
}
