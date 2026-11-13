using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace RecDiffy.Excel.Model;

internal abstract record CellContent;

internal sealed record TextContent(string Text) : CellContent;

internal sealed record NumberContent(decimal Number) : CellContent;

internal sealed record DateContent(DateOnly Date) : CellContent;

internal sealed record FormulaContent(string Formula) : CellContent;

internal sealed record BlankContent : CellContent
{
    public static BlankContent Instance { get; } = new();
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CellAddress(int Row, int Column);

internal sealed record Cell(CellContent Content, CellStyle Style);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CellRange(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    public string A1 => $"{Address.Column(FirstColumn)}{FirstRow.ToInvariant()}:{Address.Column(LastColumn)}{LastRow.ToInvariant()}";
}

internal enum CellOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    Between,
}

internal abstract record ConditionalRule(CellRange Range);

internal sealed record CellIsRule(CellRange Range, CellOperator Operator, IReadOnlyList<string> Formulas, DiffStyle Style) : ConditionalRule(Range);

internal sealed record ExpressionRule(CellRange Range, string Formula, DiffStyle Style) : ConditionalRule(Range);

internal sealed record ColorScaleRule(CellRange Range, string MinColor, string MaxColor) : ConditionalRule(Range);

internal sealed record DataBarRule(CellRange Range, string Color, decimal Minimum, decimal Maximum) : ConditionalRule(Range);

internal sealed record ChartSeries(string NameReference, string ValuesReference, string Color);

internal sealed record BarChart(
    string Title,
    CellAddress Anchor,
    double WidthCm,
    double HeightCm,
    string CategoriesReference,
    IReadOnlyList<ChartSeries> Series,
    string? ValueAxisTitle,
    bool ReverseCategories);

internal sealed record Sheet(
    string Name,
    ImmutableSortedDictionary<CellAddress, Cell> Cells,
    ImmutableList<CellRange> Merges,
    ImmutableSortedDictionary<int, double> ColumnWidths,
    ImmutableSortedDictionary<int, double> RowHeights,
    ImmutableSortedDictionary<int, int> RowOutline,
    CellAddress? Freeze,
    CellRange? AutoFilter,
    ImmutableList<ConditionalRule> Rules,
    ImmutableList<BarChart> Charts,
    bool ShowGridLines,
    bool SummaryBelow)
{
    public static Sheet Empty(string name) => new(
        name,
        ImmutableSortedDictionary.Create<CellAddress, Cell>(AddressOrder.Instance),
        [],
        ImmutableSortedDictionary<int, double>.Empty,
        ImmutableSortedDictionary<int, double>.Empty,
        ImmutableSortedDictionary<int, int>.Empty,
        null,
        null,
        [],
        [],
        false,
        true);
}

internal sealed record DefinedName(string Name, string Reference);

internal sealed record Workbook(IReadOnlyList<Sheet> Sheets, IReadOnlyList<DefinedName> Names);

internal sealed class AddressOrder : IComparer<CellAddress>
{
    public static AddressOrder Instance { get; } = new();

    public int Compare(CellAddress x, CellAddress y) =>
        x.Row != y.Row ? x.Row.CompareTo(y.Row) : x.Column.CompareTo(y.Column);
}

internal static class Address
{
    public static string Column(int column) => column switch
    {
        <= 0 => throw new ArgumentOutOfRangeException(nameof(column)),
        <= 26 => ((char)('A' + column - 1)).ToString(),
        _ => Column((column - 1) / 26) + (char)('A' + (column - 1) % 26),
    };

    public static string Cell(int row, int column) => $"{Column(column)}{row.ToInvariant()}";

    public static string Absolute(int row, int column) => $"${Column(column)}${row.ToInvariant()}";

    public static string Sheet(string sheet) =>
        sheet.All(character => char.IsLetterOrDigit(character) || character is '_') ? sheet : $"'{sheet.Replace("'", "''", StringComparison.Ordinal)}'";
}
