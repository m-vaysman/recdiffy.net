using RecDiffy.Results;

namespace RecDiffy.Tests;

// #40: where key text normalization stops. Outer white space is removed before a key is built, by the source map and
// again by symbol.map and identifier.map, and .NET counts a no-break space as white space. After that, keys compare
// ordinally (spec §7, "Strings"): case, inner spaces, a zero-width space and the composed or decomposed spelling of an
// accent all keep two keys apart.
public class KeyTextBoundaryTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "key-text-boundaries",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Account", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "Account", "to": "Account", "scheme": "account" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "Account", "to": "Account", "scheme": "account" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "keys", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Account"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Counterparty", row => row.Counterparty)
        .Text("Account", row => row.Account)
        .Text("Quantity", row => row.Quantity);

    [Theory]
    [InlineData("  ACME\t", true)]
    [InlineData(" ACME ", true)]
    [InlineData("acme", false)]
    [InlineData("AC ME", false)]
    [InlineData("ACME​", false)]
    public void A_symbol_key_drops_outer_white_space_and_keeps_everything_else(string written, bool matched)
    {
        var result = Run(new("L", "ACME", "A-1", "5"), new("R", written, "A-1", "5"));

        Assert.Equal(matched, result.Entries.Any(entry => entry.Alignment == Alignment.Matched));
    }

    [Theory]
    [InlineData("  A-1\r\n", true)]
    [InlineData("a-1", false)]
    [InlineData("A -1", false)]
    public void An_identifier_key_drops_outer_white_space_and_keeps_everything_else(string written, bool matched)
    {
        var result = Run(new("L", "ACME", "A-1", "5"), new("R", "ACME", written, "5"));

        Assert.Equal(matched, result.Entries.Any(entry => entry.Alignment == Alignment.Matched));
    }

    // "é" written as one code point (U+00E9) and as "e" with a combining acute (U+0301) look the same and are not the
    // same key: nothing normalizes Unicode.
    [Fact]
    public void A_composed_and_a_decomposed_accent_are_different_keys()
    {
        var result = Run(new("L", "Société", "A-1", "5"), new("R", "Société", "A-1", "5"));

        Assert.DoesNotContain(result.Entries, entry => entry.Alignment == Alignment.Matched);
    }

    private static ReconciliationResult Run(Row left, Row right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [left], Map),
            RecordSources.FromObjects("right", [right], Map),
            ReferenceDataSet.Empty);

    private sealed record Row(string Id, string Counterparty, string Account, string Quantity);
}
