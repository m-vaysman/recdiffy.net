using System.Data;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class DeclaredCultureTests
{
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    [Fact]
    public void A_side_culture_beats_the_run_culture_which_beats_the_server()
    {
        var declared = CultureProfile.Compile(CultureProfile.Json(run: "\"culture\": \"en-US\",", left: ", \"culture\": \"de-DE\""), Swedish);
        var undeclared = CultureProfile.Compile(CultureProfile.Json(), Swedish);

        Assert.Equal(new SideReading("de-DE", ",", ".", CultureOrigin.Side, true), declared.Reading["left"]);
        Assert.Equal(new SideReading("en-US", ".", ",", CultureOrigin.Run, true), declared.Reading["right"]);
        Assert.Equal((Swedish.Name, CultureOrigin.Server), (undeclared.Reading["left"].Culture, undeclared.Reading["left"].Origin));
        Assert.Equal((Swedish.Name, CultureOrigin.Server), (undeclared.Reading["right"].Culture, undeclared.Reading["right"].Origin));
    }

    [Fact]
    public void Each_file_is_read_with_its_own_culture_and_the_values_compare_exactly()
    {
        var profile = CultureProfile.Compile(CultureProfile.Json(left: ", \"culture\": \"de-DE\"", right: ", \"culture\": \"en-US\""), Swedish);

        var result = CultureProfile.Run(profile, ("1", "1.234,56", "11/06/2025"), ("1", "1,234.56", "11/06/2025"));

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (entry.Alignment, entry.Outcome));
    }

    [Fact]
    public void An_undeclared_culture_is_the_servers_culture_read_once_at_compile()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var profile = Reconciler.Compile(ProfileDocument.Parse(CultureProfile.Json()), RuleRegistry.CreateCore());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            var result = CultureProfile.Run(profile, ("1", "1,5", "11/06/2025"), ("1", "1,5", "11/06/2025"));

            Assert.Equal(("de-DE", CultureOrigin.Server), (result.Run.Reading["left"].Culture, result.Run.Reading["left"].Origin));
            var left = Assert.Single(result.Rows, row => row.Row.Source == "left");
            Assert.Equal(1.5m, result.Canonical(left.Row)["Quantity"].Number);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_result_records_each_sides_reading()
    {
        var profile = CultureProfile.Compile(CultureProfile.Json(run: "\"culture\": \"de-DE\", \"strictDateFormats\": false,", right: ", \"strictDateFormats\": true"), Swedish);

        var result = CultureProfile.Run(profile, ("1", "1,5", "11/06/2025"), ("1", "1,5", "11/06/2025"));

        Assert.Equal(new SideReading("de-DE", ",", ".", CultureOrigin.Run, false), result.Run.Reading["left"]);
        Assert.Equal(new SideReading("de-DE", ",", ".", CultureOrigin.Run, true), result.Run.Reading["right"]);
    }

    [Fact]
    public void An_unknown_culture_fails_compile()
    {
        var failure = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse(CultureProfile.Json(left: ", \"culture\": \"xx-QQ\"")), RuleRegistry.CreateCore()));

        Assert.Contains(failure.Errors, error => error is { Code: "UnknownCulture" } && error.Message.Contains("sources.left.culture", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"culture\": 5,")]
    [InlineData("\"strictDateFormats\": \"no\",")]
    public void A_declaration_of_the_wrong_type_is_rejected(string run)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => ProfileDocument.Parse(CultureProfile.Json(run: run)));

        Assert.Equal("InvalidParameter", Assert.Single(failure.Errors).Code);
    }

    [Fact]
    public void Declaring_a_reading_changes_the_hash_and_invariant_differs_from_undeclared()
    {
        string[] hashes =
        [
            CultureProfile.Compile(CultureProfile.Json(), Swedish).Hash,
            CultureProfile.Compile(CultureProfile.Json(run: "\"culture\": \"\","), Swedish).Hash,
            CultureProfile.Compile(CultureProfile.Json(run: "\"culture\": \"en-US\","), Swedish).Hash,
            CultureProfile.Compile(CultureProfile.Json(left: ", \"culture\": \"en-US\""), Swedish).Hash,
            CultureProfile.Compile(CultureProfile.Json(run: "\"strictDateFormats\": false,"), Swedish).Hash,
        ];

        Assert.Equal(hashes.Length, hashes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(hashes[0], CultureProfile.Compile(CultureProfile.Json(), CultureInfo.GetCultureInfo("de-DE")).Hash);
    }

    [Fact]
    public void A_reading_round_trips_through_profile_json()
    {
        var document = ProfileDocument.Parse(CultureProfile.Json(
            run: "\"culture\": \"de-DE\", \"strictDateFormats\": false,",
            left: ", \"culture\": \"sv-SE\"",
            right: ", \"strictDateFormats\": true"));

        var json = document.ToJson();
        var again = ProfileDocument.Parse(json);

        Assert.Equal(json, again.ToJson());
        Assert.Equal(("de-DE", (bool?)false), (again.Culture, again.StrictDateFormats));
        Assert.Equal(new SourceReading("sv-SE", null), again.SourceReadings["left"]);
        Assert.Equal(new SourceReading(null, true), again.SourceReadings["right"]);
    }

    [Fact]
    public void An_undeclared_reading_is_not_written()
    {
        var json = ProfileDocument.Parse(CultureProfile.Json()).ToJson();

        Assert.DoesNotContain("\"culture\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"strictDateFormats\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_declares_the_run_and_each_side()
    {
        var built = ProfileBuilder.From(ProfileDocument.Parse(CultureProfile.Json()))
            .Culture(CultureInfo.GetCultureInfo("de-DE"))
            .Culture(Side.Left, CultureInfo.GetCultureInfo("sv-SE"))
            .StrictDateFormats(false)
            .StrictDateFormats(Side.Right, true)
            .Build();

        Assert.True(built.Succeeded);
        Assert.Equal(("de-DE", (bool?)false), (built.Document.Culture, built.Document.StrictDateFormats));
        Assert.Equal(new SourceReading("sv-SE", null), built.Document.SourceReadings["left"]);
        Assert.Equal(new SourceReading(null, true), built.Document.SourceReadings["right"]);
        Assert.Equal(built.Document.ToJson(), ProfileBuilder.From(built.Document).Build().Document?.ToJson());
    }

    // Typed values from a reader never pass through the server's culture: a short is a decimal, and a key is invariant text.
    [Fact]
    public void Intake_does_not_depend_on_the_servers_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Swedish;
            using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
            table.Columns.Add("Id", typeof(decimal));
            table.Columns.Add("Qty", typeof(short));
            table.Columns.Add("Day", typeof(string));
#pragma warning restore IDISP004
            table.Rows.Add(1.5m, (short)-5, "11/06/2025");

            var result = RunLeft(table);

            var row = Assert.Single(result.Rows);
            Assert.Equal(("1.5", InputValidity.Valid), (row.Row.RowKey, row.Validity));
            Assert.Equal(-5m, result.Canonical(row.Row)["Quantity"].Number);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_double_from_a_data_reader_is_Invalid_not_converted()
    {
        using var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Qty", typeof(double));
        table.Columns.Add("Day", typeof(string));
#pragma warning restore IDISP004
        table.Rows.Add("1", 2.5d, "11/06/2025");

        var row = Assert.Single(RunLeft(table).Rows);

        Assert.Equal(InputValidity.Invalid, row.Validity);
        Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "FloatValue", RawText: "2.5" });
    }

    private static IReconciliationResult RunLeft(DataTable table) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(CultureProfile.Json(run: "\"culture\": \"en-US\",")), RuleRegistry.CreateCore()),
            RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()),
            RecordSources.FromObjects("right", Array.Empty<string>(), new SourceMap<string>()),
            ReferenceDataSet.Empty);
}
