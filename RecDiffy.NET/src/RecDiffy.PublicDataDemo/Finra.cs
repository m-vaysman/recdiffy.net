using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Net;
using RecDiffy.Extensions;
using RecDiffy.Results;

namespace RecDiffy.PublicDataDemo;

/// <summary>One symbol's line in FINRA's consolidated NMS short-sale volume file.</summary>
public sealed record ConsolidatedLine(string Symbol, decimal Short, decimal ShortExempt, decimal Total, string Markets);

/// <summary>One symbol's line in one trade reporting facility's short-sale volume file.</summary>
public sealed record FacilityLine(string Facility, string Symbol, decimal Short, decimal ShortExempt, decimal Total);

/// <summary>One trading day: the consolidated file, the facility files, and how many files' trailer counts matched their rows.</summary>
internal sealed record FinraDay(DateOnly Date, ImmutableArray<ConsolidatedLine> Consolidated, ImmutableArray<FacilityLine> Facilities, int Files, int TrailersAgreeing);

/// <summary>One parsed file: its data rows and the record count on its last line, when it has one.</summary>
internal sealed record FinraFile(ImmutableArray<string[]> Rows, int? Trailer);

/// <summary>The newest FINRA day and its result, which the FINRA tab shows in full.</summary>
internal sealed record FinraLatest(FinraDay Day, RecResult<ConsolidatedLine, FacilityLine> Result);

/// <summary>One day's run, kept for the history grid after the day's result is dropped.</summary>
internal sealed record FinraRun(DateOnly Date, int Symbols, int FacilityRows, RunSummary Summary, TimeSpan Elapsed);

/// <summary>
/// FINRA's Reg SHO daily short-sale volume: the consolidated NMS file should equal, symbol by symbol, the sum of the
/// trade reporting facility files. FORF (OTC) is not part of the consolidated file, so it is not read.
/// </summary>
internal static class Finra
{
    private const string Cdn = "https://cdn.finra.org/equity/regsho/daily/";

    private static readonly (string Code, string Name)[] FacilityFiles =
    [
        ("FNSQ", "Nasdaq TRF Carteret"),
        ("FNQC", "Nasdaq TRF Chicago"),
        ("FNYX", "NYSE TRF"),
        ("FNRA", "ADF"),
    ];

    public const string Code = """
        var result = RecBuilder.Between(consolidated, facilities)
            .MatchOn(c => c.Symbol, f => f.Symbol)
            .Sum(c => c.Short, f => f.Short)
            .Sum(c => c.ShortExempt, f => f.ShortExempt)
            .Sum(c => c.Total, f => f.Total)
            .Run();

        DataTable status = result.ToStatusCountsTable();
        DataTable match = result.ToMatchTable();
        DataTable groups = result.ToSummaryTable(c => $"{c.Symbol} {c.Total:#,0.######}", f => $"{f.Facility} {f.Total:#,0.######}");
        string html = status.ToDataGridHtml();
        """;

    public static RecResult<ConsolidatedLine, FacilityLine> Reconcile(FinraDay day) =>
        RecBuilder.Between(day.Consolidated, day.Facilities)
            .MatchOn(c => c.Symbol, f => f.Symbol)
            .Sum(c => c.Short, f => f.Short)
            .Sum(c => c.ShortExempt, f => f.ShortExempt)
            .Sum(c => c.Total, f => f.Total)
            .Run();

    /// <summary>
    /// The day's files, or <see langword="null"/> when the day is not complete: FINRA published nothing (a weekend or holiday),
    /// or the consolidated file is up and a facility file is not yet. A partial day would show breaks that are not there.
    /// </summary>
    public static async Task<FinraDay?> DownloadAsync(HttpClient http, DateOnly date, CancellationToken cancellationToken)
    {
        var consolidated = await FetchAsync(http, "CNMS", date, cancellationToken).ConfigureAwait(false);
        if (consolidated is null)
            return null;

        var facilities = await Task.WhenAll(FacilityFiles.Select(async file =>
            (file.Name, File: await FetchAsync(http, file.Code, date, cancellationToken).ConfigureAwait(false)))).ConfigureAwait(false);
        if (facilities.Any(facility => facility.File is null))
            return null;
        var files = facilities.Select(facility => (facility.Name, File: facility.File!)).ToArray();

        return new FinraDay(
            date,
            [.. consolidated.Rows.Select(row => new ConsolidatedLine(row[1], Number(row[2]), Number(row[3]), Number(row[4]), row[5]))],
            [.. files.SelectMany(facility => facility.File.Rows.Select(row => new FacilityLine(facility.Name, row[1], Number(row[2]), Number(row[3]), Number(row[4]))))],
            files.Length + 1,
            files.Select(facility => facility.File).Prepend(consolidated).Count(file => file.Trailer == file.Rows.Length));
    }

