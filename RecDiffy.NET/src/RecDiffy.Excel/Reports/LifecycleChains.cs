using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// BO4. Layout from build_xlsx.py (BO4 lifecycle chains): for each entry the engine marked LifecycleCollapsed, the rows of its chain in link order
// (following each row's target), the survivor in bold, superseded and cancel rows in grey strike-through, the rows it matched on the other side,
// and what the correction changed. Suppression and the survivor come from the engine (RowResult.Suppression, the entry's members).
internal static class LifecycleChains
{
    private static readonly ImmutableArray<ReportRole> Compared = [ReportRole.Quantity, ReportRole.Price, ReportRole.SettleDate, ReportRole.NetCash];

    public static ReportDefinition Definition { get; } = new(
        ReportKind.LifecycleChains,
        [ReportRole.LifecycleAction, ReportRole.LifecycleTarget],
        context => Chains(context).IsEmpty ? "No lifecycle-collapsed entries in this run." : null,
        Build);

    private sealed record Chain(EntryFact Entry, ImmutableArray<RowFact> Rows, ImmutableArray<RowFact> Others, RowFact Survivor);

    private static ImmutableArray<Chain> Chains(ReportContext context) =>
        [.. context.Facts.Entries
            .Where(entry => entry.Entry.Composition.HasFlag(Composition.LifecycleCollapsed))
            .SelectMany(entry => ChainsOf(context, entry))];

    // A matched entry can collapse a chain on each side. Each side with a suppressed row is its own chain.
    private static IEnumerable<Chain> ChainsOf(ReportContext context, EntryFact entry)
    {
        var linked = context.Facts.RowsByEntry[entry.Id].ToArray();
        foreach (var source in new[] { "left", "right" })
        {
            if (ChainOf(context, entry, linked, source) is { } chain)
                yield return chain;
        }
    }

    private static Chain? ChainOf(ReportContext context, EntryFact entry, RowFact[] linked, string source)
    {
        if (linked.Any(row => row.Source == source && row.Row.Suppression is not null) is false)
            return null;
        var members = (source == "left" ? entry.Entry.Left : entry.Entry.Right).Select(member => context.Facts.RowByRef[member]).ToArray();
        var chain = linked.Where(row => row.Source == source && row.Row.Suppression is not null).Concat(members.Where(row => row.Values[ReportRole.LifecycleAction].Text.Length > 0)).Distinct().ToArray();
        var survivor = members.FirstOrDefault(row => row.Values[ReportRole.LifecycleAction].Text.Length > 0) ?? members.First();
        var others = (source == "left" ? entry.Entry.Right : entry.Entry.Left).Select(member => context.Facts.RowByRef[member]).ToImmutableArray();
        return new Chain(entry, [.. LinkOrder(chain)], others, survivor);
    }

