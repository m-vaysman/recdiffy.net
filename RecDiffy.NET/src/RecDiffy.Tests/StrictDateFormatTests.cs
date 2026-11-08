using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class StrictDateFormatTests
{
    private static readonly DateOnly June11 = new(2025, 6, 11);

    [Fact]
    public void Date_parse_is_registered_to_the_culture_rule() =>
        Assert.IsType<CultureDateRule>(Rule("en-US", true, "dd/MM/yyyy"));

    // Strict: "/" and ":" mean exactly those characters, whatever the culture's own separators are.
    [Theory]
    [InlineData("", "11/06/2025")]
    [InlineData("en-US", "11/06/2025")]
    [InlineData("de-DE", "11/06/2025")]
    [InlineData("sv-SE", "11/06/2025")]
    public void A_strict_format_reads_the_characters_it_shows(string culture, string raw) =>
        Assert.Equal(June11, Read(Rule(culture, true, "dd/MM/yyyy"), raw));

    [Theory]
    [InlineData("de-DE", "11.06.2025")]
    [InlineData("sv-SE", "11-06-2025")]
    public void A_strict_format_rejects_the_cultures_own_separator(string culture, string raw)
    {
        var row = Apply(Rule(culture, true, "dd/MM/yyyy"), raw);

        Assert.True(row.Values["Day"].IsNull);
        Assert.Equal(("DateParse", DiagnosticSeverity.Invalid), (Assert.Single(row.Diagnostics).Code, Assert.Single(row.Diagnostics).Severity));
    }

    // Relaxed is the .NET behaviour: "/" also matches the culture's date separator.
    [Theory]
    [InlineData("de-DE", "11.06.2025")]
    [InlineData("de-DE", "11/06/2025")]
    [InlineData("sv-SE", "11-06-2025")]
    public void A_relaxed_format_also_reads_the_cultures_own_separator(string culture, string raw) =>
        Assert.Equal(June11, Read(Rule(culture, false, "dd/MM/yyyy"), raw));

    [Theory]
    [InlineData("dd'/'MM'/'yyyy")]
    [InlineData("dd\\/MM\\/yyyy")]
    public void A_quoted_or_escaped_slash_is_unchanged(string format) =>
        Assert.Equal(June11, Read(Rule("de-DE", true, format), "11/06/2025"));

    [Theory]
    [InlineData("de-DE", true)]
    [InlineData("sv-SE", true)]
    [InlineData("en-US", false)]
    public void Month_names_are_the_cultures(string culture, bool reads)
    {
        var row = Apply(Rule(culture, true, "dd-MMMM-yyyy"), "11-Juni-2025");

        Assert.Equal(reads, row.Values["Day"] == CanonicalCell.FromDate(June11));
    }

    [Fact]
    public void A_side_setting_beats_the_run_setting()
    {
        var profile = CultureProfile.Compile(
            CultureProfile.Json(run: "\"culture\": \"de-DE\", \"strictDateFormats\": false,", left: ", \"strictDateFormats\": true"),
            CultureInfo.InvariantCulture);

        var result = CultureProfile.Run(profile, ("1", "1", "11.06.2025"), ("1", "1", "11.06.2025"));

        var left = Assert.Single(result.Rows, row => row.Row.Source == "left");
        var right = Assert.Single(result.Rows, row => row.Row.Source == "right");
        Assert.Contains(left.Diagnostics, diagnostic => diagnostic.Code == "DateParse");
        Assert.Equal(CanonicalCell.FromDate(June11).Date, result.Canonical(right.Row)["Day"].Date);
    }

    private static INormalizationRule Rule(string culture, bool strict, string format) =>
        CultureProfile.Rules(culture, strict).Build(
            "date.parse",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Raw", ["to"] = "Day" },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["formats"] = [format] });

    private static DateOnly? Read(INormalizationRule rule, string raw) =>
        Apply(rule, raw).Values["Day"] is { IsNull: false } cell ? cell.Date : null;

    private static RowContext Apply(INormalizationRule rule, string raw)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Raw"] = raw });
        rule.Apply(ref row);
        return row;
    }
}
