using System.Text;
using System.Text.Json;

namespace RecDiffy.Capacity;

internal static class HtmlReport
{
    public static string Render(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonView.Parse(json);
        var root = document.RootElement;
        var run = JsonView.Field(root, "run");
        var source = JsonView.Field(root, "source");
        var host = JsonView.Field(root, "host");
        var derived = JsonView.Field(root, "derived");
        var cases = JsonView.Items(JsonView.Field(root, "cases"));
        var concurrency = root.TryGetProperty("concurrency", out var found) && found.ValueKind is not JsonValueKind.Null
            ? found
            : (JsonElement?)null;

        var text = new StringBuilder();
        text.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        text.Append("<title>RecDiffy capacity run ").Append(Escape(JsonView.Text(run, "id"))).Append("</title>\n");
        text.Append(Style);
        text.Append("</head>\n<body>\n<main>\n");
        text.Append("<h1>RecDiffy capacity run</h1>\n");
        text.Append("<p class=\"lede\">Phase 1 and phase 3 of <a href=\"https://github.com/m-vaysman/recdiffy.net/issues/377\">issue #377</a>: the sizes sweep and the concurrency sweep, through the public API, on one host. Measured, not promised.</p>\n");

        Header(text, run, source);
        Host(text, host);
        if (concurrency is { } section)
            Concurrency(text, section);
        else
        {
            Cases(text, cases, derived);
            Stages(text, cases);
            Scaling(text, derived);
        }

        Notes(text, root, derived, concurrency);
        Footer(text);
        text.Append("</main>\n</body>\n</html>\n");
        return text.ToString();
    }

    private static void Header(StringBuilder text, JsonElement run, JsonElement source)
    {
        text.Append("<section id=\"run\">\n<h2>Run</h2>\n<table>\n");
        Row(text, "Commit", $"<code>{Escape(JsonView.Text(source, "commit"))}</code> on <code>{Escape(JsonView.Text(source, "ref"))}</code>, working tree {Escape(JsonView.Text(source, "workingTree"))}");
        Row(text, "Sweep", Escape(JsonView.Text(run, "sweep")));
        Row(text, "Started (UTC)", Escape(JsonView.Text(run, "startedAtUtc")));
        Row(text, "Duration", $"{Format.Seconds(JsonView.Number(run, "durationSeconds"))} s (compile {Format.Seconds(JsonView.Number(run, "compileSeconds"))} s)");
        Row(text, "Harness", $"{Escape(JsonView.Text(run, "harnessVersion"))}, engine {Escape(JsonView.Text(run, "engineVersion"))}");
        Row(text, "Profile", $"{Escape(JsonView.Text(run, "profileId"))} {Escape(JsonView.Text(run, "profileVersion"))}, hash <code>{Escape(JsonView.Text(run, "profileHash"))}</code>");
        var seed = Format.Count(JsonView.Integer(run, "seed"));
        var repeats = Format.Count(JsonView.Integer(run, "repeats"));
        Row(text, "Dataset", JsonView.Text(run, "sweep") is "concurrency"
            ? $"seed {seed}, {repeats} batch(es) per level"
            : $"seed {seed}, {repeats} measured run(s) per size, rows per side {Format.Join(JsonView.Items(JsonView.Field(run, "sizes")).Select(item => Format.Count(item.GetInt32())), ", ")}");
        Row(text, "Command", "<code>" + Escape(JsonView.Text(run, "commandLine")) + "</code>");
        text.Append("</table>\n</section>\n");
    }

    private static void Host(StringBuilder text, JsonElement host)
    {
        text.Append("<section id=\"host\">\n<h2>Host</h2>\n<table>\n");
        Row(text, "Machine", Escape(JsonView.Text(host, "machine")));
        Row(text, "OS", Escape(JsonView.Text(host, "os")));
        Row(text, "Architecture", Escape(JsonView.Text(host, "architecture")));
        Row(text, "Logical processors", Format.Count(JsonView.Integer(host, "logicalProcessors")));
        Row(text, "Runtime", Escape(JsonView.Text(host, "runtime")));
        Row(text, "SDK", Escape(JsonView.Text(host, "sdk")));
        Row(text, "GC", $"{Escape(JsonView.Text(host, "gcMode"))} GC, {Escape(JsonView.Text(host, "gcLatencyMode"))}");
        Row(text, "GC heap limit", $"{Format.Gigabytes(JsonView.Integer(host, "gcHeapLimitBytes"))} GB");
        text.Append("</table>\n</section>\n");
    }

