using RecDiffy;
using RecDiffy.PublicDataDemo;

// Usage: dotnet run -- [output folder] [days]
// Downloads the last [days] calendar days of public data, reconciles it and writes [output folder]/index.html.
var output = args is [var folder, ..] ? folder : "site";
var days = args is [_, var count, ..] ? int.Parse(count, System.Globalization.CultureInfo.InvariantCulture) : 30;
var clock = TimeProvider.System;
var now = clock.GetUtcNow();
var today = DateOnly.FromDateTime(now.UtcDateTime);
var from = today.AddDays(-days);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("RecDiffy-PublicDataDemo/1.0");

// FINRA: newest day first, so the first day found is the one the tab shows in full.
var finraRuns = new List<FinraRun>();
FinraLatest? latest = null;
for (var date = today; date > from; date = date.AddDays(-1))
{
    if (await Finra.DownloadAsync(http, date, CancellationToken.None).ConfigureAwait(false) is not { } day)
        continue;
    var started = clock.GetTimestamp();
    var result = Finra.Reconcile(day);
    var run = new FinraRun(day.Date, day.Consolidated.Length, day.Facilities.Length, result.Result.Summary, clock.GetElapsedTime(started));
    finraRuns.Add(run);
    latest ??= new FinraLatest(day, result);
    Console.WriteLine($"FINRA {day.Date.ToInvariant("yyyy-MM-dd")}: {run.Summary.Exact.ToInvariant()} exact, {run.Summary.Break.ToInvariant()} breaks, {Finra.Seconds(run.Elapsed)}");
}

if (latest is null)
    throw new InvalidOperationException($"FINRA published no files in the last {days.ToInvariant()} days.");

// Treasury: one extra week of Debt to the Penny, so the first day in the window has a day to change from.
var lines = await Treasury.StatementAsync(http, from, CancellationToken.None).ConfigureAwait(false);
var debt = await Treasury.DebtAsync(http, from.AddDays(-7), CancellationToken.None).ConfigureAwait(false);
var statementDays = Treasury.Days(lines, from);
var debtChanges = Treasury.Changes(debt, from);
var treasuryStarted = clock.GetTimestamp();
var treasury = Treasury.Reconcile(statementDays, debtChanges);
var treasuryElapsed = clock.GetElapsedTime(treasuryStarted);
Console.WriteLine($"Treasury: {treasury.Result.Summary.WithinTolerance.ToInvariant()} within tolerance, {treasury.Result.Summary.Break.ToInvariant()} breaks");

Tab[] tabs =
[
    new("finra", "FINRA short volume", "FINRA: facility files against the consolidated file", Finra.Tab(latest.Day, latest.Result, finraRuns)),
    new("treasury", "US Treasury debt", "US Treasury: the day's debt change against its issues and redemptions", Treasury.Tab(treasury, lines, from, treasuryElapsed)),
    new("tolerance", "Tolerance", "Tolerance: the same Treasury data, six ways", Tolerances.Tab(Tolerances.Sweep(statementDays, debtChanges), Tolerances.Largest(statementDays, debtChanges))),
];

Directory.CreateDirectory(output);
await File.WriteAllTextAsync(Path.Combine(output, "index.html"), Page.Render(tabs, now)).ConfigureAwait(false);
Console.WriteLine($"Wrote {Path.GetFullPath(Path.Combine(output, "index.html"))}");

