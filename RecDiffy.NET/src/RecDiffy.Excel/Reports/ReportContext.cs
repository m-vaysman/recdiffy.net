using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Reports;

internal sealed record ReportContext(
    ReconciliationResult Result,
    ExcelReportOptions Options,
    FieldMap Fields,
    string ProfileId,
    ReportFacts Facts)
{
    public string Label(string text) => Options.Labels.TryGetValue(text, out var label) ? label : text;

    public IReadOnlyList<string> Labels(params string[] texts) => [.. texts.Select(Label)];

    public string? Field(ReportRole role) => Fields[role];

    public IEnumerable<string> SortCurrencies(IEnumerable<string> currencies) => Ordering.SortCurrencies(currencies, Options.CurrencyOrder);
}

internal sealed record ReportBody(Sheet Sheet, ImmutableArray<DataTab> DataTabs);

internal sealed record ReportDefinition(
    ReportKind Kind,
    ImmutableArray<ReportRole> Required,
    Func<ReportContext, string?> Unavailable,
    Func<ReportContext, ReportBody> Build)
{
    public ReportInfo Info => ReportCatalogue.Of(Kind);
}
