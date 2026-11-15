using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Reports;

internal static class ReportSheet
{
    /// <summary>The first row a report's own content uses, below the head.</summary>
    public const int Body = 6;

    /// <summary>ID and name, office, users, question and cadence, a one-line description, and the as-of date and profile.</summary>
    public static Sheet Head(ReportContext context, ReportInfo info) =>
        Sheet.Empty(info.Tab)
            .Put(1, 1, $"{info.Id} · {context.Label(info.Title)}", Styles.Title)
            .Put(2, 1, $"{info.Office}  ·  Users: {info.Users}  ·  Question: {info.Question}  ·  Cadence: {info.Cadence}", Styles.Muted)
            .Put(3, 1, info.About, CellStyle.Bare)
            .Put(4, 1, "As of", Styles.Muted)
            .PutFormula(4, 2, DataTabs.AsOfName, new CellStyle(Color: Palette.Link, Format: Formats.Date, Border: false, Align: HorizontalAlign.Left))
            .Merge(4, 2, 4, 3)
            .Put(4, 4, $"{context.Options.Title}  ·  Profile {context.ProfileId}  ·  Engine {context.Result.Run.EngineVersion}", Styles.Muted);

    // A data tab leaves a null value's cell empty, and a bare link to an empty cell reads 0 (1900-01-00 as a date); this reads blank, so blank checks fire.
    public static string Link(string reference) => $"=IF(ISBLANK({reference}),\"\",{reference})";
}
