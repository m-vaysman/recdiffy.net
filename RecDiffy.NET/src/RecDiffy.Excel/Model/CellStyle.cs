namespace RecDiffy.Excel.Model;

internal enum HorizontalAlign
{
    General,
    Left,
    Center,
    Right,
}

internal enum VerticalAlign
{
    Bottom,
    Top,
    Center,
}

internal sealed record CellStyle(
    bool Bold = false,
    bool Italic = false,
    bool Strike = false,
    bool Underline = false,
    string Color = Palette.Ink,
    string? Fill = null,
    string? Format = null,
    bool Border = true,
    bool Wrap = false,
    HorizontalAlign Align = HorizontalAlign.General,
    VerticalAlign VAlign = VerticalAlign.Bottom,
    double Size = 10)
{
    public static CellStyle Plain { get; } = new();

    public static CellStyle Bare { get; } = new(Border: false);
}

internal sealed record DiffStyle(string? FontColor = null, bool Bold = false, bool Italic = false, string? Fill = null);

internal static class Palette
{
    public const string Ink = "1F2937";
    public const string Muted = "6B7280";
    public const string White = "FFFFFF";
    public const string Link = "008000";
    public const string Head = "1F2937";
    public const string Sub = "E5E7EB";
    public const string Break = "FDE2E1";
    public const string Tolerance = "FEF3C7";
    public const string Attributed = "F3F4F6";
    public const string Ok = "DCFCE7";
    public const string Card = "F8FAFC";
    public const string Band = "EEF2F7";
    public const string Input = "FEF3C7";
    public const string BreakText = "9B1C1C";
    public const string ToleranceText = "92400E";
    public const string AttributedText = "6B7280";
    public const string OkText = "166534";
}

internal static class Formats
{
    public const string Amount = "#,##0.00;(#,##0.00);\"-\"";
    public const string Quantity = "#,##0;(#,##0);\"-\"";
    public const string Count = "#,##0;-#,##0;\"-\"";
    public const string SignedCount = "+#,##0;-#,##0;\"-\"";
    public const string Percent = "0.0%";
    public const string Date = "yyyy-mm-dd";
    public const string Integer = "0";
    public const string Price = "General";
}
