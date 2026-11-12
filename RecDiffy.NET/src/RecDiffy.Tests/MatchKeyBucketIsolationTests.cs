using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #350 phase 2: a bucket key is a MatchKey, and a MatchKey carries the identifier scheme. A value that reads the same on
// both sides but arrives under a different scheme lands in a different bucket, at Compose, in PairIndex and in GroupIndex.
public class MatchKeyBucketIsolationTests
{
    private const string PairProfile = """
        {
          "formatVersion": 1,
          "profileId": "match-key-bucket-pair",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "cusip" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "pairs", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Ref"] }
          ] } ]
        }
        """;

    private const string GroupProfile = """
        {
          "formatVersion": 1,
          "profileId": "match-key-bucket-group",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "cusip" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "groups", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "block": ["Ref"], "conserve": ["Quantity"], "many": "left" }
          ] } ]
        }
        """;

    private const string EmptySymbolProfile = """
        {
          "formatVersion": 1,
          "profileId": "match-key-bucket-empty-symbol",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref", "map": { "NA": "" } } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref", "map": { "NA": "" } } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "pairs", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Ref"] }
          ] } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Ref, string Quantity)> Rows = new SourceMap<(string Id, string Ref, string Quantity)>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("Quantity", row => row.Quantity);

    [Fact]
    public void Compose_separates_one_value_under_two_identifier_schemes_while_its_value_text_stays_equal()
    {
        var schema = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal)
        {
            ["Ref"] = new("Ref", FieldType.Identifier, FieldRole.Key, 0),
        };
        var isin = Item(schema, "isin", "X");
        var cusip = Item(schema, "cusip", "X");

        Assert.Equal(MatchKeys.ValueText(isin.Row.Context, schema["Ref"]), MatchKeys.ValueText(cusip.Row.Context, schema["Ref"]));
        Assert.NotEqual(MatchKeys.Compose(isin, ["Ref"], schema), MatchKeys.Compose(cusip, ["Ref"], schema));
        Assert.Equal(MatchKeys.Compose(isin, ["Ref"], schema), MatchKeys.Compose(isin, ["Ref"], schema));
    }

    [Fact]
    public void Compose_shared_only_refuses_a_broker_scheme()
    {
        var schema = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal)
        {
            ["Ref"] = new("Ref", FieldType.Identifier, FieldRole.Key, 0),
        };
        var shared = Item(schema, "isin", "X");
        var broker = Item(schema, "broker", "X");

        Assert.Equal(MatchKeys.ValueText(shared.Row.Context, schema["Ref"]), MatchKeys.ValueText(broker.Row.Context, schema["Ref"]));
        Assert.NotEqual(MatchKeys.Compose(shared, ["Ref"], schema), MatchKeys.Compose(broker, ["Ref"], schema));
        Assert.NotNull(MatchKeys.Compose(shared, ["Ref"], schema, sharedOnly: true));
        Assert.Null(MatchKeys.Compose(broker, ["Ref"], schema, sharedOnly: true));
    }

    [Fact]
    public void A_pair_pass_keeps_two_schemes_in_separate_buckets_and_pairs_one_scheme()
    {
        var split = Run(PairProfile, "cusip", [("L1", "X", "100.00")], [("R1", "X", "100.00")]);
        Assert.All(split.Rows, row => Assert.NotEqual(Disposition.Matched, row.Disposition));

        var paired = Run(PairProfile, "isin", [("L1", "X", "100.00")], [("R1", "X", "100.00")]);
        Assert.All(paired.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    [Fact]
    public void A_group_pass_keeps_two_schemes_in_separate_blocks_and_sums_one_scheme()
    {
        var split = Run(GroupProfile, "cusip", [("L1", "X", "50.00"), ("L2", "X", "50.00")], [("R1", "X", "100.00")]);
        Assert.All(split.Rows, row => Assert.NotEqual(Disposition.Matched, row.Disposition));

        var summed = Run(GroupProfile, "isin", [("L1", "X", "50.00"), ("L2", "X", "50.00")], [("R1", "X", "100.00")]);
        Assert.All(summed.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.Equal(3, summed.Rows.Count);
    }

    // R1 leaves an empty mapped symbol matching an empty mapped symbol. Phase 2 pins today's behaviour and must not throw;
    // the R1 follow-up issue rules on whether a blank key field should match, and repins this test if it should not.
    [Fact]
    public void An_empty_mapped_symbol_is_a_key_that_still_matches_and_never_throws()
    {
        var result = Run(EmptySymbolProfile, "isin", [("L1", "NA", "100.00")], [("R1", "NA", "100.00")]);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    // The right side of each split profile reads under "cusip". Naming the left scheme instead gives the same-scheme control.
    private static ReconciliationResult Run(string template, string rightScheme, (string Id, string Ref, string Quantity)[] left, (string Id, string Ref, string Quantity)[] right)
    {
        var json = template.Replace("\"scheme\": \"cusip\"", $"\"scheme\": \"{rightScheme}\"", StringComparison.Ordinal);
        var profile = Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore());
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", left, Rows),
            RecordSources.FromObjects("right", right, Rows),
            ReferenceDataSet.Empty);
    }

    private static ActiveItem Item(IReadOnlyDictionary<string, FieldDeclaration> schema, string scheme, string value)
    {
        var row = RowContext.Create("row", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", value);
        row = row.WithPool(pool).Write("Ref", CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, value);
        return new ActiveItem(0, Side.Left, new ConsolidatedRow([new SourceRef("left", "row", 0)], row, false, null, null));
    }
}
