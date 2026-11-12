using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class MatchKeyBucketIsolationTests
{
    private static readonly IReadOnlyDictionary<string, FieldDeclaration> Schema =
        new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal)
        {
            ["Ref"] = new("Ref", FieldType.Identifier, FieldRole.Key, 0),
        };

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("Quantity", row => row.Quantity);

    [Theory]
    [InlineData("isin", "cusip")]
    [InlineData("shared", "broker")]
    public void Compose_separates_identifier_schemes_that_share_a_value_text(string leftScheme, string rightScheme)
    {
        var left = Item("L1", leftScheme, "US00000001");
        var right = Item("R1", rightScheme, "US00000001");

        Assert.Equal(MatchKeys.ValueText(left.Row.Context, Schema["Ref"]), MatchKeys.ValueText(right.Row.Context, Schema["Ref"]));
        Assert.NotEqual(MatchKeys.Compose(left, ["Ref"], Schema), MatchKeys.Compose(right, ["Ref"], Schema));
    }

    [Fact]
    public void Compose_with_shared_only_rejects_a_broker_identifier_and_keeps_a_shared_scheme()
    {
        var shared = Item("L1", "isin", "US00000001");
        var broker = Item("R1", "broker", "US00000001");

        Assert.NotNull(MatchKeys.Compose(shared, ["Ref"], Schema, sharedOnly: true));
        Assert.Null(MatchKeys.Compose(broker, ["Ref"], Schema, sharedOnly: true));
    }

    [Fact]
    public void PairIndex_keeps_isin_and_cusip_in_separate_buckets_while_same_scheme_matches()
    {
        var crossScheme = Run(
            """
            {
              "formatVersion": 1,
              "profileId": "pair-cross-scheme",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Ref", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "cusip" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
            }
            """,
            [new("L1", "X-1", "100")],
            [new("R1", "X-1", "100")]);

        var sameScheme = Run(
            """
            {
              "formatVersion": 1,
              "profileId": "pair-same-scheme",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Ref", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
            }
            """,
            [new("L1", "X-1", "100")],
            [new("R1", "X-1", "100")]);

        Assert.DoesNotContain(crossScheme.Rows, row => row.Disposition is Disposition.Matched);
        Assert.All(sameScheme.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    [Fact]
    public void GroupIndex_keeps_isin_and_cusip_blocks_apart_while_same_scheme_commits()
    {
        var crossScheme = Run(
            """
            {
              "formatVersion": 1,
              "profileId": "group-cross-scheme",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Ref", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "cusip" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.groupSum", "block": ["Ref"], "conserve": ["Quantity"], "many": "left" } ] } ]
            }
            """,
            [new("L1", "X-1", "50"), new("L2", "X-1", "50")],
            [new("R1", "X-1", "100")]);

        var sameScheme = Run(
            """
            {
              "formatVersion": 1,
              "profileId": "group-same-scheme",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Ref", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.groupSum", "block": ["Ref"], "conserve": ["Quantity"], "many": "left" } ] } ]
            }
            """,
            [new("L1", "X-1", "50"), new("L2", "X-1", "50")],
            [new("R1", "X-1", "100")]);

        Assert.DoesNotContain(crossScheme.Rows, row => row.Disposition is Disposition.Matched);
        Assert.All(sameScheme.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    // Today symbol.map can intern "" so two rows keyed on an empty mapped symbol match without throwing; the R1 follow-up
    // issue will rule on whether an empty mapped symbol should be treated as missing and repin this case.
    [Fact]
    public void Empty_mapped_symbol_key_runs_without_throwing_and_matches_as_today()
    {
        var result = Run(
            """
            {
              "formatVersion": 1,
              "profileId": "empty-symbol-key",
              "version": "1.0.0",
              "culture": "",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Ref", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref", "map": { "NA": "" } } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref", "map": { "NA": "" } } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
            }
            """,
            [new("L1", "NA", "100")],
            [new("R1", "NA", "100")]);

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    private static ActiveItem Item(string id, string scheme, string value)
    {
        var row = RowContext.Create(id, Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (pool, interned) = row.Pool.InternIdentifier(scheme, "", value);
        row = row.WithPool(pool).Write("Ref", CanonicalCell.FromInterned(FieldType.Identifier, interned), "test", null, value);
        return new ActiveItem(0, Side.Left, new ConsolidatedRow([new SourceRef("left", id, 0)], row, false, null, null));
    }

    private static ReconciliationResult Run(string profile, Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private sealed record Row(string Id, string Ref, string Quantity);
}
