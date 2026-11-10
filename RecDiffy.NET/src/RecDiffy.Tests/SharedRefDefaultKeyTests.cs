namespace RecDiffy.Tests;

public class SharedRefDefaultKeyTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "shared-ref-default-key",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [ { "id": "P0", "pass": "match.sharedRef" } ] } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Sku, string Quantity)> Map = new SourceMap<(string Id, string Sku, string Quantity)>()
        .Text("Id", row => row.Id).Text("Sku", row => row.Sku).Text("Quantity", row => row.Quantity);

    // Either outcome is a fix: compile rejects the default key it can't use, or the run goes ahead without it.
    [Fact(Skip = "🔴 #288: compile does not check the default key Counterparty, TradeRef, so MatchKeys.Compose throws at run time.")]
    public void A_shared_ref_pass_without_a_key_never_throws_KeyNotFoundException_at_run_time()
    {
        var thrown = Record.Exception(() => Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "SKU-1", "4")], Map),
            RecordSources.FromObjects("right", [("R1", "SKU-1", "4")], Map),
            ReferenceDataSet.Empty));

        Assert.True(thrown is null or ProfileCompileException, $"Expected a compile error or a run, got {thrown?.GetType().Name}: {thrown?.Message}");
    }
}
