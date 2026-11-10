using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class RegisteredAttributionTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "registered-attribution",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Net", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fee", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Gross", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Gross", "to": "Gross" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" }, { "rule": "decimal.parse", "from": "Net", "to": "Net" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Gross", "to": "Gross" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" }, { "rule": "decimal.parse", "from": "Net", "to": "Net" } ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Gross": { "tolerance": { "absolute": 1 } },
              "Fee": { "attributed": "test.feeShare" },
              "Net": { "tolerance": { "attributed": "test.netOfFee" } }
            } }
          } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Gross", row => row.Gross).Text("Fee", row => row.Fee).Text("Net", row => row.Net);

    private static readonly RuleRegistry Registry =
        RuleRegistry.CreateCore().RegisterAttribution(new FeeShareAttribution()).RegisterAttribution(new NetOfFeeAttribution());

    [Fact]
    public void A_profile_outside_trading_attributes_its_fields_through_attributions_it_registers() =>
        Assert.Equal(
            "Matched/WithinTolerance Fee:WithinTolerance:0.01:[Gross], Gross:WithinTolerance:1:[], Net:WithinTolerance:0.99:[Gross+Fee]",
            Describe(Run(new Row("L1", "1000", "10", "990"), new Row("R1", "1001", "10.01", "990.99"))));

    [Fact]
    public void A_derived_field_its_attribution_does_not_explain_is_a_break_in_its_own_right()
    {
        var net = Assert.Single(Assert.Single(Run(new Row("L1", "1000", "10", "990"), new Row("R1", "1001", "10.01", "991.50")).Entries).Diffs, diff => diff.Field.Name == "Net");

        Assert.Equal(FieldOutcome.Break, net.Outcome);
    }

    [Fact]
    public void The_attributions_are_unknown_to_a_registry_that_does_not_register_them() =>
        Assert.Equal(
            ["UnknownRule", "UnknownRule"],
            Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore())).Errors.Select(error => error.Code),
            StringComparer.Ordinal);

    private static ReconciliationResult Run(Row left, Row right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), Registry),
            RecordSources.FromObjects("left", [left], Map),
            RecordSources.FromObjects("right", [right], Map),
            ReferenceDataSet.Empty);

    private static string Describe(ReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry => $"{entry.Alignment}/{entry.Outcome} " + string.Join(", ", entry.Diffs
            .OrderBy(diff => diff.Field.Name, StringComparer.Ordinal)
            .Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}:[{string.Join("+", diff.AttributedTo)}]"))));

    public sealed record Row(string Id, string Gross, string Fee, string Net);

    private sealed class FeeShareAttribution : IAttribution
    {
        public string Name => "test.feeShare";

        public ImmutableArray<string> Roots { get; } = ["Gross"];

        public AttributionStage Stage => AttributionStage.Measured;

        public bool Explains(RowContext left, RowContext right, FieldDeclaration field, decimal delta, MidpointMode midpoint) =>
            Math.Round(left.Values[field.Name].Number * right.Values["Gross"].Number / left.Values["Gross"].Number, field.Scale) == right.Values[field.Name].Number;

        public FieldOutcome Outcome(RootOutcomes roots) => roots.Of("Gross");

        public ImmutableArray<string> AttributedTo(RootOutcomes roots) => ["Gross"];
    }

    private sealed class NetOfFeeAttribution : IAttribution
    {
        public string Name => "test.netOfFee";

        public ImmutableArray<string> Roots { get; } = ["Gross", "Fee"];

        public AttributionStage Stage => AttributionStage.Finished;

        public bool Explains(RowContext left, RowContext right, FieldDeclaration field, decimal delta, MidpointMode midpoint) =>
            right.Values["Gross"].Number - left.Values["Gross"].Number - (right.Values["Fee"].Number - left.Values["Fee"].Number) == delta;

        public FieldOutcome Outcome(RootOutcomes roots) =>
            Roots.Select(roots.Of).Contains(FieldOutcome.Break) ? FieldOutcome.Break : FieldOutcome.WithinTolerance;

        public ImmutableArray<string> AttributedTo(RootOutcomes roots) => [.. Roots.Where(roots.Moved)];
    }
}
