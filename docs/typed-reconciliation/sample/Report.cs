using System.Globalization;
using RecDiffy;
using RecDiffy.Results;

public static class Report
{
    public static void Print<TLeft, TRight>(RecResult<TLeft, TRight> result, Func<TLeft, string> left, Func<TRight, string> right)
    {
        Console.WriteLine($"Matched {result.Matched.Count} ({result.Breaks.Count} with breaks) | Ambiguous {result.Ambiguous.Count} | Left only {result.LeftOnly.Count} | Right only {result.RightOnly.Count}");
        Console.WriteLine();
        foreach (var match in result.Matched)
        {
            Console.WriteLine($"  {Label(match.Outcome),-17} {left(match.Left[0])} ↔ {right(match.Right[0])}");
            foreach (var diff in match.Diffs.Where(diff => diff.Outcome is FieldOutcome.WithinTolerance or FieldOutcome.Break))
                Console.WriteLine($"  {"",-17}   {diff.Field}: {Show(diff.Left)} vs {Show(diff.Right)}");
        }

        foreach (var group in result.Ambiguous)
            Console.WriteLine($"  {"Ambiguous",-17} {string.Join(" + ", group.Left.Select(left))} ↔ {string.Join(" + ", group.Right.Select(right))}");
        foreach (var item in result.LeftOnly)
            Console.WriteLine($"  {"Left only",-17} {left(item)}");
        foreach (var item in result.RightOnly)
            Console.WriteLine($"  {"Right only",-17} {right(item)}");
    }

    private static string Label(FieldOutcome outcome) => outcome switch
    {
        FieldOutcome.WithinTolerance => "Within tolerance",
        _ => outcome.ToString(),
    };

    private static string Show(CanonicalValue value) => value switch
    {
        { IsNull: true } => "(blank)",
        { Type: FieldType.Decimal } => value.Number.ToString(CultureInfo.InvariantCulture),
        { Type: FieldType.Date } => value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value.Text ?? "",
    };
}
