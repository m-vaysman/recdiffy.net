using System.Diagnostics;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ViewTests
{
    [Fact]
    public void Splits_a_quantity_delta_into_left_only_right_only_and_matched()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", new[] { Row("SHARED", "10"), Row("LEFT", "15") }, Map()),
            RecordSources.FromObjects("book", new[] { Row("SHARED", "3"), Row("RIGHT", "4") }, Map()),
            ReferenceDataSet.Empty);
        var view = result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Quantity")], null));
        var cell = Assert.Single(Assert.Single(view.Lines).Cells);

        Assert.Equal("Quantity", cell.Key);
        Assert.Equal(25m, cell.Value.Left);
        Assert.Equal(7m, cell.Value.Right);
        Assert.Equal(-18m, cell.Value.Delta);
        Assert.Equal(-15m, cell.Value.LeftOnly);
        Assert.Equal(4m, cell.Value.RightOnly);
        Assert.Equal(-7m, cell.Value.MatchedDelta);
    }

    [Fact]
    public void Cancels_before_returning_a_result()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", new[] { Row("L1", "1") }, Map()),
            RecordSources.FromObjects("book", new[] { Row("R1", "1") }, Map()),
            ReferenceDataSet.Empty,
            cancellationToken: source.Token));
    }

    [Fact]
    public void Emits_a_recdiffy_span()
    {
        var names = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "RecDiffy",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => names.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        _ = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", new[] { Row("L1", "1") }, Map()),
            RecordSources.FromObjects("book", new[] { Row("R1", "1") }, Map()),
            ReferenceDataSet.Empty);

        Assert.Contains("RecDiffy.Run", names, StringComparer.Ordinal);
    }

    [Fact]
    public void Stays_the_same_at_one_and_eight_workers()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var left = RecordSources.FromObjects("book", new[] { Row("A", "1"), Row("B", "2") }, Map());
        var right = RecordSources.FromObjects("book", new[] { Row("A", "1"), Row("B", "2") }, Map());
        var serial = Reconciler.Run(profile, left, right, ReferenceDataSet.Empty, new RunOptions(1));
        var parallel = Reconciler.Run(profile, left, right, ReferenceDataSet.Empty, new RunOptions(8));

        Assert.Equal(serial.Entries.Select(entry => entry.Id.Value), parallel.Entries.Select(entry => entry.Id.Value), StringComparer.Ordinal);
        Assert.Equal(serial.Summary, parallel.Summary);
    }

    private static Dictionary<string, string> Row(string id, string quantity) => new(StringComparer.Ordinal)
    {
        ["Id"] = id,
        ["Counterparty"] = "CITI",
        ["Instrument"] = id,
        ["TradeDate"] = "2025-06-12",
        ["Side"] = "BUY",
        ["Quantity"] = quantity,
        ["Price"] = "1",
        ["NetCash"] = quantity,
    };

    private static SourceMap<Dictionary<string, string>> Map() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Counterparty", row => row["Counterparty"])
            .Text("Instrument", row => row["Instrument"])
            .Text("TradeDate", row => row["TradeDate"])
            .Text("Side", row => row["Side"])
            .Text("Quantity", row => row["Quantity"])
            .Text("Price", row => row["Price"])
            .Text("NetCash", row => row["NetCash"]);

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
