using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using RecDiffy.Excel.Model;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace RecDiffy.Excel.Writing;

// Writes a workbook straight into the package: each worksheet is streamed row by row through OpenXmlWriter, so no
// worksheet object tree is built. Only the style sheet, the shared-string table and the small workbook part exist as
// whole documents, and none of them is proportional to the number of cells (#398).
internal sealed class StreamingSpreadsheetWriter : IWorkbookWriter
{
    private const long EmuPerCm = 360_000;
    private static readonly DateOnly Epoch = new(1899, 12, 30);

    public void Write(Model.Workbook workbook, Stream output)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(output);
        var styles = StyleTable.From(workbook);
        var strings = SharedStrings(workbook);
        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.AddNewPart<WorkbookStylesPart>().Stylesheet = styles.Stylesheet;
        WriteSharedStrings(workbookPart.AddNewPart<SharedStringTablePart>(), strings.Order);
        var sheets = new List<(Model.Sheet Sheet, string Id)>(workbook.Sheets.Count);
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            var sheet = workbook.Sheets[index];
            sheets.Add((sheet, WriteSheet(workbookPart, sheet, index, styles, strings.Index)));
        }

        workbookPart.Workbook = new S.Workbook(
            new S.BookViews(new S.WorkbookView { ActiveTab = 0 }),
            new S.Sheets(sheets.Select((sheet, index) => new S.Sheet { Name = sheet.Sheet.Name, SheetId = (uint)(index + 1), Id = sheet.Id })),
            new S.DefinedNames(workbook.Names.Select(name => new S.DefinedName(name.Reference) { Name = name.Name })),
            new S.CalculationProperties { CalculationId = 191029, FullCalculationOnLoad = true });
    }

    private static (ImmutableArray<string> Order, ImmutableDictionary<string, int> Index) SharedStrings(Model.Workbook workbook)
    {
        ImmutableArray<string> order =
        [
            .. workbook.Sheets
                .SelectMany(sheet => sheet.Cells.Values)
                .Select(cell => cell.Content)
                .OfType<TextContent>()
                .Select(text => CellText.Clean(text.Text))
                .Distinct(StringComparer.Ordinal),
        ];
        return (order, order.Select((text, index) => (text, index)).ToImmutableDictionary(pair => pair.text, pair => pair.index, StringComparer.Ordinal));
    }

    private static void WriteSharedStrings(SharedStringTablePart part, ImmutableArray<string> order)
    {
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new S.SharedStringTable { Count = (uint)order.Length, UniqueCount = (uint)order.Length });
        foreach (var text in order)
            writer.WriteElement(new S.SharedStringItem(new S.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        writer.WriteEndElement();
    }

    private static string WriteSheet(WorkbookPart workbookPart, Model.Sheet sheet, int index, StyleTable styles, ImmutableDictionary<string, int> strings)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        var drawing = sheet.Charts.IsEmpty ? null : AddCharts(part, sheet.Charts);
        using (var writer = OpenXmlWriter.Create(part))
            WriteWorksheet(writer, sheet, index, styles, strings, drawing);
        return workbookPart.GetIdOfPart(part);
    }

    private static void WriteWorksheet(
        OpenXmlWriter writer,
        Model.Sheet sheet,
        int index,
        StyleTable styles,
        ImmutableDictionary<string, int> strings,
        string? drawing)
    {
        writer.WriteStartElement(new S.Worksheet());
        writer.WriteElement(new S.SheetProperties(
            new S.OutlineProperties { SummaryBelow = sheet.SummaryBelow },
            new S.PageSetupProperties { FitToPage = true }));
        writer.WriteElement(new S.SheetViews(SheetView(sheet, index)));
        writer.WriteElement(new S.SheetFormatProperties { DefaultRowHeight = 15 });
        if (!sheet.ColumnWidths.IsEmpty)
        {
            writer.WriteElement(new S.Columns(sheet.ColumnWidths.Select(width => new S.Column
            {
                Min = (uint)width.Key,
                Max = (uint)width.Key,
                Width = width.Value,
                CustomWidth = true,
            })));
        }

        WriteRows(writer, sheet, styles, strings);
        if (sheet.AutoFilter is { } filter)
            writer.WriteElement(new S.AutoFilter { Reference = filter.A1 });
        if (!sheet.Merges.IsEmpty)
            writer.WriteElement(new S.MergeCells(sheet.Merges.Select(merge => new S.MergeCell { Reference = merge.A1 })) { Count = (uint)sheet.Merges.Count });
        for (var priority = 0; priority < sheet.Rules.Count; priority++)
            writer.WriteElement(ConditionalFormatting(sheet.Rules[priority], priority + 1, styles));
        writer.WriteElement(new S.PageMargins { Left = 0.5, Right = 0.5, Top = 0.6, Bottom = 0.6, Header = 0.3, Footer = 0.3 });
        writer.WriteElement(new S.PageSetup { Orientation = S.OrientationValues.Landscape, FitToWidth = 1, FitToHeight = 0 });
        if (drawing is not null)
            writer.WriteElement(new S.Drawing { Id = drawing });
        writer.WriteEndElement();
    }

    private static void WriteRows(OpenXmlWriter writer, Model.Sheet sheet, StyleTable styles, ImmutableDictionary<string, int> strings)
    {
        writer.WriteStartElement(new S.SheetData());
        var styled = sheet.RowHeights.Keys.Concat(sheet.RowOutline.Keys).Distinct().Order().ToArray();
        var nextStyled = 0;
        using var cells = sheet.Cells.GetEnumerator();
        var hasCell = cells.MoveNext();
        while (hasCell || nextStyled < styled.Length)
        {
            var cellRow = hasCell ? cells.Current.Key.Row : int.MaxValue;
            if (nextStyled < styled.Length && styled[nextStyled] < cellRow)
            {
                writer.WriteStartElement(Row(styled[nextStyled], sheet));
                writer.WriteEndElement();
                nextStyled++;
                continue;
            }

            writer.WriteStartElement(Row(cellRow, sheet));
            while (hasCell && cells.Current.Key.Row == cellRow)
            {
                writer.WriteElement(Cell(cells.Current.Key, cells.Current.Value, styles, strings));
                hasCell = cells.MoveNext();
            }

            writer.WriteEndElement();
            if (nextStyled < styled.Length && styled[nextStyled] == cellRow)
                nextStyled++;
        }

        writer.WriteEndElement();
    }

    private static S.Row Row(int number, Model.Sheet sheet) => new()
    {
        RowIndex = (uint)number,
        Height = sheet.RowHeights.TryGetValue(number, out var height) ? height : null,
        CustomHeight = sheet.RowHeights.ContainsKey(number) ? true : null,
        OutlineLevel = sheet.RowOutline.TryGetValue(number, out var level) ? (byte)level : null,
    };

    private static S.SheetView SheetView(Model.Sheet sheet, int index) => new(
        sheet.Freeze is { } freeze && (freeze.Row > 1 || freeze.Column > 1) ? [FrozenPane(freeze)] : Array.Empty<OpenXmlElement>())
    {
        WorkbookViewId = 0,
        ShowGridLines = sheet.ShowGridLines,
        TabSelected = index == 0 ? true : null,
        ZoomScale = 100,
    };

    private static S.Pane FrozenPane(CellAddress freeze) => new()
    {
        HorizontalSplit = freeze.Column > 1 ? freeze.Column - 1 : null,
        VerticalSplit = freeze.Row > 1 ? freeze.Row - 1 : null,
        TopLeftCell = Address.Cell(freeze.Row, freeze.Column),
        ActivePane = (freeze.Row > 1, freeze.Column > 1) switch
        {
            (true, true) => S.PaneValues.BottomRight,
            (true, false) => S.PaneValues.BottomLeft,
            _ => S.PaneValues.TopRight,
        },
        State = S.PaneStateValues.Frozen,
    };

    private static S.Cell Cell(CellAddress address, Model.Cell cell, StyleTable styles, ImmutableDictionary<string, int> strings)
    {
        var reference = Address.Cell(address.Row, address.Column);
        var style = styles.Cell(cell.Style);
        return cell.Content switch
        {
            TextContent text => new S.Cell(new S.CellValue(strings[CellText.Clean(text.Text)].ToInvariant()))
            {
                CellReference = reference,
                StyleIndex = style,
                DataType = S.CellValues.SharedString,
            },
            NumberContent number => new S.Cell(new S.CellValue(number.Number.ToInvariant())) { CellReference = reference, StyleIndex = style },
            DateContent date => new S.Cell(new S.CellValue((date.Date.DayNumber - Epoch.DayNumber).ToInvariant())) { CellReference = reference, StyleIndex = style },
            FormulaContent formula => new S.Cell(new S.CellFormula(CellText.Clean(formula.Formula))) { CellReference = reference, StyleIndex = style },
            _ => new S.Cell { CellReference = reference, StyleIndex = style },
        };
    }

    private static S.ConditionalFormatting ConditionalFormatting(ConditionalRule rule, int priority, StyleTable styles) =>
        new(Rule(rule, priority, styles)) { SequenceOfReferences = new ListValue<StringValue>([new StringValue(rule.Range.A1)]) };

    private static S.ConditionalFormattingRule Rule(ConditionalRule rule, int priority, StyleTable styles) => rule switch
    {
        CellIsRule cellIs => new S.ConditionalFormattingRule(cellIs.Formulas.Select(formula => new S.Formula(CellText.Clean(formula))))
        {
            Type = S.ConditionalFormatValues.CellIs,
            Operator = cellIs.Operator switch
            {
                CellOperator.Equal => S.ConditionalFormattingOperatorValues.Equal,
                CellOperator.NotEqual => S.ConditionalFormattingOperatorValues.NotEqual,
                CellOperator.GreaterThan => S.ConditionalFormattingOperatorValues.GreaterThan,
                CellOperator.GreaterThanOrEqual => S.ConditionalFormattingOperatorValues.GreaterThanOrEqual,
                CellOperator.LessThan => S.ConditionalFormattingOperatorValues.LessThan,
                _ => S.ConditionalFormattingOperatorValues.Between,
            },
            FormatId = styles.Differential(cellIs.Style),
            Priority = priority,
        },
        ExpressionRule expression => new S.ConditionalFormattingRule(new S.Formula(CellText.Clean(expression.Formula)))
        {
            Type = S.ConditionalFormatValues.Expression,
            FormatId = styles.Differential(expression.Style),
            Priority = priority,
        },
        ColorScaleRule scale => new S.ConditionalFormattingRule(new S.ColorScale(
            new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Number, Val = "0" },
            new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Max },
            new S.Color { Rgb = StyleTable.Argb(scale.MinColor) },
            new S.Color { Rgb = StyleTable.Argb(scale.MaxColor) }))
        {
            Type = S.ConditionalFormatValues.ColorScale,
            Priority = priority,
        },
        DataBarRule bar => new S.ConditionalFormattingRule(new S.DataBar(
            new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Number, Val = bar.Minimum.ToInvariant() },
            new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Number, Val = bar.Maximum.ToInvariant() },
            new S.Color { Rgb = StyleTable.Argb(bar.Color) }))
        {
            Type = S.ConditionalFormatValues.DataBar,
            Priority = priority,
        },
        _ => throw new NotSupportedException(rule.GetType().Name),
    };

    private static string AddCharts(WorksheetPart part, ImmutableList<Model.BarChart> charts)
    {
        var drawings = part.AddNewPart<DrawingsPart>();
        var anchors = charts.Select((chart, index) =>
        {
            var chartPart = drawings.AddNewPart<ChartPart>();
            chartPart.ChartSpace = ChartSpace(chart);
            return Anchor(chart, drawings.GetIdOfPart(chartPart), index);
        }).ToArray();
        drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(anchors);
        return part.GetIdOfPart(drawings);
    }

    private static Xdr.OneCellAnchor Anchor(Model.BarChart chart, string relationship, int index) => new(
        new Xdr.FromMarker(
            new Xdr.ColumnId((chart.Anchor.Column - 1).ToInvariant()),
            new Xdr.ColumnOffset("0"),
            new Xdr.RowId((chart.Anchor.Row - 1).ToInvariant()),
            new Xdr.RowOffset("0")),
        new Xdr.Extent { Cx = (long)(chart.WidthCm * EmuPerCm), Cy = (long)(chart.HeightCm * EmuPerCm) },
        new Xdr.GraphicFrame(
            new Xdr.NonVisualGraphicFrameProperties(
                new Xdr.NonVisualDrawingProperties { Id = (uint)(index + 2), Name = $"Chart {(index + 1).ToInvariant()}" },
                new Xdr.NonVisualGraphicFrameDrawingProperties()),
            new Xdr.Transform(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 0, Cy = 0 }),
            new A.Graphic(new A.GraphicData(new C.ChartReference { Id = relationship }) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" }))
        {
            Macro = "",
        },
        new Xdr.ClientData());

    private static C.ChartSpace ChartSpace(Model.BarChart chart)
    {
        const uint categoryAxis = 500_000_001;
        const uint valueAxis = 500_000_002;
        return new C.ChartSpace(
            new C.Date1904 { Val = false },
            new C.RoundedCorners { Val = false },
            new C.Chart(
                Title(chart.Title, 1200),
                new C.AutoTitleDeleted { Val = false },
                new C.PlotArea(
                    new C.Layout(),
                    new C.BarChart(
                    [
                        new C.BarDirection { Val = C.BarDirectionValues.Bar },
                        new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
                        new C.VaryColors { Val = false },
                        .. chart.Series.Select((series, index) => Series(series, (uint)index, chart.CategoriesReference)),
                        new C.DataLabels(
                            new C.ShowLegendKey { Val = false },
                            new C.ShowValue { Val = true },
                            new C.ShowCategoryName { Val = false },
                            new C.ShowSeriesName { Val = false },
                            new C.ShowPercent { Val = false },
                            new C.ShowBubbleSize { Val = false }),
                        new C.GapWidth { Val = 60 },
                        new C.AxisId { Val = categoryAxis },
                        new C.AxisId { Val = valueAxis },
                    ]),
                    new C.CategoryAxis(
                        new C.AxisId { Val = categoryAxis },
                        new C.Scaling(new C.Orientation { Val = chart.ReverseCategories ? C.OrientationValues.MaxMin : C.OrientationValues.MinMax }),
                        new C.Delete { Val = false },
                        new C.AxisPosition { Val = C.AxisPositionValues.Left },
                        new C.NumberingFormat { FormatCode = "General", SourceLinked = true },
                        new C.MajorTickMark { Val = C.TickMarkValues.None },
                        new C.MinorTickMark { Val = C.TickMarkValues.None },
                        new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
                        new C.CrossingAxis { Val = valueAxis },
                        new C.Crosses { Val = C.CrossesValues.AutoZero },
                        new C.AutoLabeled { Val = true },
                        new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
                        new C.LabelOffset { Val = 100 },
                        new C.NoMultiLevelLabels { Val = false }),
                    new C.ValueAxis(
                    [
                        new C.AxisId { Val = valueAxis },
                        new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                        new C.Delete { Val = false },
                        new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                        new C.MajorGridlines(),
                        .. chart.ValueAxisTitle is { } title ? [Title(title, 1000)] : Array.Empty<OpenXmlElement>(),
                        new C.NumberingFormat { FormatCode = "#,##0", SourceLinked = false },
                        new C.MajorTickMark { Val = C.TickMarkValues.Outside },
                        new C.MinorTickMark { Val = C.TickMarkValues.None },
                        new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
                        new C.CrossingAxis { Val = categoryAxis },
                        new C.Crosses { Val = C.CrossesValues.AutoZero },
                        new C.CrossBetween { Val = C.CrossBetweenValues.Between },
                    ])),
                new C.Legend(new C.LegendPosition { Val = C.LegendPositionValues.Bottom }, new C.Overlay { Val = false }),
                new C.PlotVisibleOnly { Val = true },
                new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Gap }));
    }

    private static C.BarChartSeries Series(ChartSeries series, uint index, string categories) => new(
        new C.Index { Val = index },
        new C.Order { Val = index },
        new C.SeriesText(new C.StringReference(new C.Formula(series.NameReference))),
        new C.ChartShapeProperties(new A.SolidFill(new A.RgbColorModelHex { Val = series.Color })),
        new C.InvertIfNegative { Val = false },
        new C.CategoryAxisData(new C.StringReference(new C.Formula(categories))),
        new C.Values(new C.NumberReference(new C.Formula(series.ValuesReference))));

    private static C.Title Title(string text, int size) => new(
        new C.ChartText(new C.RichText(
            new A.BodyProperties(),
            new A.ListStyle(),
            new A.Paragraph(new A.Run(new A.RunProperties { Language = "en-US", FontSize = size, Bold = true }, new A.Text(text))))),
        new C.Overlay { Val = false });
}
