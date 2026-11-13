using System.Text;
using System.Text.Json;

namespace RecDiffy.Capacity;

internal static class MarkdownReport
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

        var text = new StringBuilder();
        text.Append("# RecDiffy capacity run ").Append(JsonView.Text(run, "id")).Append('\n').Append('\n');
        text.Append("| | |\n|---|---|\n");
        Row(text, "Commit", $"`{JsonView.Text(source, "commit")}` on `{JsonView.Text(source, "ref")}`, working tree {JsonView.Text(source, "workingTree")}");
        Row(text, "Started (UTC)", JsonView.Text(run, "startedAtUtc"));
        Row(text, "Duration", $"{Format.Seconds(JsonView.Number(run, "durationSeconds"))} s (compile {Format.Seconds(JsonView.Number(run, "compileSeconds"))} s)");
        Row(text, "Harness", $"{JsonView.Text(run, "harnessVersion")}, engine {JsonView.Text(run, "engineVersion")}");
        Row(text, "Profile", $"{JsonView.Text(run, "profileId")} {JsonView.Text(run, "profileVersion")}, hash `{JsonView.Text(run, "profileHash")}`");
        Row(text, "Dataset", $"seed {Format.Count(JsonView.Integer(run, "seed"))}, {Format.Count(JsonView.Integer(run, "repeats"))} measured run(s) per size");
        Row(text, "Rows per side", Format.Join(JsonView.Items(JsonView.Field(run, "sizes")).Select(item => Format.Count(item.GetInt32())), ", "));
        Row(text, "Command", "`" + JsonView.Text(run, "commandLine") + "`");
        text.Append('\n');

        text.Append("## Host\n\n| | |\n|---|---|\n");
        Row(text, "Machine", JsonView.Text(host, "machine"));
        Row(text, "OS", JsonView.Text(host, "os"));
        Row(text, "Architecture", JsonView.Text(host, "architecture"));
        Row(text, "Logical processors", Format.Count(JsonView.Integer(host, "logicalProcessors")));
        Row(text, "Runtime", JsonView.Text(host, "runtime"));
        Row(text, "SDK", JsonView.Text(host, "sdk"));
        Row(text, "GC", $"{JsonView.Text(host, "gcMode")} GC, {JsonView.Text(host, "gcLatencyMode")}");
        Row(text, "GC heap limit", $"{Format.Gigabytes(JsonView.Integer(host, "gcHeapLimitBytes"))} GB");
        text.Append('\n');

        text.Append("## Largest reconciliation measured\n\n");
        var largest = JsonView.Field(derived, "largestCompletedSize");
        text.Append(largest.ValueKind is JsonValueKind.Null
            ? "No case completed with the outcome it was built to produce.\n\n"
            : $"The largest size that completed with the outcome it was built to produce: **{Format.Count(largest.GetInt32())} rows per side**.\n\n");

        text.Append("## Cases\n\n");
        text.Append("| Rows/side | Outcome | Known outcome | Matched | Exact | Break | Left only | Right only | Total s | Engine s | Excel s | Rows/s | Peak heap MB | Peak working set MB |\n");
        text.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var item in cases)
        {
            var stages = JsonView.Items(JsonView.Field(item, "stages"));
            var engine = stages.Where(stage => JsonView.Text(stage, "name") is "engine").Select(stage => Format.Seconds(JsonView.Number(stage, "seconds"))).FirstOrDefault() ?? "0.000";
            var excel = stages.Where(stage => JsonView.Text(stage, "name") is "excel").Select(stage => Format.Seconds(JsonView.Number(stage, "seconds"))).FirstOrDefault() ?? "0.000";
            var actual = JsonView.Field(item, "actual");
            text.Append("| ")
                .Append(Format.Count(JsonView.Integer(item, "size"))).Append(" | ")
                .Append(JsonView.Text(item, "outcome")).Append(" | ")
                .Append(Known(JsonView.OptionalBool(item, "knownOutcomeMatches"))).Append(" | ")
                .Append(Format.Count(JsonView.Integer(actual, "matched"))).Append(" | ")
                .Append(Format.Count(JsonView.Integer(actual, "exact"))).Append(" | ")
                .Append(Format.Count(JsonView.Integer(actual, "break"))).Append(" | ")
                .Append(Format.Count(JsonView.Integer(actual, "leftOnly"))).Append(" | ")
                .Append(Format.Count(JsonView.Integer(actual, "rightOnly"))).Append(" | ")
                .Append(Format.Seconds(JsonView.Number(item, "latencySeconds"))).Append(" | ")
                .Append(engine).Append(" | ")
                .Append(excel).Append(" | ")
                .Append(Format.Decimal(JsonView.Number(item, "throughputRowsPerSecond"), "0")).Append(" | ")
                .Append(Format.Megabytes(JsonView.Integer(item, "peakManagedHeapBytes"))).Append(" | ")
                .Append(Format.Megabytes(JsonView.Integer(item, "peakWorkingSetBytes"))).Append(" |\n");
        }

        text.Append('\n');
        var noted = cases.Where(item => JsonView.Items(JsonView.Field(item, "notes")).Count > 0).ToArray();
        if (noted.Length > 0)
        {
            text.Append("## Case notes\n\n");
            foreach (var item in noted)
            {
                foreach (var note in JsonView.Items(JsonView.Field(item, "notes")))
                    text.Append("- **").Append(Format.Count(JsonView.Integer(item, "size"))).Append(" rows per side:** ").Append(note.GetString()).Append('\n');
            }

            text.Append('\n');
        }

        var scaling = JsonView.Items(JsonView.Field(derived, "scaling"));
        if (scaling.Count > 0)
        {
            text.Append("## Scaling\n\n| From | To | Time ratio | Rows ratio | Exponent | Verdict |\n|---|---|---|---|---|---|\n");
            foreach (var step in scaling)
            {
                text.Append("| ")
                    .Append(Format.Count(JsonView.Integer(step, "fromSize"))).Append(" | ")
                    .Append(Format.Count(JsonView.Integer(step, "toSize"))).Append(" | ")
                    .Append(Format.Decimal(JsonView.Number(step, "secondsRatio"), "0.00")).Append(" | ")
                    .Append(Format.Decimal(JsonView.Number(step, "rowsRatio"), "0.00")).Append(" | ")
                    .Append(Format.Decimal(JsonView.Number(step, "exponent"), "0.00")).Append(" | ")
                    .Append(JsonView.Text(step, "verdict")).Append(" |\n");
            }

            text.Append('\n');
        }

        text.Append("## Not measured yet\n\n| Stage | Why |\n|---|---|\n");
        foreach (var stage in JsonView.Items(JsonView.Field(derived, "unmeasuredStages")))
            text.Append("| ").Append(JsonView.Text(stage, "name")).Append(" | ").Append(JsonView.Text(stage, "reason")).Append(" |\n");

        text.Append("\n## Notes\n\n");
        foreach (var note in JsonView.Items(JsonView.Field(root, "notes")))
            text.Append("- ").Append(note.GetString()).Append('\n');

        text.Append("\nRun folder: `results.json`, `report.md`, `report.html`. Issue: https://github.com/m-vaysman/recdiffy.net/issues/377\n");
        return text.ToString();
    }

    private static string Known(bool? matches) => matches switch
    {
        true => "matched",
        false => "**MISMATCH**",
        null => "not checked",
    };

    private static void Row(StringBuilder text, string name, string value) =>
        text.Append("| ").Append(name).Append(" | ").Append(value).Append(" |\n");
}
