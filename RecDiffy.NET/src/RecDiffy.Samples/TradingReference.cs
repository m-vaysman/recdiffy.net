using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Samples;

public static class TradingReference
{
    public static bool Run()
    {
        SampleReport.Heading("5. Reference data and the trading pack");
        var profile = Reconciler.Compile(
            ProfileDocument.Parse(Profile),
            TradingRulePack.AddTo(RuleRegistry.CreateCore()));
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", Book, BookMap()),
            RecordSources.FromObjects("street", Street, StreetMap()),
            Parties);
        SampleReport.Result(result);
        Canonical(result);
        return result.Entries is [{ Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact }]
            && result.Rows.All(row => CanonicalOf(result, row) is { Counterparty: "BAML", Side: "BUY", Quantity: 3000000m, Price: 96.078125m, TradeDate: "2025-06-12" });
    }

    private sealed record BookTrade(string Id, string CounterpartyName, string AssetClass, string Direction, string Quantity, string Price, string TradeDate);

    private sealed record StreetTrade(string Id, string CounterpartyLei, string AssetClass, string Direction, string Quantity, string ExecPrice, string TradeDate);

    private sealed record CanonicalTrade(
        string Counterparty,
        string Side,
        decimal Quantity,
        CanonicalValue QuantityValue,
        decimal Price,
        CanonicalValue PriceValue,
        string TradeDate);

    private static void Canonical(ReconciliationResult result)
    {
        foreach (var row in result.Rows)
        {
            var values = CanonicalOf(result, row);
            Console.WriteLine($"canonical {row.Row.Source}:{row.Row.RowKey} counterparty={values.Counterparty} side={values.Side} quantity={result.Format(values.QuantityValue)} price={result.Format(values.PriceValue)} tradeDate={values.TradeDate} rawPrice={result.Raw(row.Row, "ExecPrice") ?? result.Raw(row.Row, "Price")}");
        }
    }

    private static CanonicalTrade CanonicalOf(ReconciliationResult result, RowResult row)
    {
        var canonical = result.Canonical(row.Row);
        return new CanonicalTrade(
            canonical.Text("Counterparty"),
            canonical.Text("Side"),
            canonical["Quantity"].Number,
            canonical["Quantity"],
            canonical["Price"].Number,
            canonical["Price"],
            canonical.Text("TradeDate"));
    }

    private static SourceMap<BookTrade> BookMap() =>
        new SourceMap<BookTrade>()
            .Text("Id", row => row.Id)
            .Text("CounterpartyName", row => row.CounterpartyName)
            .Text("AssetClass", row => row.AssetClass)
            .Text("Direction", row => row.Direction)
            .Text("Quantity", row => row.Quantity)
            .Text("Price", row => row.Price)
            .Text("TradeDate", row => row.TradeDate);

    private static SourceMap<StreetTrade> StreetMap() =>
        new SourceMap<StreetTrade>()
            .Text("Id", row => row.Id)
            .Text("CounterpartyLei", row => row.CounterpartyLei)
            .Text("AssetClass", row => row.AssetClass)
            .Text("Direction", row => row.Direction)
            .Text("Quantity", row => row.Quantity)
            .Text("ExecPrice", row => row.ExecPrice)
            .Text("TradeDate", row => row.TradeDate);

    private static readonly BookTrade[] Book = [new("BOOK", "BOFA_MLPF&S", "Bond", "BUY", "3000000", "96.078125", "12/06/2025")];

    private static readonly StreetTrade[] Street = [new("STREET", "EL495T1V227XABYD2Q18", "Bond", "B", "3000000", "96-02+", "2025-06-12")];

    private static readonly ReferenceDataSet Parties = ReferenceDataSet.Create(
        "sample-parties",
        new DateOnly(2025, 6, 11),
        "sample-parties",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["EL495T1V227XABYD2Q18"] = "BAML" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["BOFA_MLPF&S"] = "BAML" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["BAML"] = "dd/MM/yyyy" });

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "trading-reference",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "CounterpartyName", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "AssetClass" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formatBy": "Counterparty" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Direction", "quantity": "Quantity", "asset": "AssetClass" },
                { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "party.resolveLei", "from": "CounterpartyLei", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "AssetClass" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "price.from32nds", "from": "ExecPrice", "to": "Price" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Direction", "quantity": "Quantity", "asset": "AssetClass" },
                { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" }
              ]
            }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "natural", "pass": "match.naturalKey", "key": ["Counterparty", "AssetClass", "Side", "TradeDate", "Quantity"] }
          ] } ]
        }
        """;
}