    // Each row's depth is one more than the depth of the row it targets, so NEW, then CANC (targets NEW), then CORR (targets CANC).
    private static IEnumerable<RowFact> LinkOrder(IReadOnlyList<RowFact> rows)
    {
        var byKey = rows.ToImmutableDictionary(row => row.Row.Row.RowKey, StringComparer.Ordinal);
        int Depth(RowFact row, int guard) =>
            guard < rows.Count && byKey.TryGetValue(row.Values[ReportRole.LifecycleTarget].Text, out var target) ? Depth(target, guard + 1) + 1 : 0;
        return rows.OrderBy(row => Depth(row, 0)).ThenBy(row => row.Row.Row.RowKey, StringComparer.Ordinal);
    }

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.LifecycleChains);
        var rows = DataTabs.Rows(context);
        var sheet = Chains(context).Aggregate(
            (Sheet: ReportSheet.Head(context, info), Row: ReportSheet.Body),
            (state, chain) => Draw(state.Sheet, state.Row, context, rows, chain));
        return new ReportBody(sheet.Sheet.Widths([28, 16, 9, 16, 14, 11, 12, 15, 22]), [rows]);
    }

    private static (Sheet Sheet, int Row) Draw(Sheet sheet, int row, ReportContext context, DataTab rows, Chain chain)
    {
        var entry = chain.Entry;
        var first = chain.Rows.FirstOrDefault(item => item.Values[ReportRole.LifecycleAction].Text is not "CANC") ?? chain.Rows[0];
        var changed = Compared.Where(role => context.Fields.IsMapped(role) && first.Values[role] != chain.Survivor.Values[role]).ToImmutableHashSet();
        var header = row + 1;
        var matched = header + 1 + chain.Rows.Length;
        var fixedRow = matched + chain.Others.Length;
        var what = changed.IsEmpty
            ? "No compared field changed between the first version and the survivor."
            : string.Join(", ", changed.Select(role => $"{context.Field(role)} {first.Values[role].Text} → {chain.Survivor.Values[role].Text}"));

        Sheet Values(Sheet current, int line, RowFact fact, CellStyle style) => current
            .Fold(Compared, (inner, role, index) => context.Fields.IsMapped(role)
                ? inner.PutFormula(line, 5 + index, ReportSheet.Link(rows.Cell(DataTabs.RoleKey(role), fact.Index)), style with { Format = DataTabs.FormatFor(role) })
                : inner.Put(line, 5 + index, null, style));

        var drawn = sheet
            .Put(row, 1, $"{entry.Short}  ·  {entry.Text(ReportRole.Counterparty)}  ·  {entry.Text(ReportRole.Instrument)}  ·  {chain.Rows.Length} rows in the chain", Styles.Banner)
            .Merge(row, 1, row, 9)
            .Header(header, context.Labels("", "Report", "Action", "Target", "Quantity", "Price", "Settle date", "Net cash", "Disposition"), height: 18)
            .Fold(chain.Rows, (current, fact, index) =>
            {
                var line = header + 1 + index;
                var live = fact == chain.Survivor;
                var style = live ? new CellStyle(Bold: true, Color: Palette.Link) : new CellStyle(Color: Palette.Muted, Strike: true);
                var text = live ? Styles.Bold : new CellStyle(Color: Palette.Muted, Strike: true);
                var disposition = fact.Row.Suppression switch
                {
                    SuppressionReason.Superseded => "Superseded",
                    SuppressionReason.CancelEvent => "Cancel event",
                    SuppressionReason.FullyCancelled => "Fully cancelled",
                    _ => $"Survivor ({fact.Row.Disposition})",
                };
                var withValues = Values(
                    current
                        .Put(line, 1, new string('↳', index + 1), text)
                        .Put(line, 2, fact.Row.Row.RowKey, text)
                        .Put(line, 3, fact.Values[ReportRole.LifecycleAction].Text, text)
                        .Put(line, 4, fact.Values[ReportRole.LifecycleTarget].Text, text)
                        .Put(line, 9, disposition, text),
                    line,
                    fact,
                    style);
                return live
                    ? Compared.Select((role, position) => (role, position)).Where(pair => changed.Contains(pair.role))
                        .Aggregate(withValues, (inner, pair) => inner.Restyle(line, 5 + pair.position, cell => cell with { Fill = Palette.Tolerance }))
                    : withValues;
            })
            .Fold(chain.Others, (current, fact, index) => Values(
                current
                    .Put(matched + index, 1, context.Label("Matched with"), new CellStyle(Bold: true, Fill: Palette.Ok))
                    .Put(matched + index, 2, fact.Row.Row.RowKey, new CellStyle(Fill: Palette.Ok))
                    .Put(matched + index, 3, $"pass {entry.Entry.Explanation.CommittedBy}", new CellStyle(Fill: Palette.Ok))
                    .Put(matched + index, 4, $"outcome {entry.Entry.Outcome}", new CellStyle(Fill: Palette.Ok))
                    .Put(matched + index, 9, fact.Row.Disposition.ToString(), new CellStyle(Fill: Palette.Ok)),
                matched + index,
                fact,
                new CellStyle(Color: Palette.Link, Fill: Palette.Ok)))
            .Put(fixedRow, 1, context.Label("What the correction changed"), Styles.Section)
            .Put(fixedRow, 2, what, CellStyle.Bare)
            .Merge(fixedRow, 2, fixedRow, 9);
        return (drawn, fixedRow + 2);
    }
}
