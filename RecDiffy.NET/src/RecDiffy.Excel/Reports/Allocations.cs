using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// BO5. Layout from build_xlsx.py (BO5 allocations): each entry the engine committed as ManyToOne or OneToMany (and not a package),
// the block line, the allocation lines outline-grouped beneath it, and a totals line whose checks show ✓ when the allocations
// add up to the block exactly, or ✗ and the difference.
internal static class Allocations
{
    public static ReportDefinition Definition { get; } = new(
        ReportKind.Allocations,
        [ReportRole.Quantity],
        context => Groups(context).IsEmpty ? "No many-to-one or one-to-many entries (outside packages) in this run." : null,
        Build);

    private sealed record Group(EntryFact Entry, RowFact Block, string BlockSide, ImmutableArray<RowFact> Members);

    private static ImmutableArray<Group> Groups(ReportContext context) =>
        [.. context.Facts.Entries
            .Where(entry => entry.IsMatched && entry.Entry.Composition.HasFlag(Composition.Package) is false)
            .Where(entry => entry.Entry.Composition.HasFlag(Composition.ManyToOne) || entry.Entry.Composition.HasFlag(Composition.OneToMany))
            // Member counts include duplicate copies collapsed into the block, so the block side is the composition, not Right.Count == 1.
            .Select(entry => entry.Entry.Composition.HasFlag(Composition.ManyToOne)
                ? new Group(entry, context.Facts.RowByRef[entry.Entry.Right[0]], "theirs", [.. entry.Entry.Left.Select(member => context.Facts.RowByRef[member])])
                : new Group(entry, context.Facts.RowByRef[entry.Entry.Left[0]], "ours", [.. entry.Entry.Right.Select(member => context.Facts.RowByRef[member])]))];

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.Allocations);
        var rows = DataTabs.Rows(context);
        var netCash = context.Fields.IsMapped(ReportRole.NetCash);
        var price = context.Fields.IsMapped(ReportRole.Price);
        var sheet = Groups(context).Aggregate(
            (Sheet: ReportSheet.Head(context, info).SummaryAbove(), Row: ReportSheet.Body),
            (state, group) => Draw(state.Sheet, state.Row, context, rows, group, netCash, price));
        return new ReportBody(sheet.Sheet.Widths([20, 16, 14, 14, 12, 16, 16, 18]), [rows]);
    }

    private static (Sheet Sheet, int Row) Draw(Sheet sheet, int row, ReportContext context, DataTab rows, Group group, bool netCash, bool price)
    {
        var entry = group.Entry;
        var block = row + 2;
        var total = block + 1 + group.Members.Length;
        string Link(RowFact fact, ReportRole role) => ReportSheet.Link(rows.Cell(DataTabs.RoleKey(role), fact.Index));
        Sheet Line(Sheet current, int line, string label, RowFact fact, CellStyle text, CellStyle link) => current
            .Put(line, 1, label, text)
            .Put(line, 2, fact.Row.Row.RowKey, text)
            .Put(line, 3, fact.Values[ReportRole.Book].Text, text)
            .PutFormula(line, 4, Link(fact, ReportRole.Quantity), link with { Format = Formats.Quantity })
            .Put(line, 5, price ? Content.Formula(Link(fact, ReportRole.Price)) : null, link with { Format = Formats.Price })
            .Put(line, 6, netCash ? Content.Formula(Link(fact, ReportRole.NetCash)) : null, link with { Format = Formats.Amount });

        var drawn = sheet
            .Put(row, 1, $"{entry.Short}  ·  {entry.Text(ReportRole.Counterparty)}  ·  {entry.Text(ReportRole.Instrument)}  ·  {entry.Text(ReportRole.Side)}  ·  matched by {entry.Entry.Explanation.CommittedBy}  ·  outcome {entry.Entry.Outcome}", Styles.Banner)
            .Merge(row, 1, row, 8)
            .Header(row + 1, context.Labels("Line", "Row", "Book", "Quantity", "Price", "Net cash", "Quantity check", "Net cash check"), height: 18);
        var withBlock = Line(drawn, block, $"{context.Label("Block")} ({group.BlockSide})", group.Block, new CellStyle(Bold: true, Fill: Palette.Sub), new CellStyle(Bold: true, Color: Palette.Link, Fill: Palette.Sub));
        var withMembers = group.Members.Select((member, index) => (member, index)).Aggregate(
            withBlock,
            (current, pair) => Line(current, block + 1 + pair.index, $"   {context.Label("allocation")}", pair.member, CellStyle.Plain, Styles.Link(Formats.Quantity))
                .Outline(block + 1 + pair.index, 1));
        var finished = withMembers
            .Put(total, 1, context.Label("Allocations total"), Styles.Bold)
            .PutFormula(total, 4, $"SUM(D{(block + 1).ToInvariant()}:D{(total - 1).ToInvariant()})", Styles.Number(Formats.Quantity, bold: true))
            .PutFormula(total, 7, $"IF(D{total.ToInvariant()}=D{block.ToInvariant()},\"✓ equal\",\"✗ \"&TEXT(D{total.ToInvariant()}-D{block.ToInvariant()},\"#,##0.00\"))", Styles.Bold)
            .Fold(netCash ? [total] : Array.Empty<int>(), (current, line, _) => current
                .PutFormula(line, 6, $"SUM(F{(block + 1).ToInvariant()}:F{(line - 1).ToInvariant()})", Styles.Number(Formats.Amount, bold: true))
                .PutFormula(line, 8, $"IF(ROUND(F{line.ToInvariant()}-F{block.ToInvariant()},2)=0,\"✓ equal\",\"✗ \"&TEXT(F{line.ToInvariant()}-F{block.ToInvariant()},\"#,##0.00\"))", Styles.Bold))
            .CheckPrefixRules(new CellRange(total, 7, total, 7))
            .CheckPrefixRules(new CellRange(total, 8, total, 8));
        return (finished, total + 2);
    }
}
