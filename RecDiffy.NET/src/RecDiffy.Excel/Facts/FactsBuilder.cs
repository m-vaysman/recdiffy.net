using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.Excel.Facts;

// Reads a ReconciliationResult once, through its public API only, into the records every report shares.
// No matching happens here: alignment, outcome, diffs and dispositions are the engine's.
internal static class FactsBuilder
{
    private static readonly ImmutableArray<string> UnresolvedCodes = ["UnknownParty", "UnknownSymbol", "MissingKey"];

    public static ReportFacts Build(ReconciliationResult result, FieldMap map, ExcelReportOptions options)
    {
        var rowsByEntry = result.Rows.Where(row => row.EntryId is not null).ToLookup(row => row.EntryId!.Value.Value, StringComparer.Ordinal);
        var entries = result.Entries.Select((entry, index) => Entry(result, map, entry, index, rowsByEntry[entry.Id.Value])).ToImmutableArray();
        var entryById = entries.ToImmutableDictionary(entry => entry.Id, StringComparer.Ordinal);
        var leads = ItemLeads(result);
        var rows = result.Rows.Select((row, index) => new RowFact(
                index,
                row,
                row.EntryId is { } id && entryById.TryGetValue(id.Value, out var entry) ? entry : null,
                RoleValues.Read(result.Canonical(row.Row), map),
                leads.Contains(index)))
            .ToImmutableArray();
        var diffs = entries
            .Where(entry => entry.IsMatched)
            .SelectMany(entry => entry.Entry.Diffs.Select(diff => (Entry: entry, Diff: diff)))
            .Select((pair, index) => Diff(result, map, options.Profile, pair.Entry, pair.Diff, index))
            .ToImmutableArray();
        var openItems = OpenItems(entries, diffs, map, options).ToImmutableArray();
        return new ReportFacts(entries, rows, diffs, openItems, Passes(result, options.Profile));
    }

