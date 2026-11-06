using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using RecDiffy.Excel.Model;
using CellStyle = RecDiffy.Excel.Model.CellStyle;

namespace RecDiffy.Excel.Writing;

internal sealed record FontKey(bool Bold, bool Italic, bool Strike, bool Underline, string Color, double Size);

internal sealed class StyleTable
{
    private const string FontName = "Arial";
    private const string BorderColor = "D1D5DB";
    private const uint FirstCustomFormat = 164;

    private readonly ImmutableDictionary<CellStyle, uint> _cells;
    private readonly ImmutableDictionary<DiffStyle, uint> _differentials;

    private StyleTable(Stylesheet stylesheet, ImmutableDictionary<CellStyle, uint> cells, ImmutableDictionary<DiffStyle, uint> differentials)
    {
        Stylesheet = stylesheet;
        _cells = cells;
        _differentials = differentials;
    }

    public Stylesheet Stylesheet { get; }

    public uint Cell(CellStyle style) => _cells[style];

    public uint Differential(DiffStyle style) => _differentials[style];

    public static StyleTable From(Model.Workbook workbook)
    {
        var defaultStyle = CellStyle.Plain with { Border = false };
        ImmutableArray<CellStyle> styles =
        [
            defaultStyle,
            .. workbook.Sheets.SelectMany(sheet => sheet.Cells.Values.Select(cell => cell.Style)).Where(style => style != defaultStyle).Distinct(),
        ];
        ImmutableArray<FontKey> fonts = [.. styles.Select(Font).Distinct()];
        ImmutableArray<string> fills = [.. styles.Select(style => style.Fill).OfType<string>().Distinct()];
        ImmutableArray<string> formats = [.. styles.Select(style => style.Format).OfType<string>().Where(format => format is not "General").Distinct()];
        ImmutableArray<DiffStyle> differentials = [.. workbook.Sheets.SelectMany(sheet => sheet.Rules).Select(Differential).OfType<DiffStyle>().Distinct()];

        var fontIndex = fonts.Select((font, index) => (font, index)).ToImmutableDictionary(pair => pair.font, pair => (uint)pair.index);
        var fillIndex = fills.Select((fill, index) => (fill, index)).ToImmutableDictionary(pair => pair.fill, pair => (uint)(pair.index + 2));
        var formatIndex = formats.Select((format, index) => (format, index)).ToImmutableDictionary(pair => pair.format, pair => (uint)(FirstCustomFormat + pair.index));

        var stylesheet = new Stylesheet(
            new NumberingFormats(formats.Select(format => new NumberingFormat { NumberFormatId = formatIndex[format], FormatCode = format })) { Count = (uint)formats.Length },
            new Fonts(fonts.Select(FontElement)) { Count = (uint)fonts.Length },
            new Fills([
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                .. fills.Select(SolidFill),
            ]) { Count = (uint)(fills.Length + 2) },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()), ThinBorder()) { Count = 2 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(styles.Select(style => CellFormat(style, fontIndex, fillIndex, formatIndex))) { Count = (uint)styles.Length },
            new CellStyles(new DocumentFormat.OpenXml.Spreadsheet.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 },
            new DifferentialFormats(differentials.Select(DifferentialElement)) { Count = (uint)differentials.Length });

        return new StyleTable(
            stylesheet,
            styles.Select((style, index) => (style, index)).ToImmutableDictionary(pair => pair.style, pair => (uint)pair.index),
            differentials.Select((style, index) => (style, index)).ToImmutableDictionary(pair => pair.style, pair => (uint)pair.index));
    }

    private static DiffStyle? Differential(ConditionalRule rule) => rule switch
    {
        CellIsRule cellIs => cellIs.Style,
        ExpressionRule expression => expression.Style,
        _ => null,
    };

    private static FontKey Font(CellStyle style) => new(style.Bold, style.Italic, style.Strike, style.Underline, style.Color, style.Size);

    private static Font FontElement(FontKey key) => new(
        [
            .. key.Bold ? [new Bold()] : Array.Empty<OpenXmlElement>(),
            .. key.Italic ? [new Italic()] : Array.Empty<OpenXmlElement>(),
            .. key.Strike ? [new Strike()] : Array.Empty<OpenXmlElement>(),
            .. key.Underline ? [new Underline()] : Array.Empty<OpenXmlElement>(),
            new FontSize { Val = key.Size },
            new Color { Rgb = Argb(key.Color) },
            new FontName { Val = FontName },
            new FontFamilyNumbering { Val = 2 },
        ]);

    private static Fill SolidFill(string color) =>
        new(new PatternFill(new ForegroundColor { Rgb = Argb(color) }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid });

    private static Border ThinBorder() => new(
        new LeftBorder(new Color { Rgb = Argb(BorderColor) }) { Style = BorderStyleValues.Thin },
        new RightBorder(new Color { Rgb = Argb(BorderColor) }) { Style = BorderStyleValues.Thin },
        new TopBorder(new Color { Rgb = Argb(BorderColor) }) { Style = BorderStyleValues.Thin },
        new BottomBorder(new Color { Rgb = Argb(BorderColor) }) { Style = BorderStyleValues.Thin },
        new DiagonalBorder());

    private static CellFormat CellFormat(
        CellStyle style,
        ImmutableDictionary<FontKey, uint> fonts,
        ImmutableDictionary<string, uint> fills,
        ImmutableDictionary<string, uint> formats)
    {
        OpenXmlElement[] alignment = style is { Wrap: false, Align: HorizontalAlign.General, VAlign: VerticalAlign.Bottom } ? [] : [Alignment(style)];
        return new CellFormat(alignment)
        {
            FontId = fonts[Font(style)],
            FillId = style.Fill is { } fill ? fills[fill] : 0u,
            BorderId = style.Border ? 1u : 0u,
            NumberFormatId = style.Format is { } code and not "General" ? formats[code] : 0u,
            FormatId = 0,
            ApplyFont = true,
            ApplyFill = style.Fill is not null,
            ApplyBorder = style.Border,
            ApplyNumberFormat = style.Format is not null,
            ApplyAlignment = alignment.Length > 0 ? true : null,
        };
    }

    private static Alignment Alignment(CellStyle style) => new()
    {
        WrapText = style.Wrap ? true : null,
        Horizontal = style.Align switch
        {
            HorizontalAlign.Left => HorizontalAlignmentValues.Left,
            HorizontalAlign.Center => HorizontalAlignmentValues.Center,
            HorizontalAlign.Right => HorizontalAlignmentValues.Right,
            _ => null,
        },
        Vertical = style.VAlign switch
        {
            VerticalAlign.Top => VerticalAlignmentValues.Top,
            VerticalAlign.Center => VerticalAlignmentValues.Center,
            _ => null,
        },
    };

    private static DifferentialFormat DifferentialElement(DiffStyle style) => new(
        [
            new Font(
            [
                .. style.Bold ? [new Bold()] : Array.Empty<OpenXmlElement>(),
                .. style.Italic ? [new Italic()] : Array.Empty<OpenXmlElement>(),
                .. style.FontColor is { } color ? [new Color { Rgb = Argb(color) }] : Array.Empty<OpenXmlElement>(),
            ]),
            .. style.Fill is { } fill
                ? [new Fill(new PatternFill(new ForegroundColor { Rgb = Argb(fill) }, new BackgroundColor { Rgb = Argb(fill) }) { PatternType = PatternValues.Solid })]
                : Array.Empty<OpenXmlElement>(),
        ]);

    public static HexBinaryValue Argb(string color) => new("FF" + color);
}
