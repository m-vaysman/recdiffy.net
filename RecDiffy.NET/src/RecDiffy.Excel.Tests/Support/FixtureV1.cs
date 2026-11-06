using RecDiffy.RulePacks.Trading;
using RecDiffy.Tests.Acceptance;

namespace RecDiffy.Excel.Tests.Support;

// The fixture-v1 run from RecDiffy.Tests/WorkbookAcceptanceTests.cs: same workbook, profile, source map and reference data.
internal static class FixtureV1
{
    public static readonly DateOnly AsOf = new(2026, 11, 4);

    private static readonly Lazy<(CompiledProfile Profile, ReconciliationResult Result)> Cached = new(Run);

    public static CompiledProfile Profile => Cached.Value.Profile;

    public static ReconciliationResult Result => Cached.Value.Result;

    public static string Workbook => Path.Combine(Paths.Repo, "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx");

    public static ExcelReportOptions Options(params ReportKind[] reports) => new()
    {
        AsOf = AsOf,
        Profile = Profile,
        Title = "fixture-v1 acceptance run",
        Reports = reports.Length == 0 ? null : reports,
    };

    private static (CompiledProfile, ReconciliationResult) Run()
    {
        var left = XlsxSheet.Read(Workbook, 1);
        var right = XlsxSheet.Read(Workbook, 2);
        var profile = Reconciler.Compile(ProfileDocument.Parse(ProfileJson), TradingRulePack.AddTo(RuleRegistry.CreateCore()));
        return (profile, Reconciler.Run(profile, Rows("left", left), Rows("right", right), Reference()));
    }

    public static RecordSource Rows(string name, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        RecordSources.FromObjects(name, rows, Map());

    public static (RecordSource Left, RecordSource Right) Sources() =>
        (Rows("left", XlsxSheet.Read(Workbook, 1)), Rows("right", XlsxSheet.Read(Workbook, 2)));

    private static SourceMap<IReadOnlyDictionary<string, object?>> Map() =>
        new SourceMap<IReadOnlyDictionary<string, object?>>()
            .Text("RowID", row => Text(row, "RowID"))
            .Text("ExecutionRef", row => Text(row, "ExecutionRef"))
            .Text("CounterpartyName", row => Text(row, "CounterpartyName"))
            .Text("AssetClass", row => Text(row, "AssetClass"))
            .Text("InstrumentID", row => Text(row, "InstrumentID"))
            .Text("TradeDate", row => Text(row, "TradeDate"))
            .Text("SettleDate", row => Text(row, "SettleDate"))
            .Text("MaturityDate", row => Text(row, "MaturityDate"))
            .Text("Direction", row => Text(row, "Direction"))
            .Text("Perspective", row => Text(row, "Perspective"))
            .Text("Currency", row => Text(row, "Currency"))
            .Decimal("Quantity", row => Decimal(row, "Quantity"))
            .Decimal("PriceOrRate", row => Decimal(row, "PriceOrRate"))
            .Decimal("FixedRate", row => Decimal(row, "FixedRate"));

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) ? value?.ToString() : null;

    private static decimal? Decimal(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is decimal number ? number
        : row.TryGetValue(column, out value) && decimal.TryParse(value?.ToString(), out var parsed) ? parsed
        : null;

    public static ReferenceDataSet Reference()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elm International Bank"] = "ELM",
            ["ELM INTL"] = "ELM",
            ["Granite Markets PLC"] = "GRANITE",
            ["GRANITE MKT"] = "GRANITE",
            ["Falcon Asset Partners"] = "FALCON",
            ["Falcon AP"] = "FALCON",
            ["Beacon Capital LLP"] = "BEACON",
            ["Beacon Cap"] = "BEACON",
            ["Harbor Hedge Fund LP"] = "HARBOR",
            ["Harbor Fund"] = "HARBOR",
            ["Alder Bank NA"] = "ALDER",
            ["ALDER BK"] = "ALDER",
            ["Cedar Securities Ltd"] = "CEDAR",
            ["CEDAR SEC"] = "CEDAR",
            ["Driftwood Fund LP"] = "DRIFTWOOD",
            ["Driftwood HF"] = "DRIFTWOOD",
        };
        return ReferenceDataSet.Create("fixture-v1", new DateOnly(2026, 11, 4), "fixture-v1", aliases, aliases, new Dictionary<string, string>());
    }

    public const string ProfileJson = """
        {
          "formatVersion": 1,
          "profileId": "fixture-v1",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "RowID" }, "right": { "rowKey": "RowID" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "Asset", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "MaturityDate", "type": "Date", "role": "Economic" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "FixedRate", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "ExecRef", "type": "Identifier", "role": "Identity" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "CounterpartyName", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "InstrumentID", "to": "Instrument", "scheme": "INST" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "Asset" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                { "rule": "identifier.map", "from": "ExecutionRef", "to": "ExecRef", "scheme": "EXEC" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "MaturityDate", "to": "MaturityDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "PriceOrRate", "to": "Price" },
                { "rule": "decimal.parse", "from": "FixedRate", "to": "FixedRate" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Direction", "quantity": "Quantity", "asset": "Asset" },
                { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "CounterpartyName", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "InstrumentID", "to": "Instrument", "scheme": "INST" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "Asset" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                { "rule": "identifier.map", "from": "ExecutionRef", "to": "ExecRef", "scheme": "EXEC" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "MaturityDate", "to": "MaturityDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "PriceOrRate", "to": "Price" },
                { "rule": "decimal.parse", "from": "FixedRate", "to": "FixedRate" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Direction", "quantity": "Quantity", "perspective": "Perspective", "invertWhen": "Sender", "asset": "Asset" },
                { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" }
              ]
            }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "shared", "pass": "match.naturalKey", "key": ["ExecRef"] },
            { "id": "natural", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "Asset", "TradeDate", "Side", "Quantity"] }
          ] } ]
        }
        """;
}

internal static class Paths
{
    public static string Repo { get; } = FindRepo();

    public static string Specs => Path.Combine(Repo, "RecDiffy.NET", "docs", "report-specs");

    public static string Scratch(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "recdiffy-excel-tests", name, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx")) is false)
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
