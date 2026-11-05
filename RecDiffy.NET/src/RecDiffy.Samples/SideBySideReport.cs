using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class SideBySideReport
{
    public static bool Run()
    {
        SampleReport.Heading("3. Side-by-side");
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", [Row("SHARED", "10"), Row("LEFT", "15")], Map()),
            RecordSources.FromObjects("book", [Row("SHARED", "3"), Row("RIGHT", "4")], Map()),
            ReferenceDataSet.Empty);
        var view = result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Quantity")], null));
        SampleReport.Result(result);
        SampleReport.SideBySide(view);
        return view.Lines is [{ Cells: var cells }]
            && cells.TryGetValue("Quantity", out var cell)
            && cell is { Left: 25m, Right: 7m, Delta: -18m, LeftOnly: -15m, RightOnly: 4m, MatchedDelta: -7m };
    }

    private sealed record Position(string Id, string Counterparty, string Instrument, string TradeDate, string Side, string Quantity, string Price, string NetCash);

    private static Position Row(string id, string quantity) =>
        new(id, "CITI", id, "2025-06-12", "BUY", quantity, "1", quantity);

    private static SourceMap<Position> Map() =>
        new SourceMap<Position>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", row => row.Counterparty)
            .Text("Instrument", row => row.Instrument)
            .Text("TradeDate", row => row.TradeDate)
            .Text("Side", row => row.Side)
            .Text("Quantity", row => row.Quantity)
            .Text("Price", row => row.Price)
            .Text("NetCash", row => row.NetCash);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "sample",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [
              { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
              { "rule": "symbol.map", "from": "Side", "to": "Side" }
            ], "parse": [
              { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
              { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
              { "rule": "decimal.parse", "from": "Price", "to": "Price" },
              { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
            ] },
            "right": { "identify": [
              { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
              { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
              { "rule": "symbol.map", "from": "Side", "to": "Side" }
            ], "parse": [
              { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
              { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
              { "rule": "decimal.parse", "from": "Price", "to": "Price" },
              { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
            ] }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
          ] } ]
        }
        """;
}