    /// <summary>Parses one file: a header, pipe-delimited rows, and a last line that holds the number of rows.</summary>
    public static FinraFile Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = lines.Skip(1).Where(line => line.Contains('|', StringComparison.Ordinal)).Select(line => line.Split('|')).ToImmutableArray();
        var trailer = lines is [_, .., var last] && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : (int?)null;
        return new FinraFile(rows, trailer);
    }

    public static string Tab(FinraDay latest, RecResult<ConsolidatedLine, FacilityLine> result, IReadOnlyList<FinraRun> history)
    {
        var run = history[0];
        using var strip = Grids.Strip(
        [
            ("Trade date", latest.Date),
            ("Consolidated rows", latest.Consolidated.Length),
            ("Facility rows", latest.Facilities.Length),
            ("Trailer counts", $"{latest.TrailersAgreeing.ToInvariant()} of {latest.Files.ToInvariant()} files agree"),
            ("Run time, incl. start-up", Seconds(run.Elapsed)),
            ("Profile hash", Short(result.Result.Run.ProfileHash)),
            ("Engine", result.Result.Run.EngineVersion),
        ]);
        using var counts = result.ToStatusCountsTable();
        using var match = result.ToMatchTable();
        using var exceptions = Grids.Where(match, "Status", status => status is not "Exact");
        using var summary = result.ToSummaryTable(
            c => $"{c.Symbol} {c.Total.ToInvariant(Volume)}",
            f => $"{f.Facility} {f.Total.ToInvariant(Volume)}");
        var busiest = latest.Consolidated.OrderByDescending(line => line.Total).Take(10).Select(line => line.Symbol).ToArray();
        using var top = Grids.Pick(summary, "Left", busiest, text => text.Split(' ')[0]);
        using var days = Grids.Build(
            "History",
            [("Trade date", typeof(DateOnly)), ("Symbols", typeof(int)), ("Facility rows", typeof(int)), ("Exact", typeof(int)), ("Break", typeof(int)), ("Ambiguous", typeof(int)), ("Left only", typeof(int)), ("Right only", typeof(int)), ("Run time", typeof(string))],
            history.Select(day => (object?[])[day.Date, day.Symbols, day.FacilityRows, day.Summary.Exact, day.Summary.Break, day.Summary.Ambiguous, day.Summary.LeftOnly, day.Summary.RightOnly, Seconds(day.Elapsed)]));

        var ambiguousNote = result.Result.Summary.Ambiguous is 0
            ? ""
            : """
                <p class="note">Ambiguous: a symbol's facility rows don't add up to its consolidated row, so the engine refuses to force a
                match. The rows and their volumes are all listed; compare the Left and Right columns.</p>
                """;
        var exceptionHtml = exceptions.Rows.Count is 0
            ? "<p class=\"ok\">Every symbol ties: no breaks, nothing left over on either side.</p>"
            : ambiguousNote + Page.Grid(exceptions, "result.ToMatchTable(), the rows that are not Exact");

        return $"""
            <p class="lead">FINRA publishes each day's short-sale volume once per facility: the Nasdaq TRF in Carteret and in Chicago, the NYSE TRF
            and the ADF. It also publishes it once consolidated. Each symbol's consolidated row should equal the sum of its facility rows;
            the separate OTC (ORF) file is not part of it. Results for {latest.Date.ToInvariant("yyyy-MM-dd")}:</p>
            <h3>Status</h3>
            {Page.Grid(counts, "result.ToStatusCountsTable()")}
            <h3>Exceptions</h3>
            {exceptionHtml}
            <h3>The ten busiest symbols</h3>
            <p class="note">Total volume. Left is the consolidated row; Right is the facility rows that add up to it.</p>
            {Page.Grid(top, "result.ToSummaryTable(…), the ten busiest symbols")}
            <h3>The last 30 days</h3>
            {Page.Grid(days)}
            <h3>The code</h3>
            {Page.Code(Code)}
            <h3>Run details</h3>
            {Page.Grid(strip)}
            <p class="source">Source: <a href="https://www.finra.org/finra-data/browse-catalog/short-sale-volume-data/daily-short-sale-volume-files">FINRA daily short sale volume files</a>.</p>
            """;
    }

    /// <summary>Volumes at the files' own scale: fractional shares to six places, with thousands separators.</summary>
    private const string Volume = "#,0.######";

    public static string Seconds(TimeSpan elapsed) => elapsed.TotalSeconds switch
    {
        < 1 => $"{elapsed.TotalMilliseconds.ToInvariant("0")} ms",
        var seconds => $"{seconds.ToInvariant("0.0")} s",
    };

    public static string Short(string hash) => hash.Length > 12 ? hash[..12] : hash;

    private static decimal Number(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static async Task<FinraFile?> FetchAsync(HttpClient http, string code, DateOnly date, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri($"{Cdn}{code}shvol{date.ToInvariant("yyyyMMdd")}.txt"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }
}