    private static EntryFact Entry(ReconciliationResult result, FieldMap map, ResultEntry entry, int index, IEnumerable<RowResult> linked)
    {
        var ours = entry switch
        {
            { Alignment: Alignment.Matched } => RoleValues.Read(result.Projection(entry.Id, Side.Left), map),
            { Left: [var first, ..] } => RoleValues.Read(result.Canonical(first), map),
            _ => RoleValues.None,
        };
        var theirs = entry switch
        {
            { Alignment: Alignment.Matched } => RoleValues.Read(result.Projection(entry.Id, Side.Right), map),
            { Right: [var first, ..] } => RoleValues.Read(result.Canonical(first), map),
            _ => RoleValues.None,
        };
        var members = linked.ToArray();
        return new EntryFact(
            index,
            entry,
            ours,
            theirs,
            Currencies(result, map, entry.Left),
            Currencies(result, map, entry.Right),
            members.Count(row => row.Disposition is Disposition.Suppressed),
            RootFields(entry),
            [.. members.SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal)]);
    }

    private static ImmutableArray<string> Currencies(ReconciliationResult result, FieldMap map, IReadOnlyList<SourceRef> members) =>
        map[ReportRole.Currency] is { } field
            ? [.. members.Select(member => result.Canonical(member).Text(field)).Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]
            : [];

    // A root break is a Break that no other field explains; when every break is attributed, the first break stands in.
    private static ImmutableArray<string> RootFields(ResultEntry entry)
    {
        var breaks = entry.Diffs.Where(diff => diff.Outcome is FieldOutcome.Break).ToArray();
        var roots = breaks.Where(diff => diff.AttributedTo.IsDefaultOrEmpty).Select(diff => diff.Field.Name).ToImmutableArray();
        return roots.IsEmpty && breaks.Length > 0 ? [breaks[0].Field.Name] : roots;
    }

    // Rows that consolidated into one item (a package's legs) share one canonical context, and so one trace list.
    // The public API has no consolidated-item id, so the first row of each shared trace leads its item.
    private static ImmutableHashSet<int> ItemLeads(ReconciliationResult result) =>
        result.Rows
            .Select((row, index) => (Trace: (object)result.Trace(row.Row), Index: index))
            .GroupBy(pair => pair.Trace, ReferenceEqualityComparer.Instance)
            .Select(group => group.First().Index)
            .ToImmutableHashSet();

    private static DiffFact Diff(ReconciliationResult result, FieldMap map, CompiledProfile? profile, EntryFact entry, FieldDiff diff, int index)
    {
        var field = diff.Field.Name;
        var ours = FieldValue.Read(result.Projection(entry.Entry.Id, Side.Left), field);
        var theirs = FieldValue.Read(result.Projection(entry.Entry.Id, Side.Right), field);
        var role = map.Fields.Where(pair => pair.Value == field).Select(pair => (ReportRole?)pair.Key).FirstOrDefault();
        return new DiffFact(index, entry, diff, role, ours, theirs, ToleranceAmount(profile, entry.Entry.PathId, field, ours.Number, theirs.Number));
    }

    // The engine reports a tolerance by kind only; the amount comes from the profile's diff declaration, as FieldDiffer applies it.
    public static decimal? ToleranceAmount(CompiledProfile? profile, string pathId, string field, decimal? ours, decimal? theirs)
    {
        var tolerance = profile?.Document.Paths
            .Where(path => path.Id == pathId)
            .Select(path => path.Diff?.Fields.TryGetValue(field, out var declared) == true ? declared.Tolerance : null)
            .FirstOrDefault();
        var scale = Math.Max(Math.Abs(ours ?? 0m), Math.Abs(theirs ?? 0m));
        return tolerance switch
        {
            { Kind: "Absolute", Absolute: { } absolute } => absolute,
            { Kind: "Relative", Relative: { } relative } => relative * scale,
            { Kind: "AbsoluteOrRelative", Absolute: { } absolute, Relative: { } relative } => Math.Max(absolute, relative * scale),
            _ => null,
        };
    }

    private static ImmutableArray<string> Passes(ReconciliationResult result, CompiledProfile? profile)
    {
        var declared = profile?.Document.Paths.SelectMany(path => path.Passes.Select(pass => pass.Id)) ?? [];
        var committed = result.Entries.Select(entry => entry.Explanation.CommittedBy).OfType<string>();
        return [.. declared.Concat(committed).Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<OpenItem> OpenItems(ImmutableArray<EntryFact> entries, ImmutableArray<DiffFact> diffs, FieldMap map, ExcelReportOptions options)
    {
        var diffsByEntry = diffs.ToLookup(diff => diff.Entry.Id, StringComparer.Ordinal);
        var items = entries.Select(entry => OpenItem(entry, diffsByEntry[entry.Id].ToArray(), map)).OfType<OpenItem>().ToArray();
        var order = Ordering.Currencies(options.CurrencyOrder);
        return items
            .OrderBy(item => Ordering.Status(item.SettleDate.Date, options.AsOf))
            .ThenBy(item => order(item.Entry.Text(ReportRole.Currency)))
            .ThenBy(item => item.Entry.Text(ReportRole.Currency), StringComparer.Ordinal)
            .ThenByDescending(item => item.Impact ?? decimal.MinValue)
            .ThenBy(item => item.Entry.Id, StringComparer.Ordinal);
    }

    private static OpenItem? OpenItem(EntryFact entry, DiffFact[] diffs, FieldMap map)
    {
        var netCash = map.IsMapped(ReportRole.NetCash);
        var settleField = map[ReportRole.SettleDate];
        return entry.Entry switch
        {
            { Outcome: FieldOutcome.Break } => Break(entry, diffs, netCash, settleField),
            { Alignment: Alignment.LeftOnly } => Whole(entry, "Unmatched ours", entry.Ours, netCash, "Chase confirmation: not in their file"),
            { Alignment: Alignment.RightOnly } => Whole(entry, "Unmatched theirs", entry.Theirs, netCash, "DK: investigate alleged trade"),
            { Alignment: Alignment.Ambiguous } => Whole(entry, "Ambiguous", entry.Ours.Any ? entry.Ours : entry.Theirs, false, "Resolve ambiguity: choose the rows that belong together"),
            { Alignment: Alignment.SearchIncomplete } => Whole(entry, "Search incomplete", entry.Ours.Any ? entry.Ours : entry.Theirs, false, "Review: the match search hit a profile limit"),
            _ => null,
        };
    }

    private static OpenItem Break(EntryFact entry, DiffFact[] diffs, bool netCash, string? settleField)
    {
        var root = diffs.FirstOrDefault(diff => diff.Field == entry.RootFields.FirstOrDefault());
        var cashDiff = diffs.FirstOrDefault(diff => diff.Role is ReportRole.NetCash);
        decimal? impact = (netCash, root?.Field == settleField) switch
        {
            (false, _) => null,
            (true, true) => Math.Abs(entry.Ours[ReportRole.NetCash].Number ?? 0m),
            _ => Math.Abs(cashDiff?.Delta ?? 0m),
        };
        var field = root?.Field ?? "(whole trade)";
        return new OpenItem(entry, "Field break", field, root, impact, Action(entry, BreakAction(root?.Role, field)), entry.Ours[ReportRole.SettleDate]);
    }

    private static OpenItem Whole(EntryFact entry, string type, RoleValues side, bool netCash, string action) =>
        new(entry, type, type, null, netCash ? Math.Abs(side[ReportRole.NetCash].Number ?? 0m) : null, Action(entry, action), side[ReportRole.SettleDate]);

    private static string BreakAction(ReportRole? role, string field) => role switch
    {
        ReportRole.Price => "Confirm execution price with counterparty",
        ReportRole.Quantity => "Confirm fill quantity (partial fill?)",
        ReportRole.SettleDate => "Align settlement date / SSI convention",
        ReportRole.Fees => "Check commission schedule",
        ReportRole.Accrued => "Check accrual convention and day count",
        ReportRole.NetCash => "Reconcile net cash components",
        ReportRole.Currency => "Confirm settlement currency",
        ReportRole.TradeDate => "Confirm trade date",
        _ => $"Investigate {field} difference",
    };

    private static string Action(EntryFact entry, string action) =>
        entry.Diagnostics.Where(code => UnresolvedCodes.Contains(code)).ToArray() switch
        {
            [] => action,
            var codes => $"Set up reference data ({string.Join(", ", codes)}), then: {action}",
        };
}

internal static class Ordering
{
    public static Func<string, int> Currencies(IReadOnlyList<string> order) =>
        currency => order.Select((code, index) => (code, index)).Where(pair => pair.code == currency).Select(pair => pair.index).DefaultIfEmpty(order.Count).First();

    public static int Status(DateOnly? settle, DateOnly asOf) => settle switch
    {
        null => 3,
        { } date when date < asOf => 0,
        { } date when date == asOf => 1,
        _ => 2,
    };

    public static IEnumerable<string> SortCurrencies(IEnumerable<string> currencies, IReadOnlyList<string> order)
    {
        var rank = Currencies(order);
        return currencies.Distinct(StringComparer.Ordinal).OrderBy(rank).ThenBy(currency => currency, StringComparer.Ordinal);
    }
}
