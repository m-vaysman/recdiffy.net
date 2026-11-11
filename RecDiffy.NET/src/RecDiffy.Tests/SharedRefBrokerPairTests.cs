using RecDiffy.Results;

namespace RecDiffy.Tests;

// #283, found by #40. #24 asked for two things: keep the scheme in identifier keys, and make the shared-reference pass
// ignore broker identities, because P1 keys on the shared scheme only (spec §2.3, §4.4; #116 item 9). The first is
// done. The second is not: P1 keys on Counterparty and TradeRef with the veto off, so two rows whose broker references
// have the same text share a P1 key and commit there. Broker references come from each side's own numbering, so equal
// text is a coincidence, and the veto already treats a broker reference as a wildcard.
public class SharedRefBrokerPairTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "shared-ref-broker-pair",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeRef", "type": "Identifier", "role": "Identity" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "broker" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "broker" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.sharedRef" },
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "Quantity"] }
          ] } ]
        }
        """;

    private static readonly SourceMap<Trade> Map = new SourceMap<Trade>()
        .Text("Id", trade => trade.Id)
        .Text("Counterparty", trade => trade.Counterparty)
        .Text("Instrument", trade => trade.Instrument)
        .Text("TradeRef", trade => trade.TradeRef)
        .Text("Quantity", trade => trade.Quantity);

    // Each side numbered its own trades B-7 and B-9, crosswise. P2 pairs the trades whose economics agree. Today P1 pairs
    // them by the broker text first, so both pairs commit on P1 and break on quantity.
    [Fact(Skip = "#283: match.sharedRef pairs two broker references that have the same text.")]
    public void Broker_references_with_the_same_text_do_not_pair_on_the_shared_reference_pass()
    {
        var result = Run(
            [new("L1", "ACME", "BOND1", "B-7", "100"), new("L2", "ACME", "BOND1", "B-9", "200")],
            [new("R1", "ACME", "BOND1", "B-7", "200"), new("R2", "ACME", "BOND1", "B-9", "100")]);

        Assert.Equal(
            ["left:L1|right:R2 P2 Exact", "left:L2|right:R1 P2 Exact"],
            result.Entries.Select(entry => $"{Members(entry)} {entry.Explanation.CommittedBy} {entry.Outcome}").Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static ReconciliationResult Run(Trade[] left, Trade[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static string Members(ResultEntry entry) =>
        string.Join("|", entry.Left.Concat(entry.Right).Select(member => $"{member.Source}:{member.RowKey}").Order(StringComparer.Ordinal));

    private sealed record Trade(string Id, string Counterparty, string Instrument, string TradeRef, string Quantity);
}