    private static void Cases(StringBuilder text, IReadOnlyList<JsonElement> cases, JsonElement derived)
    {
        text.Append("<section id=\"sizes\">\n<h2>The largest reconciliation</h2>\n");
        var largest = JsonView.Field(derived, "largestCompletedSize");
        text.Append(largest.ValueKind is JsonValueKind.Null
            ? "<p class=\"flag\">No case completed with the outcome it was built to produce. The sweep records what each size did.</p>\n"
            : $"<p class=\"flag\">The largest size that completed with the outcome it was built to produce: <strong>{Format.Count(largest.GetInt32())} rows per side</strong>.</p>\n");
        var failure = JsonView.Field(derived, "firstFailure");
        text.Append(failure.ValueKind is JsonValueKind.Null
            ? "<p class=\"flag\">No case failed.</p>\n"
            : $"<p class=\"flag\">The first size that failed: <strong>{Format.Count(JsonView.Integer(failure, "size"))} rows per side</strong>, "
              + $"{Escape(JsonView.Text(failure, "outcome"))} in the {Escape(JsonView.Text(failure, "stage"))} stage after "
              + $"{Format.Seconds(JsonView.Number(failure, "seconds"))} s.</p>\n");

        text.Append("<table>\n<thead><tr><th>Rows/side</th><th>Outcome</th><th>Known outcome</th><th>Matched</th><th>Exact</th><th>Break</th><th>Left only</th><th>Right only</th><th>Total s</th><th>Rows/s</th><th>Peak heap</th><th>Peak working set</th><th>Bytes/row</th><th>Retained heap</th><th>Rows searched</th><th>Rows blocked</th><th>Overflow</th><th>Failure</th></tr></thead>\n<tbody>\n");
        foreach (var item in cases)
        {
            var actual = JsonView.Field(item, "actual");
            text.Append("<tr><td>").Append(Format.Count(JsonView.Integer(item, "size"))).Append("</td>")
                .Append("<td>").Append(Escape(JsonView.Text(item, "outcome"))).Append("</td>")
                .Append("<td>").Append(Known(JsonView.OptionalBool(item, "knownOutcomeMatches"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(actual, "matched"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(actual, "exact"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(actual, "break"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(actual, "leftOnly"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(actual, "rightOnly"))).Append("</td>")
                .Append("<td>").Append(Format.Seconds(JsonView.Number(item, "latencySeconds"))).Append("</td>")
                .Append("<td>").Append(Format.Decimal(JsonView.Number(item, "throughputRowsPerSecond"), "0")).Append("</td>")
                .Append("<td>").Append(Format.Megabytes(JsonView.Integer(item, "peakManagedHeapBytes"))).Append(" MB</td>")
                .Append("<td>").Append(Format.Megabytes(JsonView.Integer(item, "peakWorkingSetBytes"))).Append(" MB</td>")
                .Append("<td>").Append(Format.Decimal(JsonView.Number(item, "bytesPerInputRow"), "0")).Append(" B</td>")
                .Append("<td>").Append(Format.Megabytes(JsonView.Integer(item, "retainedHeapBytes"))).Append(" MB</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(item, "rowsSearched"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(item, "rowsBlocked"))).Append("</td>")
                .Append("<td>").Append(Format.Count(JsonView.Integer(item, "aggregateOverflowFields"))).Append("</td>")
                .Append("<td>").Append(JsonView.OptionalText(item, "failureStage") is { } failed
                    ? $"{Escape(failed)} {Format.Seconds(JsonView.Number(item, "timeToFailureSeconds"))} s"
                    : "-").Append("</td></tr>\n");
        }

        text.Append("</tbody>\n</table>\n");

        foreach (var item in cases.Where(item => JsonView.Items(JsonView.Field(item, "notes")).Count > 0))
        {
            foreach (var note in JsonView.Items(JsonView.Field(item, "notes")))
            {
                text.Append("<p class=\"flag\"><strong>").Append(Format.Count(JsonView.Integer(item, "size"))).Append(" rows per side:</strong> ")
                    .Append(Escape(note.GetString() ?? "")).Append("</p>\n");
            }
        }

        text.Append("<h3>Wall time against size</h3>\n");
        foreach (var item in cases)
        {
            var percent = Format.Decimal(JsonView.Number(item, "wallTimeBarPercent"), "0.0");
            text.Append("<div class=\"chart\"><span class=\"label\">").Append(Format.Count(JsonView.Integer(item, "size"))).Append("</span>")
                .Append("<span class=\"bar\"><span class=\"fill\" style=\"width:").Append(percent).Append("%\"></span></span>")
                .Append("<span class=\"value\">").Append(Format.Seconds(JsonView.Number(item, "latencySeconds"))).Append(" s</span></div>\n");
        }

        text.Append("<h3>Peak managed heap against size</h3>\n");
        foreach (var item in cases)
        {
            var percent = Format.Decimal(JsonView.Number(item, "peakHeapBarPercent"), "0.0");
            text.Append("<div class=\"chart\"><span class=\"label\">").Append(Format.Count(JsonView.Integer(item, "size"))).Append("</span>")
                .Append("<span class=\"bar\"><span class=\"fill heap\" style=\"width:").Append(percent).Append("%\"></span></span>")
                .Append("<span class=\"value\">").Append(Format.Megabytes(JsonView.Integer(item, "peakManagedHeapBytes"))).Append(" MB</span></div>\n");
        }

        text.Append("</section>\n");
    }

    private static void Stages(StringBuilder text, IReadOnlyList<JsonElement> cases)
    {
        text.Append("<section id=\"speed\">\n<h2>Speed, by measured stage</h2>\n");
        text.Append("<p class=\"note\">Only the stages the public API exposes are measured. The stages inside <code>Reconciler.Run</code> are inside <em>engine</em> until P2 and P4 add them.</p>\n");
        foreach (var item in cases)
        {
            text.Append("<h3>").Append(Format.Count(JsonView.Integer(item, "size"))).Append(" rows per side</h3>\n");
            text.Append("<div class=\"chart\"><span class=\"label\">stages</span><span class=\"bar\">");
            foreach (var stage in JsonView.Items(JsonView.Field(item, "stages")))
            {
                var share = Format.Decimal(JsonView.Number(stage, "sharePercent"), "0.0");
                text.Append("<span class=\"fill ").Append(Escape(JsonView.Text(stage, "name"))).Append("\" style=\"width:").Append(share).Append("%\" title=\"")
                    .Append(Escape(JsonView.Text(stage, "name"))).Append(": ").Append(Format.Seconds(JsonView.Number(stage, "seconds"))).Append(" s\"></span>");
            }

            text.Append("</span><span class=\"value\">").Append(Format.Seconds(JsonView.Number(item, "latencySeconds"))).Append(" s</span></div>\n");
            text.Append("<table class=\"stages\">\n<thead><tr><th>Stage</th><th>Seconds</th><th>Share</th></tr></thead>\n<tbody>\n");
            foreach (var stage in JsonView.Items(JsonView.Field(item, "stages")))
            {
                text.Append("<tr><td>").Append(Escape(JsonView.Text(stage, "name"))).Append("</td><td>")
                    .Append(Format.Seconds(JsonView.Number(stage, "seconds"))).Append("</td><td>")
                    .Append(Format.Percent(JsonView.Number(stage, "sharePercent"))).Append("%</td></tr>\n");
            }

            text.Append("<tr><td>rows per second (engine)</td><td colspan=\"2\">").Append(Format.Decimal(JsonView.Number(item, "throughputRowsPerSecond"), "0")).Append("</td></tr>\n");
            text.Append("<tr><td>rows per second (engine and Excel)</td><td colspan=\"2\">").Append(Format.Decimal(JsonView.Number(item, "endToEndRowsPerSecond"), "0")).Append("</td></tr>\n");
            text.Append("<tr><td>p50 / p95 / p99</td><td colspan=\"2\">")
                .Append(Format.Seconds(JsonView.Number(item, "latencySeconds"))).Append(" / ")
                .Append(Format.Seconds(JsonView.Number(item, "latencyP95Seconds"))).Append(" / ")
                .Append(Format.Seconds(JsonView.Number(item, "latencyP99Seconds"))).Append(" s over ")
                .Append(Format.Count(JsonView.Items(JsonView.Field(item, "samples")).Count)).Append(" run(s)</td></tr>\n");
            text.Append("<tr><td>allocated</td><td colspan=\"2\">").Append(Format.Megabytes(JsonView.Integer(item, "allocatedBytes"))).Append(" MB, GC pause ")
                .Append(Format.Seconds(JsonView.Number(item, "gcPauseSeconds"))).Append(" s</td></tr>\n");
            var files = JsonView.Items(JsonView.Field(item, "files"));
            if (files.Count > 0)
            {
                text.Append("<tr><td>Excel</td><td colspan=\"2\">");
                text.Append(Format.Join(files.Select(Describe), "; "));
                text.Append("</td></tr>\n");
            }

            text.Append("</tbody>\n</table>\n");
        }

        text.Append("</section>\n");
    }

    private static void Scaling(StringBuilder text, JsonElement derived)
    {
        var steps = JsonView.Items(JsonView.Field(derived, "scaling"));
        if (steps.Count is 0)
            return;
        text.Append("<section id=\"scaling\">\n<h2>Scaling per step</h2>\n");
        text.Append("<p class=\"note\">Exponent is the log-log slope: 1.0 is linear, 2.0 is quadratic. A step over 1.5 is flagged, informationally.</p>\n");
        text.Append("<table>\n<thead><tr><th>From</th><th>To</th><th>Time ratio</th><th>Rows ratio</th><th>Exponent</th><th>Verdict</th></tr></thead>\n<tbody>\n");
        foreach (var step in steps)
        {
            text.Append("<tr><td>").Append(Format.Count(JsonView.Integer(step, "fromSize"))).Append("</td><td>")
                .Append(Format.Count(JsonView.Integer(step, "toSize"))).Append("</td><td>")
                .Append(Format.Decimal(JsonView.Number(step, "secondsRatio"), "0.00")).Append("</td><td>")
                .Append(Format.Decimal(JsonView.Number(step, "rowsRatio"), "0.00")).Append("</td><td>")
                .Append(Format.Decimal(JsonView.Number(step, "exponent"), "0.00")).Append("</td><td>")
                .Append(Escape(JsonView.Text(step, "verdict"))).Append("</td></tr>\n");
        }

        text.Append("</tbody>\n</table>\n</section>\n");
    }

    private static void Concurrency(StringBuilder text, JsonElement section)
    {
        var cases = JsonView.Items(JsonView.Field(section, "cases"));
        text.Append("<section id=\"concurrency\">\n<h2>Concurrency</h2>\n");
        text.Append("<p class=\"note\">Each batch starts all N runs together over the same seeded dataset. Rows per run are fixed, so total work grows with N; the numbers below are throughput, not total batch time.</p>\n");
        text.Append("<div class=\"facts\">\n");
        Fact(text, "Rows per run", Format.Count(JsonView.Integer(section, "rowsPerRun")) + " per side");
        Fact(text, "Batches per level", Format.Count(JsonView.Integer(section, "repeats")));
        Fact(text, "GC", Escape(JsonView.Text(section, "gcMode")) + " GC");
        Fact(text, "Logical processors", Format.Count(JsonView.Integer(section, "logicalProcessors")));
        Fact(text, "Baseline fingerprint", "<code>" + Escape(JsonView.Text(section, "baselineFingerprint")) + "</code>");
        Fact(text, "Results against the baseline", JsonView.Field(section, "allMatchBaseline").GetBoolean()
            ? "<strong>every run matched, field by field</strong>"
            : "<strong class=\"bad\">a run did not match the baseline</strong>");
        text.Append("</div>\n");

        text.Append("<h3>Rows per second against concurrency</h3>\n");
        foreach (var style in new[] { "threads", "tasks", "parallel" })
        {
            var ofStyle = cases.Where(item => string.Equals(JsonView.Text(item, "style"), style, StringComparison.Ordinal)).ToArray();
            if (ofStyle.Length is 0)
                continue;
            text.Append("<h4>").Append(Escape(style)).Append("</h4>\n");
            foreach (var level in ofStyle.Select(item => JsonView.Integer(item, "level")).Distinct().Order())
            {
                var shared = ofStyle.FirstOrDefault(item => JsonView.Integer(item, "level") == level && JsonView.Text(item, "sharing") is "shared");
                var separate = ofStyle.FirstOrDefault(item => JsonView.Integer(item, "level") == level && JsonView.Text(item, "sharing") is "separate");
                text.Append("<div class=\"chart\"><span class=\"label\">N=").Append(Format.Count(level)).Append("</span><span class=\"bar\">");
                if (shared.ValueKind is not JsonValueKind.Undefined)
                    text.Append("<span class=\"fill shared\" style=\"width:").Append(Format.Decimal(JsonView.Number(shared, "throughputBarPercent"), "0.0")).Append("%\"></span>");
                if (separate.ValueKind is not JsonValueKind.Undefined)
                    text.Append("<span class=\"fill separate\" style=\"width:").Append(Format.Decimal(JsonView.Number(separate, "throughputBarPercent"), "0.0")).Append("%\"></span>");
                text.Append("</span><span class=\"value\">");
                if (shared.ValueKind is not JsonValueKind.Undefined)
                    text.Append(Format.Decimal(JsonView.Number(shared, "rowsPerSecond"), "0")).Append(" shared");
                if (shared.ValueKind is not JsonValueKind.Undefined && separate.ValueKind is not JsonValueKind.Undefined)
                    text.Append(" / ");
                if (separate.ValueKind is not JsonValueKind.Undefined)
                    text.Append(Format.Decimal(JsonView.Number(separate, "rowsPerSecond"), "0")).Append(" separate");
                text.Append(" rows/s</span></div>\n");
            }
        }

        text.Append("<h3>Every batch</h3>\n");
        text.Append("<table>\n<thead><tr><th>Fan-out</th><th>Profiles</th><th>Runs</th><th>Wall s</th><th>Runs/min</th><th>Rows/s</th><th>Efficiency</th><th>Cores used</th><th>Peak heap</th><th>p50</th><th>p95</th><th>p99</th><th>Matches</th></tr></thead>\n<tbody>\n");
        foreach (var item in cases)
        {
            text.Append("<tr><td>").Append(Escape(JsonView.Text(item, "style"))).Append("</td><td>")
                .Append(Escape(JsonView.Text(item, "sharing"))).Append("</td><td>")
                .Append(Format.Count(JsonView.Integer(item, "runs"))).Append("</td><td>")
                .Append(Format.Seconds(JsonView.Number(item, "wallSeconds"))).Append("</td><td>")
                .Append(Format.Decimal(JsonView.Number(item, "runsPerMinute"), "0.0")).Append("</td><td>")
                .Append(Format.Decimal(JsonView.Number(item, "rowsPerSecond"), "0")).Append("</td><td>")
                .Append(Format.Decimal(JsonView.Number(item, "scalingEfficiencyPercent"), "0.0")).Append("%</td><td>")
                .Append(Format.Decimal(JsonView.Number(item, "coresUsed"), "0.0")).Append("</td><td>")
                .Append(Format.Megabytes(JsonView.Integer(item, "peakManagedHeapBytes"))).Append(" MB</td><td>")
                .Append(Format.Seconds(JsonView.Number(item, "latencyP50Seconds"))).Append("</td><td>")
                .Append(Format.Seconds(JsonView.Number(item, "latencyP95Seconds"))).Append("</td><td>")
                .Append(Format.Seconds(JsonView.Number(item, "latencyP99Seconds"))).Append("</td><td>")
                .Append(JsonView.Field(item, "resultsMatchBaseline").GetBoolean() ? "yes" : "<strong class=\"bad\">no</strong>").Append("</td></tr>\n");
        }

        text.Append("</tbody>\n</table>\n</section>\n");
    }

    private static void Fact(StringBuilder text, string name, string html) =>
        text.Append("<div class=\"fact\"><span class=\"name\">").Append(Escape(name)).Append("</span><span class=\"value\">").Append(html).Append("</span></div>\n");

    private static void Notes(StringBuilder text, JsonElement root, JsonElement derived, JsonElement? concurrency)
    {
        text.Append("<section id=\"notes\">\n<h2>Not measured yet</h2>\n<dl>\n");
        foreach (var stage in JsonView.Items(JsonView.Field(derived, "unmeasuredStages")))
            text.Append("<dt>").Append(Escape(JsonView.Text(stage, "name"))).Append("</dt><dd>").Append(Escape(JsonView.Text(stage, "reason"))).Append("</dd>\n");
        text.Append("</dl>\n<h2>Notes</h2>\n<ul>\n");
        var notes = concurrency is { } section ? JsonView.Items(JsonView.Field(section, "notes")) : JsonView.Items(JsonView.Field(root, "notes"));
        foreach (var note in notes)
            text.Append("<li>").Append(Escape(note.GetString() ?? "")).Append("</li>\n");
        text.Append("</ul>\n</section>\n");
    }

    private static void Footer(StringBuilder text)
    {
        text.Append("<footer>\n<p>Source of truth: <code>results.json</code> in the run folder, next to this page and <code>report.md</code>. Generated by the RecDiffy capacity harness for <a href=\"https://github.com/m-vaysman/recdiffy.net/issues/377\">issue #377</a>.</p>\n</footer>\n");
    }

    private static string Known(bool? matches) => matches switch
    {
        true => "matched",
        false => "<strong class=\"bad\">MISMATCH</strong>",
        null => "not checked",
    };

    private static string Describe(JsonElement file) =>
        JsonView.OptionalText(file, "skipReason") is { } reason
            ? $"{Escape(JsonView.Text(file, "kind"))}: skipped ({Escape(reason)})"
            : $"{Escape(JsonView.Text(file, "kind"))}: {Escape(JsonView.Text(file, "path"))} ({Format.Megabytes(JsonView.Integer(file, "bytes"))} MB)";

    private static void Row(StringBuilder text, string name, string value) =>
        text.Append("<tr><th>").Append(Escape(name)).Append("</th><td>").Append(value).Append("</td></tr>\n");

    private static string Escape(string? value) =>
        (value ?? "").Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private const string Style = """
        <style>
        :root { color-scheme: light dark; --ink: #1b1b1b; --muted: #5c5c5c; --rule: #d8d8d8; --paper: #fdfdfc; --engine: #35618f; --excel: #8a6240; --bad: #a32020; }
        @media (prefers-color-scheme: dark) { :root { --ink: #e8e8e6; --muted: #a8a8a4; --rule: #3a3a38; --paper: #161615; --engine: #79a7d6; --excel: #c39a6b; } }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--paper); color: var(--ink); font: 15px/1.5 "Segoe UI", system-ui, -apple-system, sans-serif; }
        main { max-width: 1080px; margin: 0 auto; padding: 2rem 1.25rem 4rem; }
        h1 { font-size: 1.7rem; margin: 0 0 .25rem; }
        h2 { font-size: 1.2rem; margin: 2.4rem 0 .6rem; border-bottom: 1px solid var(--rule); padding-bottom: .25rem; }
        h3 { font-size: 1rem; margin: 1.6rem 0 .5rem; }
        p.lede { color: var(--muted); margin: 0 0 1.5rem; }
        p.note, p.flag { color: var(--muted); }
        p.flag strong { color: var(--ink); }
        a { color: var(--engine); }
        table { border-collapse: collapse; width: 100%; margin: .4rem 0 1rem; font-size: .92rem; }
        th, td { text-align: left; padding: .35rem .5rem; border-bottom: 1px solid var(--rule); vertical-align: top; }
        thead th { color: var(--muted); font-weight: 600; }
        tbody tr:hover { background: color-mix(in srgb, var(--rule) 28%, transparent); }
        table.stages { width: auto; min-width: 60%; }
        dl { display: grid; grid-template-columns: max-content 1fr; gap: .3rem 1rem; }
        dt { font-weight: 600; }
        dd { margin: 0; color: var(--muted); }
        .facts { display: grid; grid-template-columns: max-content 1fr; gap: .25rem 1rem; margin: .5rem 0 1rem; }
        .fact { display: contents; }
        .fact .name { color: var(--muted); }
        code { font-size: .85em; }
        .chart { display: grid; grid-template-columns: 7rem 1fr 6rem; align-items: center; gap: .5rem; margin: .3rem 0; }
        .chart .label { color: var(--muted); font-size: .85rem; text-align: right; }
        .chart .bar { display: flex; height: 1.1rem; background: color-mix(in srgb, var(--rule) 45%, transparent); border-radius: 3px; overflow: hidden; }
        .chart .fill { display: block; height: 100%; background: var(--engine); }
        .chart .fill.heap { background: var(--excel); }
        .chart .fill.excel { background: var(--excel); }
        .chart .fill.shared { background: var(--engine); }
        .chart .fill.separate { background: var(--excel); }
        .chart .value { color: var(--muted); font-size: .85rem; }
        .bad { color: var(--bad); }
        footer { margin-top: 3rem; border-top: 1px solid var(--rule); color: var(--muted); font-size: .85rem; }
        </style>
        """;
}
