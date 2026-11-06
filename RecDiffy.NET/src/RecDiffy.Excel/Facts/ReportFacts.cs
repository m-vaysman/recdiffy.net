using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.Excel.Facts;

internal sealed record EntryFact(
    int Index,
    ResultEntry Entry,
    RoleValues Ours,
    RoleValues Theirs,
    ImmutableArray<string> OursCurrencies,
    ImmutableArray<string> TheirsCurrencies,
    int SuppressedRows,
    ImmutableArray<string> RootFields,
    ImmutableArray<string> Diagnostics)
{
    public string Id => Entry.Id.Value;

    public string Short => Id.Length > 12 ? Id[..12] : Id;

    public bool IsMatched => Entry.Alignment is Alignment.Matched;

    public bool CurrencySplit => IsMatched && OursCurrencies.Length > 0 && TheirsCurrencies.Length > 0 && OursCurrencies.SequenceEqual(TheirsCurrencies) is false;

    public FieldValue Value(ReportRole role) => Ours[role].IsNull ? Theirs[role] : Ours[role];

    public string Text(ReportRole role) => Value(role).Text;

    public string Keys(Side side) => string.Join("; ", (side is Side.Left ? Entry.Left : Entry.Right).Select(member => member.RowKey));
}

internal sealed record RowFact(int Index, RowResult Row, EntryFact? Entry, RoleValues Values, bool ItemLead)
{
    public string Source => Row.Row.Source;

    public bool PairCurrencySplit => Entry is { CurrencySplit: true } && Row.Disposition is Disposition.Matched;
}

internal sealed record DiffFact(
    int Index,
    EntryFact Entry,
    FieldDiff Diff,
    ReportRole? Role,
    FieldValue Ours,
    FieldValue Theirs,
    decimal? ToleranceAmount)
{
    public string Field => Diff.Field.Name;

    public decimal? Delta => Diff.Delta.Kind switch
    {
        DeltaKind.Number => Diff.Delta.Number,
        DeltaKind.Days => Diff.Delta.Days,
        _ => null,
    };

    public DiffState State => (Diff.AttributedTo.IsDefaultOrEmpty, Diff.Outcome) switch
    {
        (false, _) => DiffState.Attributed,
        (true, FieldOutcome.WithinTolerance) => DiffState.Tolerance,
        _ => DiffState.Break,
    };
}

internal enum DiffState
{
    Break,
    Tolerance,
    Attributed,
}

// An open item is an engine entry (a break, or an unmatched, ambiguous or search-incomplete entry) or a row the engine left without an entry:
// unrouted, or excluded as Invalid before matching. Rows show those as LeftOnly or RightOnly, and they must not drop out of the blotter.
internal sealed record OpenItem(EntryFact? Entry, RowFact? Row, string ItemType, string Category, DiffFact? Diff, decimal? Impact, string Action, FieldValue SettleDate)
{
    public string Field => (Diff, Row) switch
    {
        ({ } diff, _) => diff.Field,
        (_, { Row.Validity: InputValidity.Invalid }) => "(excluded: invalid)",
        (_, { }) => "(not routed)",
        _ => "(whole trade)",
    };

    public string Key => Entry?.Id ?? $"(no entry) {Row!.Source}:{Row.Row.Row.RowKey}";

    public string Label => Entry?.Short ?? Row!.Row.Row.RowKey;

    public FieldValue Value(ReportRole role) => Entry?.Value(role) ?? Row!.Values[role];

    public string Text(ReportRole role) => Value(role).Text;
}

internal sealed record ReportFacts(
    ImmutableArray<EntryFact> Entries,
    ImmutableArray<RowFact> Rows,
    ImmutableArray<DiffFact> Diffs,
    ImmutableArray<OpenItem> OpenItems,
    ImmutableArray<string> Passes)
{
    public ILookup<string, RowFact> RowsByEntry { get; } = Rows.Where(row => row.Entry is not null).ToLookup(row => row.Entry!.Id, StringComparer.Ordinal);

    public ILookup<string, DiffFact> DiffsByEntry { get; } = Diffs.ToLookup(diff => diff.Entry.Id, StringComparer.Ordinal);

    public ImmutableDictionary<SourceRef, RowFact> RowByRef { get; } = Rows.ToImmutableDictionary(row => row.Row.Row);
}
