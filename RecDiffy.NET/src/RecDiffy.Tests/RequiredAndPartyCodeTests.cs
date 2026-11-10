using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// #148: the codes required.present, party.resolveLei and party.resolveAlias write. Each rule is built the way compile
// builds it (CultureProfile.SideRules) and applied to one row; the last two tests run a profile.
public class RequiredAndPartyCodeTests
{
    private static readonly ReferenceDataSet Reference = ReferenceDataSet.Create(
        "parties",
        new DateOnly(2026, 11, 10),
        "parties-v1",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["5493001KJTIIGC8Y1R12"] = "GSCO" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Goldman Sachs"] = "GSCO" },
        new Dictionary<string, string>(StringComparer.Ordinal));

    private static readonly SideRules Rules = CultureProfile.Rules("en-US");

    [Fact]
    public void Required_present_names_each_missing_field_with_RequiredMissing()
    {
        var rule = Required("Quantity", "Day");

        var row = Apply(rule, Row(new Dictionary<string, object?>(StringComparer.Ordinal)));

        Diagnostic[] expected =
        [
            new("RequiredMissing", DiagnosticSeverity.Invalid, "required.present", "Quantity", null),
            new("RequiredMissing", DiagnosticSeverity.Invalid, "required.present", "Day", null),
        ];
        Assert.Equal(expected, row.Diagnostics);
    }

    [Fact]
    public void Required_present_counts_a_null_cell_as_missing()
    {
        var rule = Required("Quantity", "Day");
        var written = Row(new Dictionary<string, object?>(StringComparer.Ordinal))
            .Write("Quantity", CanonicalCell.Null(FieldType.Decimal), "decimal.parse", "", null)
            .Write("Day", CanonicalCell.FromDate(new DateOnly(2026, 11, 10)), "date.parse", "10/11/2026", "2026-11-10");

        var row = Apply(rule, written);

        Assert.Equal(new Diagnostic("RequiredMissing", DiagnosticSeverity.Invalid, "required.present", "Quantity", null), Assert.Single(row.Diagnostics));
    }

    [Fact]
    public void Required_present_writes_nothing_when_every_field_has_a_value()
    {
        var rule = Required("Quantity", "Day");
        var written = Row(new Dictionary<string, object?>(StringComparer.Ordinal))
            .Write("Quantity", CanonicalCell.FromDecimal(0m), "decimal.parse", "0", "0")
            .Write("Day", CanonicalCell.FromDate(new DateOnly(2026, 11, 10)), "date.parse", "10/11/2026", "2026-11-10");

        var row = Apply(rule, written);

        Assert.Empty(row.Diagnostics);
        Assert.Equal(written.Values, row.Values);
    }

    [Theory]
    [InlineData("party.resolveLei", "LEI", "549300UNKNOWNLEI0000")]
    [InlineData("party.resolveAlias", "Name", "Golden Sacks")]
    public void An_unknown_party_is_Invalid_with_UnknownParty_and_its_raw_text(string ruleId, string column, string raw)
    {
        var rule = Party(ruleId, column);

        var row = Apply(rule, Row(new Dictionary<string, object?>(StringComparer.Ordinal) { [column] = raw }));

        Assert.Equal(new Diagnostic("UnknownParty", DiagnosticSeverity.Invalid, ruleId, "Counterparty", raw), Assert.Single(row.Diagnostics));
        Assert.True(row.IsNull("Counterparty"));
    }

    [Theory]
    [InlineData("party.resolveLei", "LEI", "5493001KJTIIGC8Y1R12")]
    [InlineData("party.resolveLei", "LEI", "  5493001KJTIIGC8Y1R12 ")]
    [InlineData("party.resolveAlias", "Name", "Goldman Sachs")]
    public void A_known_party_writes_the_party_code(string ruleId, string column, string raw)
    {
        var rule = Party(ruleId, column);

        var row = Apply(rule, Row(new Dictionary<string, object?>(StringComparer.Ordinal) { [column] = raw }));

        Assert.Empty(row.Diagnostics);
        Assert.Equal("GSCO", row.Display("Counterparty"));
    }

