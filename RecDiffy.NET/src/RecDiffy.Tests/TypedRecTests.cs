using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class TypedRecTests
{
    private static readonly int[] Seeds = [.. Enumerable.Range(1, 25)];

    private static readonly LedgerEntry[] Ledger =
    [
        new("INV-1", new DateOnly(2026, 11, 2), 100.00m, "rent"),
        new(null, new DateOnly(2026, 11, 3), 250.00m, "card"),
        new("INV-3", new DateOnly(2026, 11, 3), 75.00m, "supplies"),
        new("INV-4", new DateOnly(2026, 11, 4), 19.99m, "fees"),
        new("INV-5", new DateOnly(2026, 11, 5), 42.00m, "unreconciled"),
        new(null, new DateOnly(2026, 11, 6), 10.00m, "coffee"),
        new(null, new DateOnly(2026, 11, 6), 10.00m, "coffee"),
    ];

    private static readonly StatementLine[] Statement =
    [
        new("INV-1", new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Unspecified), 100.00m, "RENT NOV"),
        new(null, new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Unspecified), 250.00m, "CARD 1234"),
        new("INV-3", new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Unspecified), 70.00m, "SUPPLIES"),
        new("INV-4", new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Unspecified), 20.00m, "FEES"),
        new("INV-9", new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Unspecified), 5.00m, "INTEREST"),
        new(null, new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Unspecified), 10.00m, "COFFEE"),
    ];

    private static RecBuilder<LedgerEntry, StatementLine> Rec(IEnumerable<LedgerEntry> ledger, IEnumerable<StatementLine> statement) =>
        RecBuilder.Between(ledger, statement)
            .MatchOn(l => l.Ref, s => s.Reference)
            .ThenMatchOn(l => l.Date, s => s.Posted)
            .And(l => l.Amount, s => s.Amount)
            .Compare(l => l.Amount, s => s.Amount, tolerance: 0.05m);

    [Fact]
    public void A_ledger_matches_a_statement_and_returns_the_original_objects()
    {
        var result = Rec(Ledger, Statement).Run();

        Assert.Equal(4, result.Matched.Count);
        Assert.Same(Ledger[0], result.Matched[0].Left[0]);
        Assert.Same(Statement[0], result.Matched[0].Right[0]);
        Assert.Same(Ledger[1], result.Matched[1].Left[0]);
        Assert.Same(Statement[1], result.Matched[1].Right[0]);
        Assert.Equal([FieldOutcome.Exact, FieldOutcome.Exact, FieldOutcome.Break, FieldOutcome.WithinTolerance], result.Matched.Select(match => match.Outcome));
        Assert.Same(Ledger[2], Assert.Single(result.Breaks).Left[0]);
        Assert.Equal("Amount", Assert.Single(result.Breaks[0].Diffs).Field.Name);
        Assert.Same(Ledger[4], Assert.Single(result.LeftOnly));
        Assert.Same(Statement[4], Assert.Single(result.RightOnly));
        var contest = Assert.Single(result.Ambiguous);
        Assert.Equal(2, contest.Left.Count);
        Assert.Contains(contest.Left, entry => ReferenceEquals(entry, Ledger[5]));
        Assert.Contains(contest.Left, entry => ReferenceEquals(entry, Ledger[6]));
        Assert.Same(Statement[5], Assert.Single(contest.Right));
    }

    [Fact]
    public void Member_types_become_field_types_and_each_match_is_a_natural_key_pass()
    {
        var profile = Rec(Ledger, Statement).Profile;

        Assert.Equal(
            ["Ref|Identifier|Key", "Date|Date|Key", "Amount|Decimal|Key"],
            profile.Fields.Select(field => $"{field.Name}|{field.Type}|{field.Role}"),
            StringComparer.Ordinal);
        Assert.Equal(["P1:Ref", "P2:Date,Amount"], profile.Paths[0].Passes.Select(pass => $"{pass.Id}:{string.Join(",", pass.Key)}"), StringComparer.Ordinal);
        Assert.All(profile.Paths[0].Passes, pass => Assert.Equal("match.naturalKey", pass.Pass));
        Assert.Equal(0.05m, profile.Paths[0].Diff!.Fields["Amount"].Tolerance.Absolute);
        Assert.Equal("Reference", profile.Normalization["right"].Identify[0].Strings["from"]);
        Assert.Equal("Posted", profile.Normalization["right"].Parse[0].Strings["from"]);
    }

    // The exported profile is the whole policy: parsed, compiled and run with plain SourceMaps on the same objects, it
    // gives the same result.
    [Fact]
    public void The_exported_json_run_through_parse_compile_and_run_gives_the_same_result()
    {
        var typed = Rec(Ledger, Statement).Run();
        var json = typed.Profile.ToJson();

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", Ledger, new SourceMap<LedgerEntry>().Text("Ref", row => row.Ref).Date("Date", row => row.Date).Decimal("Amount", row => row.Amount)),
            RecordSources.FromObjects("right", Statement, new SourceMap<StatementLine>().Text("Reference", row => row.Reference).Date("Posted", row => DateOnly.FromDateTime(row.Posted)).Decimal("Amount", row => row.Amount)),
            ReferenceDataSet.Empty);

        Assert.Equal(Fingerprint(typed.Result), Fingerprint(result));
    }

    [Fact]
    public void Results_do_not_change_when_both_inputs_are_shuffled()
    {
        var baseline = Outcome(Rec(Ledger, Statement).Run());

        Assert.All(Seeds, seed => Assert.Equal(baseline, Outcome(Rec(Shuffle(Ledger, seed), Shuffle(Statement, seed + 1000)).Run())));
    }

    [Fact]
    public void Whole_numbers_nested_members_and_named_profiles_are_accepted()
    {
        var result = RecBuilder.Between([new Order(new("A-1"), 3, 30L)], [new Delivery(new("A-1"), 3m, 30)])
            .Named("orders", "2.0.0")
            .MatchOn(o => o.Header.Number, d => d.Header.Number)
            .Compare(o => o.Units, d => d.Units)
            .Compare(o => o.Cents, d => d.Cents)
            .Run();

        Assert.Equal(FieldOutcome.Exact, Assert.Single(result.Matched).Outcome);
        Assert.Equal("Header.Number", result.Profile.Fields[0].Name);
        Assert.Equal(("orders", "2.0.0"), (result.Profile.ProfileId, result.Profile.Version));
    }

    [Fact]
    public void A_string_compared_but_not_matched_is_text()
    {
        var profile = Rec(Ledger, Statement).Compare(l => l.Memo, s => s.Description).Profile;

        Assert.Equal(FieldType.Text, profile.Fields.Single(field => field.Name == "Memo").Type);
        Assert.Equal(FieldRole.Economic, profile.Fields.Single(field => field.Name == "Memo").Role);
    }

    [Fact]
    public void Selections_the_engine_cannot_use_are_refused_when_made()
    {
        var start = RecBuilder.Between(Ledger, Statement);

        Assert.Throws<ArgumentException>(() => start.MatchOn(l => l.Ratio, s => s.Ratio));
        Assert.Throws<ArgumentException>(() => start.MatchOn(l => l.Ref!.Trim(), s => s.Reference));
        Assert.Throws<ArgumentException>(() => start.MatchOn(l => l.Ref, s => s.Amount));
        Assert.Throws<ArgumentException>(() => start.MatchOn(l => l.Ref, s => s.Reference).Compare(l => l.Date, s => s.Posted, tolerance: 1m));
        Assert.Throws<InvalidOperationException>(() => start.ThenMatchOn(l => l.Ref, s => s.Reference));
        Assert.Throws<InvalidOperationException>(() => start.And(l => l.Ref, s => s.Reference));
        Assert.Throws<InvalidOperationException>(() => start.Run());
        Assert.Throws<ArgumentException>(() => start.MatchOn(l => l.Amount, s => s.Amount).Compare(l => l.Amount, s => s.Fee).Profile);
    }

    [Fact]
    public void A_builder_never_changes_once_made()
    {
        var start = RecBuilder.Between(Ledger, Statement).MatchOn(l => l.Ref, s => s.Reference);
        var before = start.Profile.ToJson();

        _ = start.ThenMatchOn(l => l.Date, s => s.Posted).Compare(l => l.Amount, s => s.Amount, 1m).Named("other", "9");

        Assert.Equal(before, start.Profile.ToJson());
    }

    private static string Outcome(RecResult<LedgerEntry, StatementLine> result) => string.Join(
        "\n",
        [
            .. result.Matched.Select(match => $"match {Ids(match.Left)} {Ids(match.Right)} {match.Outcome}").Order(StringComparer.Ordinal),
            .. result.Ambiguous.Select(group => $"ambiguous {Ids(group.Left)} {Ids(group.Right)}").Order(StringComparer.Ordinal),
            $"left-only {Ids(result.LeftOnly)}",
            $"right-only {Ids(result.RightOnly)}",
        ]);

    // Identity, not value: two equal ledger entries are told apart by their position in the original array.
    private static string Ids(IEnumerable<LedgerEntry> rows) =>
        string.Join(",", rows.Select(row => Array.FindIndex(Ledger, candidate => ReferenceEquals(candidate, row)).ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));

    private static string Ids(IEnumerable<StatementLine> rows) =>
        string.Join(",", rows.Select(row => Array.FindIndex(Statement, candidate => ReferenceEquals(candidate, row)).ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));

    private static T[] Shuffle<T>(IReadOnlyList<T> rows, int seed)
    {
        var copy = rows.ToArray();
        var random = new Random(seed);
        for (var index = copy.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    private static IReadOnlyList<string> Fingerprint(ReconciliationResult result) =>
    [
        .. result.Entries.Select(entry => string.Join(
            "|",
            entry.Id,
            entry.Alignment,
            entry.Composition,
            entry.Outcome,
            string.Join(",", entry.Left.Select(member => member.Ordinal)),
            string.Join(",", entry.Right.Select(member => member.Ordinal)),
            string.Join(",", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}")))).Order(StringComparer.Ordinal),
        .. result.Rows.Select(row => $"{row.Row.Source}:{row.Row.Ordinal}|{row.Disposition}|{row.Validity}").Order(StringComparer.Ordinal),
    ];

    private sealed record LedgerEntry(string? Ref, DateOnly Date, decimal Amount, string Memo)
    {
        public double Ratio => 0;
    }

    private sealed record StatementLine(string? Reference, DateTime Posted, decimal Amount, string Description)
    {
        public double Ratio => 0;

        public string Fee => "";
    }

    private sealed record Header(string Number);

    private sealed record Order(Header Header, int Units, long Cents);

    private sealed record Delivery(Header Header, decimal Units, int Cents);
}
