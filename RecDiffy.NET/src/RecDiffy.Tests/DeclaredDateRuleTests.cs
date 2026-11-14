using System.Collections.Immutable;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// date.parse after #225: text is read only with a format that writes a year, so the year never comes from the clock.
public class DeclaredDateRuleTests
{
    private static readonly DateOnly June11 = new(2025, 6, 11);

    private const string ByParty = """
        {
          "formatVersion": 1,
          "profileId": "declared-date-by-party",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Party", "type": "Symbol", "role": "Key" },
            { "name": "Day", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Party", "to": "Party" } ],
              "parse": [ { "rule": "date.parse", "from": "Day", "to": "Day", "formatBy": "Party" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Party", "to": "Party" } ],
              "parse": [ { "rule": "date.parse", "from": "Day", "to": "Day", "formatBy": "Party" } ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Party", row => row.Party).Text("Day", row => row.Day);

    [Fact]
    public void Date_parse_is_registered_to_the_declared_rule() =>
        Assert.IsType<PartyFormatDateRule>(Rule("en-US", ["dd/MM/yyyy"]));

    [Theory]
    [InlineData("dd/MM")]
    [InlineData("MM-dd")]
    [InlineData("d MMMM")]
    [InlineData("HH:mm")]
    [InlineData("dd/MM 'yyyy'")]
    [InlineData("dd/MM \\y")]
    [InlineData("M")]
    [InlineData("t")]
    public void Compile_rejects_a_declared_format_with_no_year(string format)
    {
        var json = CultureProfile.Json(formats: Quoted(format));

        var failure = Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()));

        Assert.Contains(new ProfileError("InvalidParameter", $"date.parse format '{format}' has no year, so the year would come from the clock."), failure.Errors);
    }

    [Theory]
    [InlineData("dd/MM/yyyy")]
    [InlineData("yy-MM-dd")]
    [InlineData("'on' d MMMM yyyy")]
    [InlineData("d")]
    [InlineData("D")]
    [InlineData("y")]
    public void A_declared_format_that_writes_a_year_compiles(string format) =>
        Assert.NotNull(Reconciler.Compile(ProfileDocument.Parse(CultureProfile.Json(formats: Quoted(format))), RuleRegistry.CreateCore()));

    [Fact]
    public void A_format_with_a_year_still_reads_its_text() =>
        Assert.Equal(June11, Read(Apply(Rule("", ["dd/MM/yyyy"]), "11/06/2025")));

    [Theory]
    [InlineData("2025-06-11")]
    [InlineData("11/06")]
    [InlineData("11 June")]
    public void Text_with_no_declared_format_is_invalid(string text)
    {
        var row = Apply(Rule("", []), text);

        Assert.True(row.Values["Day"].IsNull);
        Assert.Equal(("DateParse", DiagnosticSeverity.Invalid), (Assert.Single(row.Diagnostics).Code, Assert.Single(row.Diagnostics).Severity));
    }

    [Fact]
    public void A_typed_date_reads_with_no_declared_format()
    {
        object?[] typed = [June11, new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Unspecified), new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Unspecified).ToOADate()];

        var read = typed.Select(value => Read(Apply(Rule("", []), value)));

        Assert.All(read, date => Assert.Equal(June11, date));
    }

    [Fact]
    public void A_blank_cell_is_unknown_with_no_diagnostic()
    {
        var row = Apply(Rule("", []), "");

        Assert.True(row.Values["Day"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_reference_data_format_with_no_year_makes_the_row_invalid()
    {
        var result = Run(new Row("L1", "ACME", "11/06"), new Row("R1", "ACME", "11/06"));

        Assert.All(result.Rows, row =>
        {
            Assert.Equal(Disposition.Quarantined, row.Disposition);
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic.Code == "DateParse");
        });
    }

    [Fact]
    public void A_reference_data_format_with_a_year_reads_its_text()
    {
        var result = Run(new Row("L1", "BETA", "11/06/2025"), new Row("R1", "BETA", "11/06/2025"));

        Assert.All(result.Rows, row => Assert.Equal(June11, result.Canonical(row.Row)["Day"].Date));
        Assert.Equal(FieldOutcome.Exact, Assert.Single(result.Entries).Outcome);
    }

    private static string Quoted(string format) => System.Text.Json.JsonSerializer.Serialize(format);

    private static INormalizationRule Rule(string culture, IReadOnlyList<string> formats) =>
        CultureProfile.Rules(culture).Build(
            "date.parse",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Raw", ["to"] = "Day" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["formats"] = formats });

    private static DateOnly? Read(RowContext row) =>
        row.Values["Day"] is { IsNull: false } cell ? cell.Date : null;

    private static RowContext Apply(INormalizationRule rule, object? raw)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Raw"] = raw });
        rule.Apply(ref row);
        return row;
    }

    private static IReconciliationResult Run(Row left, Row right)
    {
        var reference = ReferenceDataSet.Create(
            "dates",
            new DateOnly(2025, 1, 1),
            "dates",
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ACME"] = "dd/MM", ["BETA"] = "dd/MM/yyyy" });
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(ByParty), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [left], Map),
            RecordSources.FromObjects("right", [right], Map),
            reference);
    }

    public sealed record Row(string Id, string Party, string Day);
}
