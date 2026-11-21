using System.Text;
using RecDiffy;
using RecDiffy.Results;

namespace BlindDiscovery;

internal static class RunDigest
{
    private const int ValueExamples = 3;

    public static string Format(IReconciliationResult result, IReadOnlyList<string> fieldNames)
    {
        var summary = result.Summary;
        var lines = new List<string>
        {
            "# delta = right - left. ratio = right / left. sameSign = share of non-zero breaks with the same sign.",
            "summary"
                + $" matched={summary.Matched.ToInvariant()}"
                + $" exact={summary.Exact.ToInvariant()}"
                + $" withinTolerance={summary.WithinTolerance.ToInvariant()}"
                + $" break={summary.Break.ToInvariant()}"
                + $" leftOnly={summary.LeftOnly.ToInvariant()}"
                + $" rightOnly={summary.RightOnly.ToInvariant()}"
                + $" ambiguous={summary.Ambiguous.ToInvariant()}"
                + $" searchIncomplete={summary.SearchIncomplete.ToInvariant()}"
                + $" quarantined={summary.Quarantined.ToInvariant()}"
                + $" suppressed={summary.Suppressed.ToInvariant()}",
        };
        var composition = CompositionLine(result);
        if (composition.Length > 0)
            lines.Add(composition);
        var diagnostics = Diagnostics(result);
        if (diagnostics.Length > 0)
            lines.Add(diagnostics);

        var fields = result.Entries
            .Where(entry => entry.Alignment == Alignment.Matched)
            .SelectMany(entry => entry.Diffs)
            .GroupBy(diff => diff.Field.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(Field)
            .Where(line => line is not null)
            .Cast<string>()
            .ToArray();
        lines.Add(fields.Length == 0 ? "fields none" : "fields");
        lines.AddRange(fields);

        lines.Add("unmatched");
        foreach (var alignment in new[] { Alignment.LeftOnly, Alignment.RightOnly, Alignment.Ambiguous, Alignment.SearchIncomplete })
        {
            var entries = result.Entries.Where(entry => entry.Alignment == alignment).ToArray();
            if (entries.Length == 0)
                continue;
            var examples = entries
                .OrderBy(entry => entry.Left.Concat(entry.Right).Select(row => row.Ordinal).DefaultIfEmpty(int.MaxValue).Min())
                .ThenBy(entry => entry.Id.Value, StringComparer.Ordinal)
                .Take(3)
                .Select(Example);
            lines.Add($"  {Name(alignment)}={entries.Length.ToInvariant()} examples={string.Join(" | ", examples)}");
            lines.AddRange(CanonicalExamples(result, entries, fieldNames));
        }

        if (lines[^1] == "unmatched")
            lines.Add("  none");

        if (summary.Quarantined > 0)
        {
            var samples = result.Rows
                .Where(row => row.Disposition == Disposition.Quarantined)
                .Take(3)
                .Select(row => $"{(row.Row.RowKey.Length == 0 ? "#" + row.Row.Ordinal.ToInvariant() : row.Row.RowKey)}:{string.Join('+', row.Diagnostics.Select(diagnostic => diagnostic.Code))}");
            lines.Add("quarantine examples " + string.Join(" | ", samples));
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string? Field(IGrouping<string, FieldDiff> group)
    {
        var breaks = group.Where(diff => diff.Outcome == FieldOutcome.Break).ToArray();
        var tolerated = group.Count(diff => diff.Outcome == FieldOutcome.WithinTolerance);
        if (breaks.Length == 0 && tolerated == 0)
            return null;

        var line = new StringBuilder($"  {group.Key} break={breaks.Length.ToInvariant()}");
        if (tolerated > 0)
            line.Append($" withinTolerance={tolerated.ToInvariant()}");
        if (breaks.Length == 0)
            return line.ToString();

        line.Append($" nullVsValue={breaks.Count(diff => diff.Left.IsNull != diff.Right.IsNull).ToInvariant()}");
        line.Append($" sameSign={SameSign(breaks)}");
        var numbers = breaks.Where(diff => diff.Delta.Kind == DeltaKind.Number && !diff.Left.IsNull && !diff.Right.IsNull).Select(diff => diff.Delta.Number).Order().ToArray();
        var days = breaks.Where(diff => diff.Delta.Kind == DeltaKind.Days).Select(diff => diff.Delta.Days).Order().ToArray();
        if (numbers.Length > 0)
            line.Append($" delta[min={Num(numbers[0])} p50={Num(Quantile(numbers, 0.50))} p90={Num(Quantile(numbers, 0.90))} p99={Num(Quantile(numbers, 0.99))} max={Num(numbers[^1])}]");
        if (days.Length > 0)
            line.Append($" deltaDays[min={days[0].ToInvariant()} p50={Quantile(days, 0.50).ToInvariant()} p90={Quantile(days, 0.90).ToInvariant()} max={days[^1].ToInvariant()}]");
        line.Append($" ratio={Ratio(breaks)}");
        var shown = breaks.Take(ValueExamples).Select(diff => $"L={Show(diff.Left)} R={Show(diff.Right)}");
        line.Append($" values[{string.Join(" ; ", shown)}]");
        return line.ToString();
    }

    private static IEnumerable<string> CanonicalExamples(IReconciliationResult result, IReadOnlyList<ResultEntry> entries, IReadOnlyList<string> fieldNames) =>
        entries
            .OrderBy(entry => entry.Left.Concat(entry.Right).Select(row => row.Ordinal).DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(entry => entry.Id.Value, StringComparer.Ordinal)
            .Take(ValueExamples)
            .SelectMany(entry => entry.Left.Take(1).Select(row => ("L", row)).Concat(entry.Right.Take(1).Select(row => ("R", row))))
            .Select(pair => $"    {pair.Item1}#{pair.row.Ordinal.ToInvariant()} {Row(result.Canonical(pair.row), fieldNames)}");

    private static string Row(CanonicalRow row, IReadOnlyList<string> fieldNames) =>
        string.Join(' ', fieldNames.Select(name => $"{name}={Show(row[name])}"));

    private static string Show(CanonicalValue value) =>
        value.IsNull ? "null" : Clip(value.Text ?? value.Type switch
        {
            FieldType.Date => value.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _ => Num(value.Number),
        }, 40);

    private static string SameSign(IReadOnlyList<FieldDiff> breaks)
    {
        var both = breaks.Where(diff => !diff.Left.IsNull && !diff.Right.IsNull && diff.Left.Number != 0m && diff.Right.Number != 0m && diff.Left.Type == FieldType.Decimal).ToArray();
        if (both.Length == 0)
            return "n/a";
        var same = both.Count(diff => Math.Sign(diff.Left.Number) == Math.Sign(diff.Right.Number));
        return decimal.Round(same / (decimal)both.Length, 2, MidpointRounding.AwayFromZero).ToInvariant("0.00");
    }

    private static string Ratio(IReadOnlyList<FieldDiff> breaks)
    {
        var ratios = breaks
            .Where(diff => !diff.Left.IsNull && !diff.Right.IsNull && diff.Left.Type == FieldType.Decimal && diff.Left.Number != 0m)
            .Select(diff => decimal.Round(diff.Right.Number / diff.Left.Number, 6, MidpointRounding.AwayFromZero))
            .ToArray();
        if (ratios.Length == 0)
            return "n/a";
        var modal = ratios.GroupBy(ratio => ratio).MaxBy(group => group.Count())!;
        var share = decimal.Round(modal.Count() / (decimal)ratios.Length, 2, MidpointRounding.AwayFromZero);
        return share >= 0.5m ? $"{Num(modal.Key)}@{share.ToInvariant("0.00")}" : "mixed";
    }

    private static decimal Quantile(decimal[] sorted, double p) => sorted[Rank(sorted.Length, p)];

    private static int Quantile(int[] sorted, double p) => sorted[Rank(sorted.Length, p)];

    private static int Rank(int length, double p) => Math.Clamp((int)Math.Ceiling(p * length) - 1, 0, length - 1);

    private static string CompositionLine(IReconciliationResult result)
    {
        (string Name, Composition Flag)[] flags =
        [
            ("oneToOne", Composition.OneToOne),
            ("manyToOne", Composition.ManyToOne),
            ("oneToMany", Composition.OneToMany),
            ("package", Composition.Package),
            ("lifecycle", Composition.LifecycleCollapsed),
        ];
        var parts = flags
            .Select(flag => (flag.Name, Count: result.Entries.Count(entry => entry.Composition.HasFlag(flag.Flag))))
            .Where(part => part.Count > 0)
            .Select(part => $"{part.Name}={part.Count.ToInvariant()}")
            .ToArray();
        return parts.Length == 0 ? "" : "composition " + string.Join(' ', parts);
    }

    private static string Diagnostics(IReconciliationResult result)
    {
        var parts = result.Rows
            .SelectMany(row => row.Diagnostics)
            .GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .Select(group => (group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(8)
            .Select(group => $"{group.Key}={group.Count.ToInvariant()}")
            .ToArray();
        return parts.Length == 0 ? "" : "diagnostics " + string.Join(' ', parts);
    }

    private static string Example(ResultEntry entry)
    {
        var left = Keys(entry.Left);
        var right = Keys(entry.Right);
        var body = (entry.Left.Length, entry.Right.Length) switch
        {
            ( > 0, > 0) => $"L:{left} R:{right}",
            ( > 0, _) => left,
            _ => right,
        };
        var trail = entry.Explanation.Trail.FirstOrDefault();
        var key = trail?.Key is { Length: > 0 } shown && !string.Equals(shown, body, StringComparison.Ordinal)
            ? " key=" + Clip(shown, 60)
            : "";
        var detail = trail?.Detail is { Length: > 0 } why ? " " + Clip(why, 40) : "";
        return body + key + detail;
    }

    private static string Keys(IReadOnlyList<SourceRef> rows) =>
        rows.Count == 0
            ? "-"
            : string.Join("+", rows.OrderBy(row => row.Ordinal).ThenBy(row => row.RowKey, StringComparer.Ordinal).Select(Label));

    private static string Label(SourceRef row) =>
        Clip(row.RowKey.Length == 0 ? "#" + row.Ordinal.ToInvariant() : row.RowKey, 32);

    private static string Name(Alignment alignment) => alignment switch
    {
        Alignment.LeftOnly => "leftOnly",
        Alignment.RightOnly => "rightOnly",
        Alignment.Ambiguous => "ambiguous",
        Alignment.SearchIncomplete => "searchIncomplete",
        _ => alignment.ToString(),
    };

    private static string Clip(string value, int width) =>
        value.Length <= width ? value : string.Concat(value.AsSpan(0, width - 1), "…");

    private static string Num(decimal value) => value.ToInvariant("0.################");
}