    [Theory]
    [InlineData("party.resolveLei", "LEI", "")]
    [InlineData("party.resolveLei", "LEI", "   ")]
    [InlineData("party.resolveLei", "LEI", null)]
    [InlineData("party.resolveAlias", "Name", "")]
    [InlineData("party.resolveAlias", "Name", null)]
    public void A_blank_party_writes_no_code(string ruleId, string column, string? raw)
    {
        var rule = Party(ruleId, column);

        var row = Apply(rule, Row(new Dictionary<string, object?>(StringComparer.Ordinal) { [column] = raw }));

        Assert.Empty(row.Diagnostics);
        Assert.True(row.IsNull("Counterparty"));
    }

    // party.resolveAlias fills the field only when an earlier rule left it empty, so it never reports a name it did not look up.
    [Fact]
    public void An_alias_is_not_looked_up_once_the_lei_resolved_the_party()
    {
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal) { ["LEI"] = "5493001KJTIIGC8Y1R12", ["Name"] = "Golden Sacks" };

        var row = Apply(Party("party.resolveAlias", "Name"), Apply(Party("party.resolveLei", "LEI"), Row(raw)));

        Assert.Empty(row.Diagnostics);
        Assert.Equal("GSCO", row.Display("Counterparty"));
    }

    [Fact]
    public void A_row_missing_a_required_field_is_quarantined_with_RequiredMissing()
    {
        var result = Run(RequiredProfile, ("T1", "", "Goldman Sachs"), ("T1", "5", "Goldman Sachs"));

        var left = Assert.Single(result.Rows, row => row.Row.Source == "left");
        Assert.Equal(Disposition.Quarantined, left.Disposition);
        Assert.Contains(left.Diagnostics, diagnostic => diagnostic is { Code: "RequiredMissing", Severity: DiagnosticSeverity.Invalid, RuleId: "required.present", Field: "Quantity" });
    }

    [Fact]
    public void A_row_with_an_unknown_party_is_quarantined_with_UnknownParty()
    {
        var result = Run(PartyProfile, ("T1", "5", "Golden Sacks"), ("T1", "5", "Goldman Sachs"));

        var left = Assert.Single(result.Rows, row => row.Row.Source == "left");
        Assert.Equal(Disposition.Quarantined, left.Disposition);
        Assert.Contains(left.Diagnostics, diagnostic => diagnostic is { Code: "UnknownParty", Severity: DiagnosticSeverity.Invalid, RuleId: "party.resolveAlias", Field: "Counterparty", RawText: "Golden Sacks" });
        Assert.Equal(Disposition.RightOnly, Assert.Single(result.Rows, row => row.Row.Source == "right").Disposition);
    }

    private static INormalizationRule Required(params string[] fields) =>
        Rules.Build(
            "required.present",
            ImmutableDictionary<string, string>.Empty,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["fields"] = fields });

    private static INormalizationRule Party(string ruleId, string from) =>
        Rules.Build(ruleId, new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = from, ["to"] = "Counterparty" });

    private static RowContext Row(IReadOnlyDictionary<string, object?> raw) =>
        RowContext.Create("T1", Side.Left, raw) with { Reference = Reference };

    private static RowContext Apply(INormalizationRule rule, RowContext row)
    {
        var next = row;
        rule.Apply(ref next);
        return next;
    }

    private static ReconciliationResult Run(string json, (string Id, string Qty, string Name) left, (string Id, string Qty, string Name) right)
    {
        var profile = ProfileCompiler.Compile(ProfileDocument.Parse(json), TradingRulePack.AddTo(RuleRegistry.CreateCore()), CultureInfo.InvariantCulture).Profile
            ?? throw new InvalidOperationException("The profile did not compile.");
        var map = new SourceMap<(string Id, string Qty, string Name)>()
            .Text("Id", row => row.Id)
            .Text("Qty", row => row.Qty)
            .Text("Name", row => row.Name);
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [left], map),
            RecordSources.FromObjects("right", [right], map),
            Reference);
    }

    private const string RequiredProfile = """
        {
          "formatVersion": 1,
          "profileId": "required-missing",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ],
              "validate": [ { "rule": "required.present", "fields": [ "Quantity" ] } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ],
              "validate": [ { "rule": "required.present", "fields": [ "Quantity" ] } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Id" ] } ] } ]
        }
        """;

    private const string PartyProfile = """
        {
          "formatVersion": 1,
          "profileId": "unknown-party",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" }, { "rule": "party.resolveAlias", "from": "Name", "to": "Counterparty" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" }, { "rule": "party.resolveAlias", "from": "Name", "to": "Counterparty" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Id", "Counterparty" ] } ] } ]
        }
        """;
}
