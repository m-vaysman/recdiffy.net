using RecDiffy.Results;

namespace RecDiffy.BrowserDemo.Demo;

/// <summary>One status name per result entry, in the words the grid uses, and the order exceptions come first in.</summary>
public static class EntryStatus
{
    public static IReadOnlyList<string> Order { get; } =
        ["Break", "LeftOnly", "RightOnly", "Ambiguous", "SearchIncomplete", "WithinTolerance", "Matched", "Exact"];

    public static string Of(ResultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Alignment switch
        {
            Alignment.Matched => entry.Outcome switch
            {
                FieldOutcome.Exact => "Exact",
                FieldOutcome.WithinTolerance => "WithinTolerance",
                FieldOutcome.Break => "Break",
                _ => "Matched",
            },
            Alignment.Ambiguous => "Ambiguous",
            Alignment.SearchIncomplete => "SearchIncomplete",
            Alignment.LeftOnly => "LeftOnly",
            Alignment.RightOnly => "RightOnly",
            _ => entry.Alignment.ToString(),
        };
    }

    public static int Rank(string status) => Order.ToList().IndexOf(status) switch
    {
        < 0 => Order.Count,
        var rank => rank,
    };
}
