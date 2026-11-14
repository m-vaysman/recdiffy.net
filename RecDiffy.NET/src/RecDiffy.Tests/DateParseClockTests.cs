using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// The proof test from #225, from an adversarial pass on dev. Spec §10.1: "No decision reads RowKey, row order, ordinals,
// string.GetHashCode, dictionary order or the clock." Spec §3.3: "formats are never guessed." A date that carries no
// year has no year to read, so the row is Invalid (DateParse), or Compile rejects the format. The year never comes from
// the machine's clock.
public class DateParseClockTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "date-parse-clock",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Settle", row => row.Settle);

    // The format has a day and a month. The text has no year.
    [Fact]
    public void A_format_with_no_year_does_not_take_the_year_from_the_clock() =>
        AssertNoDate("\"formats\": [\"dd/MM\"]", "11/06");

    // No formats are declared, and the text has no year.
    [Fact]
    public void A_date_with_no_year_and_no_formats_does_not_take_the_year_from_the_clock() =>
        AssertNoDate("\"formats\": []", "11/06");

    // No formats are declared. "11 June" has a day and a month, and no year.
    [Fact]
    public void A_day_and_a_month_with_no_formats_are_not_read_as_a_month_and_a_year() =>
        AssertNoDate("\"formats\": []", "11 June");

    private static void AssertNoDate(string formats, string text)
    {
        var changed = Profile.Replace("\"formats\": [\"yyyy-MM-dd\"]", formats, StringComparison.Ordinal);
        Assert.False(string.Equals(Profile, changed, StringComparison.Ordinal));

        var read = Read(changed, text);

        Assert.True(
            read is null,
            string.Create(CultureInfo.InvariantCulture, $"'{text}' was read as {read}. The clock says {DateTime.Today:yyyy-MM-dd}."));
    }

    // Null when Compile rejects the profile or every row is quarantined with DateParse. Otherwise what the run gave.
    private static string? Read(string profile, string text)
    {
        CompiledProfile compiled;
        try
        {
            compiled = Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore());
        }
        catch (ProfileCompileException)
        {
            return null;
        }

        var result = Reconciler.Run(
            compiled,
            RecordSources.FromObjects("left", [new Row("L1", text)], Map),
            RecordSources.FromObjects("right", [new Row("R1", text)], Map),
            ReferenceDataSet.Empty);
        var rejected = result.Rows.All(row =>
            row.Disposition == Disposition.Quarantined && row.Diagnostics.Any(diagnostic => string.Equals(diagnostic.Code, "DateParse", StringComparison.Ordinal)));

        return rejected ? null : string.Join("; ", result.Entries.Select(entry => $"{entry.Alignment}/{entry.Outcome} SettleDate {Date(result, entry)}"));
    }

    private static string Date(IReconciliationResult result, ResultEntry entry)
    {
        var value = result.Projection(entry.Id, Side.Left)["SettleDate"];
        return value.IsNull ? "null" : value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public sealed record Row(string Id, string Settle);
}
