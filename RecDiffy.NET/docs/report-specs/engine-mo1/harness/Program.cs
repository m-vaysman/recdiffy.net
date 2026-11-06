// Runs the RecDiffy engine (built from origin/main) on the fixture-v1 workbook with the profile and
// reference data from RecDiffy.Tests/WorkbookAcceptanceTests.cs, copied verbatim, then writes the
// engine's own result and side-by-side view to JSON. No reconciliation logic lives here.
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using RecDiffy;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

var book = args[0];
var output = args[1];
var commit = args.Length > 2 ? args[2] : "";

var left = XlsxSheet.Read(book, 1);
var right = XlsxSheet.Read(book, 2);
var expected = XlsxSheet.Read(book, 3);
var profile = Reconciler.Compile(ProfileDocument.Parse(V1.Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore()));
var result = Reconciler.Run(profile, V1.Rows("left", left), V1.Rows("right", right), V1.Reference());
var byKey = result.Rows.ToDictionary(row => row.Row.RowKey, StringComparer.Ordinal);

// The acceptance test's checks, counted instead of asserted (xunit cannot be restored offline).
var checks = new List<object>();
var passed = 0;
foreach (var row in expected)
{
    var outcome = row.GetValueOrDefault("ExpectedOutcome")?.ToString();
    var internalIds = V1.Ids(row.GetValueOrDefault("InternalRowIDs"));
    var counterpartyIds = V1.Ids(row.GetValueOrDefault("CounterpartyRowIDs"));
    string? failure = outcome switch
    {
        "EXACT" or "NORMALIZED" => Matched(internalIds, counterpartyIds, FieldOutcome.Exact),
        "FIELD_BREAK" => Matched(internalIds, counterpartyIds, FieldOutcome.Break),
        "INTERNAL_ONLY" => internalIds.All(id => byKey[id].Disposition == Disposition.LeftOnly) ? null : "not LeftOnly",
        "COUNTERPARTY_ONLY" => counterpartyIds.All(id => byKey[id].Disposition == Disposition.RightOnly) ? null : "not RightOnly",
        "AMBIGUOUS" => internalIds.Concat(counterpartyIds).All(id => byKey[id].Disposition == Disposition.Ambiguous) ? null : "not Ambiguous",
        _ => null,
    };
    if (failure is null) passed++;
    else checks.Add(new { caseId = row.GetValueOrDefault("CaseID")?.ToString(), outcome, failure });
}

string? Matched(string[] l, string[] r, FieldOutcome want)
{
    var entry = result.Entries.SingleOrDefault(item => item.Left.Any(member => member.RowKey == l[0]));
    if (entry is null) return "no entry";
    if (entry.Alignment != Alignment.Matched) return $"alignment {entry.Alignment}";
    if (entry.Outcome != want) return $"outcome {entry.Outcome}";
    if (!l.Order(StringComparer.Ordinal).SequenceEqual(entry.Left.Select(m => m.RowKey).Order(StringComparer.Ordinal))) return "left members differ";
    if (!r.Order(StringComparer.Ordinal).SequenceEqual(entry.Right.Select(m => m.RowKey).Order(StringComparer.Ordinal))) return "right members differ";
    return l.Concat(r).All(id => byKey[id].Disposition == Disposition.Matched) ? null : "row not Matched";
}

// The engine's own side-by-side view, one block per currency so amounts are never added across currencies.
var currencies = result.Rows.Where(row => row.Disposition is not Disposition.Suppressed)
    .Select(row => result.Canonical(row.Row).Text("Currency")).Where(c => c.Length > 0).Distinct().Order(StringComparer.Ordinal).ToArray();
var sideBySide = currencies.Select(ccy => new
{
    currency = ccy,
    spec = new { rows = new[] { "Counterparty" }, columns = new[] { "Asset" }, measures = new[] { "Quantity" }, filter = $"Currency = {ccy}" },
    lines = result.BuildSideBySide(new SideBySideSpec(["Counterparty"], ["Asset"], [new MeasureSpec("Quantity")], new FilterSpec("Currency", ccy)))
        .Lines.Select(line => new
        {
            row = line.Row,
            column = line.Column,
            cells = line.Cells.ToDictionary(cell => cell.Key, cell => new
            {
                left = cell.Value.Left, right = cell.Value.Right, delta = cell.Value.Delta,
                leftOnly = cell.Value.LeftOnly, rightOnly = cell.Value.RightOnly, matchedDelta = cell.Value.MatchedDelta,
            }),
        }),
}).ToArray();

