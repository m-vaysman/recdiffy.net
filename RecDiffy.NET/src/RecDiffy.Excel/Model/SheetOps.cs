namespace RecDiffy.Excel.Model;

internal static class Content
{
    public static CellContent Of(object? value) => value switch
    {
        null => BlankContent.Instance,
        CellContent content => content,
        string { Length: 0 } => BlankContent.Instance,
        string text => new TextContent(text),
        decimal number => new NumberContent(number),
        int number => new NumberContent(number),
        long number => new NumberContent(number),
        DateOnly date => new DateContent(date),
        _ => new TextContent(value.ToString() ?? ""),
    };

    public static FormulaContent Formula(string formula) => new(formula.StartsWith('=') ? formula[1..] : formula);
}

internal static class SheetOps
{
    public static Sheet Put(this Sheet sheet, int row, int column, object? value, CellStyle? style = null) =>
        sheet with { Cells = sheet.Cells.SetItem(new CellAddress(row, column), new Cell(Content.Of(value), style ?? CellStyle.Plain)) };

    public static Sheet PutFormula(this Sheet sheet, int row, int column, string formula, CellStyle? style = null) =>
        sheet.Put(row, column, Content.Formula(formula), style);

    public static Sheet Restyle(this Sheet sheet, int row, int column, Func<CellStyle, CellStyle> change) =>
        sheet.Cells.TryGetValue(new CellAddress(row, column), out var cell)
            ? sheet with { Cells = sheet.Cells.SetItem(new CellAddress(row, column), cell with { Style = change(cell.Style) }) }
            : sheet.Put(row, column, null, change(CellStyle.Plain));

    public static Sheet PutRow(this Sheet sheet, int row, int firstColumn, IEnumerable<object?> values, CellStyle? style = null) =>
        values.Select((value, index) => (value, index)).Aggregate(sheet, (current, item) => current.Put(row, firstColumn + item.index, item.value, style));

    public static Sheet Header(this Sheet sheet, int row, IReadOnlyList<string> titles, int firstColumn = 1, double height = 30) =>
        sheet.PutRow(row, firstColumn, titles, Styles.Header).Height(row, height);

    public static Sheet Merge(this Sheet sheet, int firstRow, int firstColumn, int lastRow, int lastColumn) =>
        sheet with { Merges = sheet.Merges.Add(new CellRange(firstRow, firstColumn, lastRow, lastColumn)) };

    public static Sheet Widths(this Sheet sheet, IEnumerable<double> widths) =>
        widths.Select((width, index) => (width, index)).Aggregate(sheet, (current, item) => current.Width(item.index + 1, item.width));

    public static Sheet Width(this Sheet sheet, int column, double width) =>
        sheet with { ColumnWidths = sheet.ColumnWidths.SetItem(column, width) };

    public static Sheet Height(this Sheet sheet, int row, double height) =>
        sheet with { RowHeights = sheet.RowHeights.SetItem(row, height) };

    public static Sheet Outline(this Sheet sheet, int row, int level) =>
        sheet with { RowOutline = sheet.RowOutline.SetItem(row, level) };

    public static Sheet FreezeAt(this Sheet sheet, int row, int column) =>
        sheet with { Freeze = new CellAddress(row, column) };

    public static Sheet Filter(this Sheet sheet, int firstRow, int firstColumn, int lastRow, int lastColumn) =>
        sheet with { AutoFilter = new CellRange(firstRow, firstColumn, Math.Max(firstRow, lastRow), lastColumn) };

    public static Sheet Rule(this Sheet sheet, ConditionalRule rule) =>
        rule.Range.LastRow < rule.Range.FirstRow ? sheet : sheet with { Rules = sheet.Rules.Add(rule) };

    public static Sheet Chart(this Sheet sheet, BarChart chart) =>
        sheet with { Charts = sheet.Charts.Add(chart) };

    public static Sheet Gridlines(this Sheet sheet, bool show) =>
        sheet with { ShowGridLines = show };

