using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// Issue #91: row dispositions, the run summary, and entry membership do not depend on input order.
public class OrderIndependenceTests
{
    private static readonly int[] Seeds = [.. Enumerable.Range(1, 25)];

    [Fact]
    public void Fixture_v1_dispositions_ignore_input_order()
    {
        var book = Path.Combine(RepoRoot(), "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx");
        var left = Acceptance.XlsxSheet.Read(book, 1);
        var right = Acceptance.XlsxSheet.Read(book, 2);
        var profile = Reconciler.Compile(ProfileDocument.Parse(FixtureProfile), RuleRegistry.CreateCore().Let(TradingRulePack.AddTo));
        var baseline = Run(profile, left, right, FixtureMap(), FixtureReference());

        Assert.Equal(500, left.Count + right.Count);
        Assert.Equal(left.Count + right.Count, baseline.Rows.Count);
        Assert.Contains(baseline.Rows, row => row.Disposition == Disposition.Matched);
        Assert.Contains(baseline.Rows, row => row.Disposition == Disposition.LeftOnly);
        Assert.Contains(baseline.Rows, row => row.Disposition == Disposition.RightOnly);
        Assert.Contains(baseline.Rows, row => row.Disposition == Disposition.Ambiguous);

        AssertSameOrder(baseline, seed => Run(profile, Shuffle(left, seed), Shuffle(right, seed), FixtureMap(), FixtureReference()));
    }

