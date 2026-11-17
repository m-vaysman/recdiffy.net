using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using RecDiffy.Extensions;
using RecDiffy.Results;

namespace RecDiffy.PublicDataDemo;

/// <summary>One line of the Daily Treasury Statement's public debt transactions, in millions of dollars.</summary>
public sealed record DtsLine(DateOnly Date, string Type, string Market, string Security, string Description, decimal Amount)
{
    /// <summary>Issues add to the debt; redemptions take from it.</summary>
    public decimal Signed => Type is "Redemptions" ? -Amount : Amount;

    /// <summary>Government Account Series securities are held by government trust funds, which is our mapping to intragovernmental holdings.</summary>
    public bool Intragovernmental => Security is "Government Account Series";
}

/// <summary>A day of the Daily Treasury Statement: issues minus redemptions, in total and by our holder mapping, in millions.</summary>
public sealed record DtsDay(DateOnly Date, int Lines, decimal Net, decimal Intragovernmental, decimal Public);

/// <summary>The change in Debt to the Penny since the previous published day, in total and by holder, in millions.</summary>
public sealed record DebtChange(DateOnly Date, DateOnly Since, decimal Net, decimal Intragovernmental, decimal Public);

/// <summary>One day of Debt to the Penny, in dollars.</summary>
internal sealed record DebtDay(DateOnly Date, decimal Total, decimal Intragovernmental, decimal Public);

/// <summary>
/// US Treasury: each day's change in total public debt outstanding (Debt to the Penny) should equal that day's public debt
/// issues minus redemptions (the Daily Treasury Statement). The statement rounds each line to the nearest million.
/// </summary>
internal static class Treasury
{
    private const string Api = "https://api.fiscaldata.treasury.gov/services/api/fiscal_service/";

    /// <summary>Each of the statement's 23 lines is rounded to the nearest million, so a day's sum can be off by up to $11.5M.</summary>
    public const decimal Tolerance = 12m;

    public const string Code = """
        var result = RecBuilder.Between(statementDays, debtChanges)
            .MatchOn(d => d.Date, c => c.Date)
            .Compare(d => d.Net, c => c.Net, tolerance: 12m)
            .Compare(d => d.Intragovernmental, c => c.Intragovernmental, tolerance: 12m)
            .Compare(d => d.Public, c => c.Public, tolerance: 12m)
            .Run();

        DataTable status = result.ToStatusCountsTable();
        DataTable days = result.ToMatchTable();
        string html = days.ToDataGridHtml();
        """;

    public static RecResult<DtsDay, DebtChange> Reconcile(IEnumerable<DtsDay> days, IEnumerable<DebtChange> changes, decimal tolerance = Tolerance) =>
        RecBuilder.Between(days, changes)
            .MatchOn(d => d.Date, c => c.Date)
            .Compare(d => d.Net, c => c.Net, tolerance: tolerance)
            .Compare(d => d.Intragovernmental, c => c.Intragovernmental, tolerance: tolerance)
            .Compare(d => d.Public, c => c.Public, tolerance: tolerance)
            .Run();