    public static Sheet SummaryAbove(this Sheet sheet) =>
        sheet with { SummaryBelow = false };

    public static Sheet Fold<T>(this Sheet sheet, IEnumerable<T> items, Func<Sheet, T, int, Sheet> step) =>
        items.Select((item, index) => (item, index)).Aggregate(sheet, (current, pair) => step(current, pair.item, pair.index));

    public static Sheet Note(this Sheet sheet, int row, string text, int span = 0) =>
        span > 1
            ? sheet.Put(row, 1, text, Styles.Note with { Wrap = true, VAlign = VerticalAlign.Top }).Merge(row, 1, row, span)
            : sheet.Put(row, 1, text, Styles.Note);

    public static Sheet CheckRules(this Sheet sheet, CellRange range) =>
        sheet
            .Rule(new CellIsRule(range, CellOperator.Equal, ["\"✓\""], Styles.CheckPass))
            .Rule(new CellIsRule(range, CellOperator.Equal, ["\"✗\""], Styles.CheckFail));

    public static Sheet CheckPrefixRules(this Sheet sheet, CellRange range) =>
        sheet
            .Rule(new ExpressionRule(range, $"LEFT({Address.Cell(range.FirstRow, range.FirstColumn)},1)=\"✓\"", Styles.CheckPass with { Fill = Palette.Ok }))
            .Rule(new ExpressionRule(range, $"LEFT({Address.Cell(range.FirstRow, range.FirstColumn)},1)=\"✗\"", Styles.CheckFail));
}

internal static class Styles
{
    public static CellStyle Plain => CellStyle.Plain;

    public static CellStyle Bold { get; } = new(Bold: true);

    public static CellStyle Header { get; } = new(Bold: true, Color: Palette.White, Fill: Palette.Head, Wrap: true, VAlign: VerticalAlign.Center);

    public static CellStyle SubHeader { get; } = new(Bold: true, Fill: Palette.Sub, Align: HorizontalAlign.Center);

    public static CellStyle Title { get; } = new(Bold: true, Size: 14, Border: false);

    public static CellStyle Section { get; } = new(Bold: true, Border: false);

    public static CellStyle Muted { get; } = new(Color: Palette.Muted, Border: false);

    public static CellStyle Note { get; } = new(Color: Palette.Muted, Italic: true, Border: false);

    public static CellStyle Banner { get; } = new(Bold: true, Color: Palette.White, Fill: Palette.Head);

    public static CellStyle Band { get; } = new(Bold: true, Fill: Palette.Band);

    public static CellStyle Total { get; } = new(Bold: true, Fill: Palette.Sub);

    public static CellStyle Check { get; } = new(Align: HorizontalAlign.Center);

    public static CellStyle Link(string format) => new(Color: Palette.Link, Format: format);

    public static CellStyle Number(string format, bool bold = false) => new(Bold: bold, Format: format);

    public static CellStyle RootBreak { get; } = new(Bold: true, Color: Palette.BreakText, Fill: Palette.Break);

    public static CellStyle WithinTolerance { get; } = new(Bold: true, Color: Palette.ToleranceText, Fill: Palette.Tolerance);

    public static CellStyle Consequence { get; } = new(Italic: true, Color: Palette.AttributedText, Fill: Palette.Attributed);

    public static DiffStyle CheckPass { get; } = new(FontColor: Palette.OkText, Bold: true);

    public static DiffStyle CheckFail { get; } = new(FontColor: Palette.BreakText, Bold: true, Fill: Palette.Break);

    public static DiffStyle Red { get; } = new(FontColor: Palette.BreakText, Bold: true, Fill: Palette.Break);

    public static DiffStyle Amber { get; } = new(FontColor: Palette.ToleranceText, Bold: true, Fill: Palette.Tolerance);

    public static DiffStyle Grey { get; } = new(FontColor: Palette.Muted, Fill: Palette.Attributed);
}