    [Fact]
    public void Lifecycle_package_group_ambiguous_and_duplicate_dispositions_ignore_input_order()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(PathsProfile), RuleRegistry.CreateCore());
        var baseline = Run(profile, PathsLeft, PathsRight, PathsMap(), ReferenceDataSet.Empty);
        AssertPaths(baseline);

        AssertSameOrder(baseline, seed => Run(profile, Shuffle(PathsLeft, seed), Shuffle(PathsRight, seed), PathsMap(), ReferenceDataSet.Empty));
    }

    private static void AssertSameOrder(ReconciliationResult baseline, Func<int, ReconciliationResult> run)
    {
        var mismatches = Seeds
            .SelectMany(seed => Mismatches(baseline, run(seed), seed))
            .ToArray();
        Assert.True(mismatches.Length == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static IEnumerable<string> Mismatches(ReconciliationResult baseline, ReconciliationResult shuffled, int seed)
    {
        if (baseline.Summary != shuffled.Summary)
            yield return $"seed {seed}: summary {shuffled.Summary} vs {baseline.Summary}";

        var expected = Fingerprint(baseline);
        var actual = Fingerprint(shuffled);
        foreach (var key in expected.Keys.Union(actual.Keys).OrderBy(key => key.Source, StringComparer.Ordinal).ThenBy(key => key.RowKey, StringComparer.Ordinal))
        {
            expected.TryGetValue(key, out var baselineDisposition);
            actual.TryGetValue(key, out var shuffledDisposition);
            if (baselineDisposition != shuffledDisposition)
                yield return $"seed {seed}: {key.Source}/{key.RowKey} disposition [{shuffledDisposition}] baseline [{baselineDisposition}]";
        }

        var baselineEntries = Entries(baseline);
        var shuffledEntries = Entries(shuffled);
        if (baselineEntries != shuffledEntries)
            yield return $"seed {seed}: entries differ{Environment.NewLine}baseline:{Environment.NewLine}{baselineEntries}{Environment.NewLine}shuffled:{Environment.NewLine}{shuffledEntries}";
    }

    private static Dictionary<RowIdentity, string> Fingerprint(ReconciliationResult result) =>
        result.Rows
            .GroupBy(row => new RowIdentity(row.Row.Source, row.Row.RowKey))
            .ToDictionary(
                group => group.Key,
                group => string.Join(",", group.Select(row => row.Disposition.ToString()).OrderBy(name => name, StringComparer.Ordinal)));

    private static string Entries(ReconciliationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Entries
                .Select(entry => $"{entry.Id} {Members(entry.Left)} | {Members(entry.Right)}")
                .OrderBy(line => line, StringComparer.Ordinal));

    private static string Members(IReadOnlyList<SourceRef> members) =>
        string.Join(",", members.Select(member => $"{member.Source}:{member.RowKey}").OrderBy(text => text, StringComparer.Ordinal));

    private static void AssertPaths(ReconciliationResult result)
    {
        Assert.Equal(PathsLeft.Length + PathsRight.Length, result.Rows.Count);
        Assert.Equal("Matched", DispositionOf(result, "left", "L-PAY"));
        Assert.Equal("Matched", DispositionOf(result, "left", "L-REC"));
        Assert.Equal("Matched", DispositionOf(result, "right", "R-PKG"));
        Assert.Contains(result.Entries, entry => entry.Alignment == Alignment.Matched && entry.Composition.HasFlag(Composition.Package));

        Assert.Equal("Matched", DispositionOf(result, "left", "A1"));
        Assert.Equal("Matched", DispositionOf(result, "left", "A2"));
        Assert.Equal("Matched", DispositionOf(result, "left", "A3"));
        Assert.Equal("Matched", DispositionOf(result, "right", "B1"));
        Assert.Contains(result.Entries, entry => entry.Alignment == Alignment.Matched && entry.Composition.HasFlag(Composition.ManyToOne));

        Assert.Equal("Matched", DispositionOf(result, "left", "L-LIFE"));
        Assert.Equal("Matched", DispositionOf(result, "right", "R-CORR"));
        Assert.Equal("Suppressed", DispositionOf(result, "right", "R-NEW"));
        Assert.Equal("Suppressed", DispositionOf(result, "right", "R-CANC"));
        Assert.Contains(result.Entries, entry => entry.Alignment == Alignment.Matched && entry.Composition.HasFlag(Composition.LifecycleCollapsed));

        Assert.Equal("Ambiguous", DispositionOf(result, "left", "AL1"));
        Assert.Equal("Ambiguous", DispositionOf(result, "left", "AL2"));
        Assert.Equal("Ambiguous", DispositionOf(result, "right", "AR1"));

        Assert.Equal("LeftOnly,LeftOnly", DispositionOf(result, "left", "DUP-1"));
        Assert.Contains(result.Rows.Where(row => row.Row.RowKey == "DUP-1"), row => row.Diagnostics.Any(diagnostic => diagnostic.Code == "DuplicateRowCollapsed"));
    }

    private static string DispositionOf(ReconciliationResult result, string source, string rowKey) =>
        string.Join(
            ",",
            result.Rows
                .Where(row => row.Row.Source == source && row.Row.RowKey == rowKey)
                .Select(row => row.Disposition.ToString())
                .OrderBy(name => name, StringComparer.Ordinal));

    private static ReconciliationResult Run<T>(
        CompiledProfile profile,
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        SourceMap<T> map,
        ReferenceDataSet reference) =>
        Reconciler.Run(profile, RecordSources.FromObjects("left", left, map), RecordSources.FromObjects("right", right, map), reference);

    private static T[] Shuffle<T>(IReadOnlyList<T> rows, int seed)
    {
        var copy = rows.ToArray();
        var random = new Random(seed);
        for (var index = copy.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    private readonly record struct RowIdentity(string Source, string RowKey);

    private sealed record Trade(
        string Id,
        string Counterparty,
        string Instrument,
        string Side,
        string Quantity,
        string Price,
        string? NetCash,
        string? Uti = null,
        string? EventAction = null,
        string? EventTarget = null,
        string? PackageRef = null,
        string? LegRole = null);

    private static readonly Trade[] PathsLeft =
    [
        new("L-PAY", "DELTA", "SWAP-1", "PAY_FIXED", "1000000", "3.5", "278961.22", "U-PKG", PackageRef: "PKG-1", LegRole: "LEG_1_PAY"),
        new("L-REC", "DELTA", "SWAP-1", "REC_FIXED", "1000000", "3.2", "259124.12", "U-PKG", PackageRef: "PKG-1", LegRole: "LEG_2_RECEIVE"),
        new("A1", "GAMMA", "EQ-3", "BUY", "300", "10.00", "3000.00"),
        new("A2", "GAMMA", "EQ-3", "BUY", "200", "10.00", "2000.00"),
        new("A3", "GAMMA", "EQ-3", "BUY", "500", "10.00", "5000.00"),
        new("L-LIFE", "ACME", "EQ-4", "BUY", "2000", "105.149", "210298.00", "U-LIFE"),
        new("AL1", "CEDAR", "EURUSD", "BUY", "100", "5", null),
        new("AL2", "CEDAR", "EURUSD", "BUY", "100", "5", null),
        new("DUP-1", "ZETA", "EQ-9", "SELL", "10", "1", "10.00"),
        new("DUP-1", "ZETA", "EQ-9", "SELL", "10", "1", "10.00"),
    ];

    private static readonly Trade[] PathsRight =
    [
        new("R-PKG", "DELTA", "SWAP-1", "PAY_FIXED", "1000000", "3.5", "19837.10", "U-PKG"),
        new("B1", "GAMMA", "EQ-3", "BUY", "1000", "10.00", "10000.00"),
        new("R-NEW", "ACME", "EQ-4", "BUY", "2000", "102.604", "205208.00", "U-LIFE", "NEW"),
        new("R-CANC", "ACME", "EQ-4", "BUY", "2000", "102.604", "205208.00", "U-LIFE", "CANC", "R-NEW"),
        new("R-CORR", "ACME", "EQ-4", "BUY", "2000", "105.149", "210298.00", "U-LIFE", "CORR", "R-CANC"),
        new("AR1", "CEDAR", "EURUSD", "BUY", "100", "5", null),
    ];

    private static SourceMap<Trade> PathsMap() =>
        new SourceMap<Trade>()
            .Text("Id", trade => trade.Id)
            .Text("Counterparty", trade => trade.Counterparty)
            .Text("Instrument", trade => trade.Instrument)
            .Text("Side", trade => trade.Side)
            .Text("TradeDate", _ => "2026-11-02")
            .Text("Currency", _ => "USD")
            .Text("Quantity", trade => trade.Quantity)
            .Text("Price", trade => trade.Price)
            .Text("NetCash", trade => trade.NetCash)
            .Text("Uti", trade => trade.Uti)
            .Text("EventAction", trade => trade.EventAction)
            .Text("EventTarget", trade => trade.EventTarget)
            .Text("PackageRef", trade => trade.PackageRef)
            .Text("LegRole", trade => trade.LegRole);

    private const string PathsNormalization = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" },
            { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
            { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" },
            { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
            { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" },
            { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
            { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "Price", "to": "Price" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ]
        }
        """;

    private static string PathsProfile => $$"""
        {
          "formatVersion": 1,
          "profileId": "order-independence",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Uti", "type": "Identifier", "role": "Identity" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": { "left": {{PathsNormalization}}, "right": {{PathsNormalization}} },
          "consolidation": {
            "left": { "packages": [ {
              "link": "PackageRef", "role": "LegRole", "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
              "aggregate": { "Quantity": "Same", "Price": "FromRole(LEG_1_PAY)", "Side": "FromRole(LEG_1_PAY)", "NetCash": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)" }
            } ] },
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] },
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
            { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
          ] } ]
        }
        """;

    private static SourceMap<IReadOnlyDictionary<string, object?>> FixtureMap() =>
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

    private static ReferenceDataSet FixtureReference()
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx")) is false)
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private const string FixtureProfile = """
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