var json = new
{
    meta = new
    {
        producedBy = "RecDiffy engine, Reconciler.Run + ReconciliationResult.BuildSideBySide",
        sourceCommit = commit,
        engineVersion = result.Run.EngineVersion,
        profileHash = result.Run.ProfileHash,
        referenceId = result.Run.ReferenceId,
        workbook = Path.GetFileName(book),
        leftRows = left.Count,
        rightRows = right.Count,
    },
    summary = result.Summary,
    acceptance = new { cases = expected.Count, passed, failures = checks },
    sideBySide,
    rows = result.Rows.Select(row => new
    {
        source = row.Row.Source,
        rowKey = row.Row.RowKey,
        disposition = row.Disposition.ToString(),
        entryId = row.EntryId?.Value,
        counterparty = result.Canonical(row.Row).Text("Counterparty"),
        asset = result.Canonical(row.Row).Text("Asset"),
        currency = result.Canonical(row.Row).Text("Currency"),
        quantity = result.Canonical(row.Row).IsNull("Quantity") ? (decimal?)null : result.Canonical(row.Row)["Quantity"].Number,
        diagnostics = row.Diagnostics.Select(d => d.ToString()),
    }),
    entries = result.Entries.Select(entry => new
    {
        id = entry.Id.Value,
        path = entry.PathId,
        alignment = entry.Alignment.ToString(),
        composition = entry.Composition.ToString(),
        outcome = entry.Outcome.ToString(),
        committedBy = entry.Explanation.CommittedBy,
        left = entry.Left.Select(m => m.RowKey),
        right = entry.Right.Select(m => m.RowKey),
        diffs = entry.Diffs.Where(d => d.Outcome is not FieldOutcome.Exact and not FieldOutcome.NotCompared).Select(d => new
        {
            field = d.Field.Name,
            outcome = d.Outcome.ToString(),
            left = result.Format(d.Left),
            right = result.Format(d.Right),
            delta = d.Delta.Kind switch { DeltaKind.Number => d.Delta.Number.ToString(CultureInfo.InvariantCulture), DeltaKind.Days => d.Delta.Days.ToString(CultureInfo.InvariantCulture), _ => "" },
        }),
        trail = entry.Explanation.Trail.Select(t => new { pass = t.PassId, verdict = t.Verdict.ToString(), detail = t.Detail }),
    }),
};
File.WriteAllText(output, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"profile {result.Run.ProfileHash[..12]} engine {result.Run.EngineVersion}");
Console.WriteLine($"summary {result.Summary}");
Console.WriteLine($"acceptance {passed}/{expected.Count} cases pass; {checks.Count} fail");
Console.WriteLine($"currencies {string.Join(",", currencies)}");

// ---- Copied from RecDiffy.Tests/WorkbookAcceptanceTests.cs (origin/main), unchanged apart from being static members here.
static class V1
{
    public static string[] Ids(object? cell) =>
        cell?.ToString() is { Length: > 0 } text
            ? [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

    public static RecordSource Rows(string name, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        RecordSources.FromObjects(name, rows, Map());

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

    public const string Profile = """
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

// ---- Copied from RecDiffy.Tests/Acceptance/XlsxSheet.cs (origin/main), unchanged.
static class XlsxSheet
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Read(string path, int sheetIndex)
    {
        using var zip = ZipFile.OpenRead(path);
        var strings = SharedStrings(zip);
        var sheet = zip.GetEntry($"xl/worksheets/sheet{sheetIndex}.xml") ?? throw new FileNotFoundException(path);
        using var stream = sheet.Open();
        var rows = XDocument.Load(stream).Descendants(Spreadsheet + "row").Select(row => Cells(row, strings)).ToArray();
        var header = rows[0];
        return [.. rows.Skip(1).Select(row => (IReadOnlyDictionary<string, object?>)header.Keys.ToDictionary(
            column => header[column]?.ToString() ?? column,
            column => row.TryGetValue(column, out var value) ? value : null,
            StringComparer.Ordinal))];
    }

    private static IReadOnlyList<string> SharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];
        using var stream = entry.Open();
        return [.. XDocument.Load(stream).Descendants(Spreadsheet + "si").Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value)))];
    }

    private static Dictionary<string, object?> Cells(XElement row, IReadOnlyList<string> strings) =>
        row.Elements(Spreadsheet + "c").ToDictionary(cell => Column(cell.Attribute("r")!.Value), cell => Value(cell, strings), StringComparer.Ordinal);

    private static string Column(string reference) => new(reference.TakeWhile(char.IsLetter).ToArray());

    private static object? Value(XElement cell, IReadOnlyList<string> strings)
    {
        var inline = cell.Element(Spreadsheet + "is");
        var text = inline is not null
            ? string.Concat(inline.Descendants(Spreadsheet + "t").Select(item => item.Value))
            : cell.Element(Spreadsheet + "v")?.Value;
        if (string.IsNullOrEmpty(text))
            return null;
        var kind = cell.Attribute("t")?.Value;
        var raw = kind is "s" ? strings[int.Parse(text, CultureInfo.InvariantCulture)] : text;
        if (raw.Length > 10 && raw[10] is 'T' && raw.EndsWith('Z'))
            return raw[..10];
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && kind is not "s"
            ? number
            : raw;
    }
}
