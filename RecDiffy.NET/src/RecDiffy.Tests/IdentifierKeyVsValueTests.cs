using RecDiffy.Results;

namespace RecDiffy.Tests;

public class IdentifierKeyVsValueTests
{
    private const string RouteProfile = """
        {
          "formatVersion": 1,
          "profileId": "identifier-route-value",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "TradeId", "type": "Identifier", "role": "Key" },
            { "name": "Desk", "type": "Identifier", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Id", "to": "TradeId", "scheme": "trade" },
                { "rule": "identifier.map", "from": "Desk", "to": "Desk", "scheme": "desk" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            },
            "right": {
              "identify": [
                { "rule": "identifier.map", "from": "Id", "to": "TradeId", "scheme": "trade" },
                { "rule": "identifier.map", "from": "Desk", "to": "Desk", "scheme": "desk" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            }
          },
          "paths": [
            {
              "id": "rates",
              "route": { "rule": "route.byField", "field": "Desk", "equals": "RATES" },
              "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["TradeId"] } ]
            }
          ]
        }
        """;

    private const string SharedRefProfile = """
        {
          "formatVersion": 1,
          "profileId": "identifier-shared-ref",
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
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [
            {
              "id": "trades",
              "route": { "rule": "route.all" },
              "passes": [
                { "id": "P1", "pass": "match.sharedRef" },
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "Quantity"] }
              ]
            }
          ]
        }
        """;

    private const string GroupContestProfile = """
        {
          "formatVersion": 1,
          "profileId": "identifier-group-contest-key",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Book", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [
            {
              "id": "allocs",
              "route": { "rule": "route.all" },
              "passes": [
                { "id": "P3", "pass": "match.groupSum", "block": ["Book"], "conserve": ["Quantity"], "many": "left" }
              ]
            }
          ]
        }
        """;

    private static readonly SourceMap<(string Id, string Desk, string Amount)> RouteMap = new SourceMap<(string Id, string Desk, string Amount)>()
        .Text("Id", row => row.Id)
        .Text("Desk", row => row.Desk)
        .Text("Amount", row => row.Amount);

    private static readonly SourceMap<(string Id, string Counterparty, string Instrument, string TradeRef, string Quantity)> TradeMap =
        new SourceMap<(string Id, string Counterparty, string Instrument, string TradeRef, string Quantity)>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", row => row.Counterparty)
            .Text("Instrument", row => row.Instrument)
            .Text("TradeRef", row => row.TradeRef)
            .Text("Quantity", row => row.Quantity);

    [Theory]
    [InlineData("book")]
    [InlineData("desk")]
    [InlineData("shared")]
    [InlineData("broker")]
    public void Route_by_field_on_an_identifier_matches_the_value_regardless_of_scheme(string scheme)
    {
        var json = RouteProfile.Replace("\"scheme\": \"desk\"", $"\"scheme\": \"{scheme}\"", StringComparison.Ordinal);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("T1", "RATES", "100.00")], RouteMap),
            RecordSources.FromObjects("right", [("T1", "RATES", "100.00")], RouteMap),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, "rates", FieldOutcome.Exact), (entry.Alignment, entry.PathId, entry.Outcome));
    }

    [Fact]
    public void Route_by_field_on_an_identifier_does_not_match_scheme_plus_value_or_a_blank_identifier()
    {
        var json = RouteProfile.Replace("\"equals\": \"RATES\"", "\"equals\": \"desk\\u001eRATES\"", StringComparison.Ordinal);
        var encodedRouteResult = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("T1", "RATES", "100.00")], RouteMap),
            RecordSources.FromObjects("right", [("T1", "RATES", "100.00")], RouteMap),
            ReferenceDataSet.Empty);

        Assert.Equal(2, encodedRouteResult.Entries.Count);
        Assert.All(encodedRouteResult.Entries, entry =>
        {
            Assert.Equal("", entry.PathId);
            Assert.Equal("Unrouted", Assert.Single(entry.Explanation.Trail).Detail);
        });

        var blankResult = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(RouteProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("T1", "", "100.00")], RouteMap),
            RecordSources.FromObjects("right", [("T1", "   ", "100.00")], RouteMap),
            ReferenceDataSet.Empty);

        Assert.Equal(2, blankResult.Entries.Count);
        Assert.All(blankResult.Entries, entry =>
        {
            Assert.Equal("", entry.PathId);
            Assert.Equal("Unrouted", Assert.Single(entry.Explanation.Trail).Detail);
        });
    }

    [Theory]
    [InlineData("shared", "P1", FieldOutcome.Break)]
    [InlineData("uti", "P1", FieldOutcome.Break)]
    [InlineData("broker", "P2", FieldOutcome.Exact)]
    public void Shared_ref_pass_pairs_non_broker_schemes_and_ignores_broker_scheme(string scheme, string expectedPass, FieldOutcome expectedOutcome)
    {
        var json = SharedRefProfile.Replace("\"scheme\": \"shared\"", $"\"scheme\": \"{scheme}\"", StringComparison.Ordinal);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "ACME", "BOND1", "REF-1", "100.00"), ("L2", "ACME", "BOND1", "REF-2", "200.00")], TradeMap),
            RecordSources.FromObjects("right", [("R1", "ACME", "BOND1", "REF-1", "200.00"), ("R2", "ACME", "BOND1", "REF-2", "100.00")], TradeMap),
            ReferenceDataSet.Empty);

        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal(Alignment.Matched, entry.Alignment);
            Assert.Equal(expectedPass, entry.Explanation.CommittedBy);
            Assert.Equal(expectedOutcome, entry.Outcome);
        });
    }

    [Fact]
    public void Group_sum_contest_records_the_contested_block_key()
    {
        var map = new SourceMap<(string Id, string Book, string Quantity)>()
            .Text("Id", row => row.Id)
            .Text("Book", row => row.Book)
            .Text("Quantity", row => row.Quantity);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(GroupContestProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "RATES", "50.00"), ("L2", "RATES", "50.00"), ("L3", "RATES", "50.00")], map),
            RecordSources.FromObjects("right", [("R1", "RATES", "100.00")], map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Ambiguous, entry.Alignment);
        var contest = Assert.Single(entry.Explanation.Trail);
        Assert.Equal(("P3", PassVerdict.Contest, "book\u001eRATES", 4), (contest.PassId, contest.Verdict, contest.Key, contest.Candidates.Count));
    }
}
