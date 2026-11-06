using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Excel.Reports;
using RecDiffy.Excel.Writing;

namespace RecDiffy.Excel;

/// <summary>Writes formatted Excel reports, one .xlsx file per report, from a RecDiffy <see cref="ReconciliationResult"/>.</summary>
public static class RecDiffyExcel
{
    private static readonly IWorkbookWriter Writer = new OpenXmlWorkbookWriter();

    /// <summary>Writes one report to a stream. Returns the outcome; when the report is skipped, nothing is written and the reason is returned.</summary>
    public static ReportOutcome WriteReport(this ReconciliationResult result, ReportKind kind, Stream output, ExcelReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        var context = Context(result, options);
        var composed = Compose(context, ReportRegistry.Of(kind));
        if (composed.Workbook is { } workbook)
            Writer.Write(workbook, output);
        return new ReportOutcome(kind, composed.FileName, null, composed.SkipReason);
    }

    /// <summary>Writes one file per selected report into a folder. Returns, for each report, the file path or the reason it was skipped.</summary>
    public static IReadOnlyList<ReportOutcome> WriteAll(this ReconciliationResult result, string folder, ExcelReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);
        var context = Context(result, options);
        var composed = Selected(options).Select(definition => Compose(context, definition)).ToArray();
        if (composed.Any(item => item.Workbook is not null))
            Directory.CreateDirectory(folder);
        return [.. composed.Select(item => item.Workbook is { } workbook ? Save(workbook, folder, item) : new ReportOutcome(item.Kind, item.FileName, null, item.SkipReason))];
    }

    /// <summary>Runs <see cref="Reconciler.Run"/> and then <see cref="WriteAll"/>. The profile also supplies the profile id, tolerances and pass order.</summary>
    public static IReadOnlyList<ReportOutcome> RunAndWrite(
        CompiledProfile profile,
        RecordSource left,
        RecordSource right,
        ReferenceDataSet reference,
        string folder,
        ExcelReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);
        var result = Reconciler.Run(profile, left, right, reference);
        return result.WriteAll(folder, options with { Profile = options.Profile ?? profile });
    }

    /// <summary>The file name a report is written to: &lt;ID&gt;_&lt;ReportName&gt;_&lt;profileId&gt;_&lt;asOf yyyy-MM-dd&gt;.xlsx.</summary>
    public static string FileName(ReportKind kind, string profileId, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        var info = ReportCatalogue.Of(kind);
        var safe = new string([.. profileId.Select(character => Path.GetInvalidFileNameChars().Contains(character) || char.IsWhiteSpace(character) ? '-' : character)]);
        return $"{info.Id}_{info.Name}_{safe}_{asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx";
    }

    internal sealed record Composed(ReportKind Kind, string FileName, Workbook? Workbook, string? SkipReason);

    internal static ReportContext Context(ReconciliationResult result, ExcelReportOptions options)
    {
        var map = options.FieldMap ?? (options.Profile is { } profile ? FieldMap.FromProfile(profile) : FieldMap.Empty);
        var profileId = options.ProfileId ?? options.Profile?.ProfileId ?? result.Run.ProfileHash[..Math.Min(12, result.Run.ProfileHash.Length)];
        return new ReportContext(result, options, map, profileId, FactsBuilder.Build(result, map, options));
    }

    internal static Composed Compose(ReportContext context, ReportDefinition definition)
    {
        var fileName = FileName(definition.Kind, context.ProfileId, context.Options.AsOf);
        var missing = definition.Required.Where(role => context.Fields.IsMapped(role) is false).ToArray();
        var skip = missing.Length > 0
            ? $"Unmapped report roles: {string.Join(", ", missing)}. Map them in ExcelReportOptions.FieldMap."
            : definition.Unavailable(context);
        return skip is null
            ? new Composed(definition.Kind, fileName, Assemble(context, definition), null)
            : new Composed(definition.Kind, fileName, null, skip);
    }

    private static Workbook Assemble(ReportContext context, ReportDefinition definition)
    {
        var body = definition.Build(context);
        ImmutableArray<Sheet> sheets =
        [
            body.Sheet,
            .. body.DataTabs.DistinctBy(tab => tab.Name).Select(tab => tab.Sheet),
            DataTabs.RunInfo(context, definition.Info),
        ];
        return new Workbook(sheets, [new DefinedName(DataTabs.AsOfName, DataTabs.AsOfReference)]);
    }

    private static IEnumerable<ReportDefinition> Selected(ExcelReportOptions options) =>
        (options.Reports ?? Enum.GetValues<ReportKind>()).Distinct().Select(ReportRegistry.Of);

    private static ReportOutcome Save(Workbook workbook, string folder, Composed item)
    {
        var path = Path.Combine(folder, item.FileName);
        using (var stream = File.Create(path))
            Writer.Write(workbook, stream);
        return new ReportOutcome(item.Kind, item.FileName, path, null);
    }
}
