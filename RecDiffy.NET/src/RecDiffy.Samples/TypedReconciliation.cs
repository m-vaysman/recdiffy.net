using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class TypedReconciliation
{
    public static bool Run()
    {
        SampleReport.Heading("9. Reconcile your own objects");
        LedgerEntry[] ledger =
        [
            new("INV-1", new DateOnly(2026, 11, 2), 100.00m),
            new(null, new DateOnly(2026, 11, 3), 250.00m),
            new("INV-3", new DateOnly(2026, 11, 3), 75.00m),
            new("INV-5", new DateOnly(2026, 11, 5), 42.00m),
        ];
        StatementLine[] statement =
        [
            new("INV-1", new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Unspecified), 100.00m),
            new(null, new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Unspecified), 250.00m),
            new("INV-3", new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Unspecified), 70.00m),
        ];

        // Match on the reference first; lines without one match on date and amount.
        var result = RecBuilder.Between(ledger, statement)
            .MatchOn(entry => entry.Ref, line => line.Reference)
            .ThenMatchOn(entry => entry.Date, line => line.Posted)
            .And(entry => entry.Amount, line => line.Amount)
            .Compare(entry => entry.Amount, line => line.Amount, tolerance: 0.05m)
            .Run();

        foreach (var match in result.Matched)
            Console.WriteLine($"matched {match.Left[0]} with {match.Right[0]}: {match.Outcome}");
        foreach (var entry in result.LeftOnly)
            Console.WriteLine($"only in the ledger: {entry}");
        Console.WriteLine(result.Profile.ToJson(indented: false));

        return result.Matched is [{ Outcome: FieldOutcome.Exact }, { Outcome: FieldOutcome.Exact }, { Outcome: FieldOutcome.Break }]
            && ReferenceEquals(result.Matched[0].Left[0], ledger[0])
            && result.LeftOnly is [{ Ref: "INV-5" }]
            && result.RightOnly is [];
    }

    private sealed record LedgerEntry(string? Ref, DateOnly Date, decimal Amount);

    private sealed record StatementLine(string? Reference, DateTime Posted, decimal Amount);
}
