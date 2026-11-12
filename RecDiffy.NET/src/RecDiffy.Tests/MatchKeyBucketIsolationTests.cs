using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class MatchKeyBucketIsolationTests
{
    private const string NaturalKey = """
        {
          "formatVersion": 1,
          "profileId": "match-key-scheme-split",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "isin" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "cusip" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private const string GroupSum = """
        {
          "formatVersion": 1,
          "profileId": "match-key-group-split",
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
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "block": ["Ref"], "conserve": ["Quantity"], "many": "left" }
          ] } ]
        }
        """;

    private const string EmptySymbol = """
        {
          "formatVersion": 1,
          "profileId": "match-key-empty-symbol",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Symbol", "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref", "map": { "NA": "" } } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref", "map": { "NA": "" } } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    [Fact]
    public void The_same_identifier_value_under_isin_and_cusip_shares_value_text_and_not_the_composed_key()
    {
        var isin = Item("isin", "X");
        var cusip = Item("cusip", "X");

        Assert.Equal(Text(isin), Text(cusip));
        Assert.NotEqual(Compose(isin), Compose(cusip));
    }

    [Fact]
    public void The_same_identifier_value_under_a_shared_scheme_and_broker_shares_value_text_and_not_the_composed_key()
    {
        var shared = Item("shared", "X");
        var broker = Item("broker", "X");

        Assert.Equal(Text(shared), Text(broker));
        Assert.NotEqual(Compose(shared), Compose(broker));
        Assert.Null(Compose(broker, sharedOnly: true));
    }

    [Fact]
    public void A_natural_key_does_not_pair_the_same_value_read_as_isin_with_the_same_value_read_as_cusip()
    {
        var split = Run(NaturalKey, [("L1", "X")], [("R1", "X")]);
        Assert.DoesNotContain(split.Rows, row => row.Disposition is Disposition.Matched);

        var control = Run(SameScheme(NaturalKey), [("L1", "X")], [("R1", "X")]);
        Assert.Equal(2, control.Rows.Count);
        Assert.All(control.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    [Fact]
    public void A_group_sum_does_not_commit_the_same_value_across_isin_and_cusip_blocks()
    {
        (string Id, string Ref, string Quantity)[] left = [("L1", "X", "50.00"), ("L2", "X", "50.00")];
        (string Id, string Ref, string Quantity)[] right = [("R1", "X", "100.00")];

        var split = Run(GroupSum, left, right);
        Assert.DoesNotContain(split.Rows, row => row.Disposition is Disposition.Matched);

        var control = Run(SameScheme(GroupSum), left, right);
        Assert.Equal(3, control.Rows.Count);
        Assert.All(control.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    // The R1 follow-up issue will rule on whether two rows keyed by an empty mapped symbol should match, and will repin this.
    [Fact]
    public void An_empty_mapped_symbol_matches_and_does_not_throw()
    {
        var result = Run(EmptySymbol, [("L1", "NA")], [("R1", "NA")]);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
    }

    private static string? Text(ActiveItem item) =>
        MatchKeys.ValueText(item.Row.Context, Schema()["Ref"]);

    private static MatchKey? Compose(ActiveItem item, bool sharedOnly = false) =>
        MatchKeys.Compose(item, ["Ref"], Schema(), sharedOnly);

    private static ActiveItem Item(string scheme, string value)
    {
        var row = RowContext.Create("row", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (pool, id) = row.Pool.InternIdentifier(scheme, "", value);
        row = row.WithPool(pool).Write("Ref", CanonicalCell.FromInterned(FieldType.Identifier, id), "test", null, value);
        return new ActiveItem(0, Side.Left, new ConsolidatedRow([new SourceRef("left", "row", 0)], row, false, null, null));
    }

    private static Dictionary<string, FieldDeclaration> Schema() =>
        new(StringComparer.Ordinal)
        {
            ["Ref"] = new("Ref", FieldType.Identifier, FieldRole.Key, 0),
        };

    private static string SameScheme(string profile) =>
        profile.Replace("\"scheme\": \"cusip\"", "\"scheme\": \"isin\"", StringComparison.Ordinal);

    private static ReconciliationResult Run(string profile, (string Id, string Ref)[] left, (string Id, string Ref)[] right)
    {
        var map = new SourceMap<(string Id, string Ref)>()
            .Text("Id", row => row.Id)
            .Text("Ref", row => row.Ref);
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);
    }

    private static ReconciliationResult Run(string profile, (string Id, string Ref, string Quantity)[] left, (string Id, string Ref, string Quantity)[] right)
    {
        var map = new SourceMap<(string Id, string Ref, string Quantity)>()
            .Text("Id", row => row.Id)
            .Text("Ref", row => row.Ref)
            .Text("Quantity", row => row.Quantity);
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);
    }
}