    public static async Task<ImmutableArray<DtsLine>> StatementAsync(HttpClient http, DateOnly from, CancellationToken cancellationToken)
    {
        var rows = await GetAsync(http, $"v1/accounting/dts/public_debt_transactions?filter=record_date:gte:{from.ToInvariant("yyyy-MM-dd")}&page%5Bsize%5D=2000", cancellationToken).ConfigureAwait(false);
        return
        [
            .. rows
                .Where(row => decimal.TryParse(row.GetProperty("transaction_today_amt").GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                .Select(row => new DtsLine(
                    Date(row, "record_date"),
                    Text(row, "transaction_type"),
                    Text(row, "security_market"),
                    Text(row, "security_type"),
                    Text(row, "security_type_desc"),
                    Number(row, "transaction_today_amt"))),
        ];
    }

    public static async Task<ImmutableArray<DebtDay>> DebtAsync(HttpClient http, DateOnly from, CancellationToken cancellationToken)
    {
        var rows = await GetAsync(http, $"v2/accounting/od/debt_to_penny?filter=record_date:gte:{from.ToInvariant("yyyy-MM-dd")}&sort=record_date&page%5Bsize%5D=200", cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(row => new DebtDay(Date(row, "record_date"), Number(row, "tot_pub_debt_out_amt"), Number(row, "intragov_hold_amt"), Number(row, "debt_held_public_amt")))];
    }

    /// <summary>The statement's lines summed per day, from <paramref name="from"/> on.</summary>
    public static ImmutableArray<DtsDay> Days(IEnumerable<DtsLine> lines, DateOnly from) =>
    [
        .. lines
            .Where(line => line.Date >= from)
            .GroupBy(line => line.Date)
            .OrderBy(day => day.Key)
            .Select(day => new DtsDay(
                day.Key,
                day.Count(),
                day.Sum(line => line.Signed),
                day.Where(line => line.Intragovernmental).Sum(line => line.Signed),
                day.Where(line => !line.Intragovernmental).Sum(line => line.Signed))),
    ];

    /// <summary>Each published day's change from the day before it, in millions to the nearest $10,000, from <paramref name="from"/> on.</summary>
    public static ImmutableArray<DebtChange> Changes(IReadOnlyList<DebtDay> days, DateOnly from)
    {
        var ordered = days.OrderBy(day => day.Date).ToArray();
        return
        [
            .. ordered.Zip(ordered.Skip(1))
                .Where(pair => pair.Second.Date >= from)
                .Select(pair => new DebtChange(
                    pair.Second.Date,
                    pair.First.Date,
                    Millions(pair.Second.Total - pair.First.Total),
                    Millions(pair.Second.Intragovernmental - pair.First.Intragovernmental),
                    Millions(pair.Second.Public - pair.First.Public))),
        ];
    }

    public static string Tab(RecResult<DtsDay, DebtChange> result, IReadOnlyList<DtsLine> lines, DateOnly from, TimeSpan elapsed)
    {
        var latest = lines.Max(line => line.Date);
        var to = result.Matched.SelectMany(match => match.Right).Concat(result.RightOnly).Select(change => change.Date).Append(latest).Max();
        using var strip = Grids.Strip(
        [
            ("From", from),
            ("To", to),
            ("Statement days", result.Result.Summary.Matched + result.Result.Summary.LeftOnly),
            ("Tolerance", "$12M"),
            ("Run time", Finra.Seconds(elapsed)),
            ("Profile hash", Finra.Short(result.Result.Run.ProfileHash)),
            ("Engine", result.Result.Run.EngineVersion),
        ]);
        using var counts = result.ToStatusCountsTable();
        var fields = FieldOutcomes(result);
        using var byField = Grids.Build(
            "Fields",
            [("Field", typeof(string)), ("Exact", typeof(int)), ("Within tolerance", typeof(int)), ("Break", typeof(int))],
            fields.Select(field => (object?[])[field.Field, field.Exact, field.WithinTolerance, field.Break]));
        using var match = result.ToMatchTable();
        match.Columns["Left.Date"]!.SetOrdinal(0);
        match.Columns["Diff"]!.SetOrdinal(2);
        using var breakdown = Grids.Build(
            "Lines",
            [("Type", typeof(string)), ("Market", typeof(string)), ("Security", typeof(string)), ("Description", typeof(string)), ("$ millions", typeof(decimal))],
            lines.Where(line => line.Date == latest).Select(line => (object?[])[line.Type, line.Market, line.Security, line.Description is "null" ? null : line.Description, line.Amount]));

        var summary = result.Result.Summary;
        var totalTies = fields is [{ Field: "Net", Break: 0 }, ..];
        var splitBreaks = fields.Skip(1).Any(field => field.Break > 0);
        var reading = (summary.Matched, totalTies, splitBreaks) switch
        {
            (0, _, _) => "No statement day had a Debt to the Penny day to compare it with.",
            (_, true, true) => """
                The total ties on every compared day, within rounding. The split by holder does not: our mapping counts Government
                Account Series as intragovernmental and everything else as held by the public, and the two breaks offset each other.
                That is the signature of a classification difference, not missing money. A matched total can still hide breaks in its parts.
                """,
            (_, true, false) => "The total and the split by holder both tie on every compared day, within rounding.",
            _ => "The total breaks on at least one day beyond rounding: see the Diff column for the day and the amount.",
        };
        var unmatched = (summary.LeftOnly + summary.RightOnly) switch
        {
            0 => "",
            1 => " One day has no partner on the other side yet; it is listed as Left only or Right only.",
            var count => $" {count.ToInvariant()} days have no partner on the other side yet; they are listed as Left only or Right only.",
        };

        return $"""
            <p class="lead">Each business day's change in total public debt (Debt to the Penny) should equal that day's issues minus
            redemptions (the Daily Treasury Statement), within $12M of rounding. Matched day by day, compared in total and by holder:</p>
            <h3>Status</h3>
            {Page.Grid(counts, "result.ToStatusCountsTable()")}
            <h3>By field</h3>
            <p class="note">{reading}{unmatched}</p>
            {Page.Grid(byField)}
            <h3>Day by day, in $ millions</h3>
            <p class="note">Left is the Daily Treasury Statement: Lines is how many of its lines were summed. Right is Debt to the Penny:
            Since is the previous day it changed from.</p>
            {Page.Grid(match, "result.ToMatchTable()")}
            <h3>The {lines.Count(line => line.Date == latest).ToInvariant()} statement lines behind {latest.ToInvariant("yyyy-MM-dd")}</h3>
            {Page.Grid(breakdown)}
            <h3>The code</h3>
            {Page.Code(Code)}
            <h3>Run details</h3>
            {Page.Grid(strip)}
            <p class="source">Sources: <a href="https://fiscaldata.treasury.gov/datasets/debt-to-the-penny/">Debt to the Penny</a> and
            <a href="https://fiscaldata.treasury.gov/datasets/daily-treasury-statement/">Daily Treasury Statement</a>, Treasury's Fiscal Data API.</p>
            """;
    }

    /// <summary>For each compared field, how many matched days were exact, within tolerance or a break.</summary>
    public static ImmutableArray<(string Field, int Exact, int WithinTolerance, int Break)> FieldOutcomes(RecResult<DtsDay, DebtChange> result) =>
    [
        .. new[] { "Net", "Intragovernmental", "Public" }.Select(field =>
        {
            var outcomes = result.Matched
                .SelectMany(match => match.Diffs)
                .Where(diff => string.Equals(diff.Field.Name, field, StringComparison.Ordinal))
                .Select(diff => diff.Outcome)
                .ToArray();
            return (field, outcomes.Count(outcome => outcome is FieldOutcome.Exact), outcomes.Count(outcome => outcome is FieldOutcome.WithinTolerance), outcomes.Count(outcome => outcome is FieldOutcome.Break));
        }),
    ];

    private static decimal Millions(decimal dollars) => Math.Round(dollars / 1_000_000m, 2, MidpointRounding.AwayFromZero);

    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString() ?? "";

    private static decimal Number(JsonElement row, string name) => decimal.Parse(Text(row, name), NumberStyles.Number, CultureInfo.InvariantCulture);

    private static DateOnly Date(JsonElement row, string name) => DateOnly.ParseExact(Text(row, name), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<ImmutableArray<JsonElement>> GetAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(Api + path), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var rows = document.RootElement.GetProperty("data").EnumerateArray();
        return [.. rows.Select(row => row.Clone())];
    }
}
