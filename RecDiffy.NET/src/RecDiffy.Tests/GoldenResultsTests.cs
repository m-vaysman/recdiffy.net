using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// What the engine returns for two fixed pairs of inputs, as text, compared with Goldens/golden-trades.txt and
// Goldens/golden-structures.txt. Any change in a result fails here, on net8.0 and net10.0: entries, diffs, rows,
// diagnostics, the summary, explanations, the raw cells, canonical values and trace of every row, and two side-by-side
// views, all in the order the engine returns them (spec v3.1 §10.1).
//
// Accepting an intended change is a manual step: copy the .actual.txt file the failure names over the golden file and
// commit it. Nothing rewrites a golden file.
public class GoldenResultsTests
{
    private static readonly int[] Seeds = [1, 2, 3, 5, 8, 13, 21, 34];

    public static TheoryData<string> Scenarios => ["trades", "structures"];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_result_matches_the_golden_file(string name)
    {
        var scenario = Scenario(name);

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(scenario, GoldenResult.Run(scenario))), GoldenPath(scenario), ActualPath(scenario, "run"));

        Assert.True(failure is null, failure);
    }

    // Spec v3.1 §10.1: the same rows in any order give the same result. A SourceRef carries the row's position in its
    // input, so each shuffled position is mapped back to the position the fixed input has. Three orders follow the
    // input, and only they are put in a fixed order before the two texts are compared: the rows; unmatched entries of
    // one alignment on one route, among themselves; and inside an entry, the members of an unmatched entry and the
    // copies of one row key. §10.1 allows that: commit order "affects output order only". Everything else must be in
    // the same order, the matched entries and their members included.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_result_is_the_same_for_rows_in_any_order(string name)
    {
        var scenario = Scenario(name);
        var expected = GoldenText.Lines(GoldenResult.Text(scenario, GoldenResult.Run(scenario), anyOrder: true));
        var shuffles = Seeds.Select(seed => (Name: $"seed {seed.ToInvariant()}", Left: Shuffled(scenario.Left.Count, seed), Right: Shuffled(scenario.Right.Count, seed + 1000)))
            .Append((Name: "reversed", Left: [.. Enumerable.Range(0, scenario.Left.Count).Reverse()], Right: [.. Enumerable.Range(0, scenario.Right.Count).Reverse()]));

        string[] failures =
        [
            .. shuffles
                .Select(shuffle => (shuffle.Name, Lines: GoldenText.Lines(GoldenResult.Text(scenario, GoldenResult.Run(scenario, shuffle.Left, shuffle.Right), anyOrder: true, shuffle.Left, shuffle.Right))))
                .Select(run => GoldenText.Difference(expected, run.Lines) is { } difference ? $"Rows shuffled with {run.Name} give a different result:\n{difference}" : null)
                .OfType<string>(),
        ];

        Assert.True(failures.Length is 0, string.Join("\n\n", failures));
    }

    // The profiles declare their cultures, so the machine's culture changes nothing (spec v3.1 §3.5).
    [Theory]
    [InlineData("trades", "")]
    [InlineData("trades", "de-DE")]
    [InlineData("trades", "tr-TR")]
    [InlineData("trades", "fr-FR")]
    [InlineData("trades", "ar-SA")]
    [InlineData("structures", "")]
    [InlineData("structures", "de-DE")]
    [InlineData("structures", "tr-TR")]
    [InlineData("structures", "fr-FR")]
    [InlineData("structures", "ar-SA")]
    public void The_result_is_the_same_under_any_machine_culture(string name, string machine)
    {
        var scenario = Scenario(name);
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(machine);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(machine);

            var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(scenario, GoldenResult.Run(scenario))), GoldenPath(scenario), ActualPath(scenario, "culture"));

            Assert.True(failure is null, failure);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    // RunOptions: "The value does not change the result."
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_result_is_the_same_when_result_building_runs_in_parallel(string name)
    {
        var scenario = Scenario(name);
        var result = GoldenResult.Run(scenario, options: new RunOptions(MaxDegreeOfParallelism: 4));

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(scenario, result)), GoldenPath(scenario), ActualPath(scenario, "parallel"));

        Assert.True(failure is null, failure);
    }

    [Fact]
    public async Task The_trades_result_is_the_same_from_RunAsync()
    {
        var scenario = GoldenTrades.Scenario;
        var result = await Reconciler.RunAsync(scenario.Compile(), GoldenTrades.LeftSource(), GoldenTrades.RightSource(), scenario.Reference);

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(scenario, result)), GoldenPath(scenario), ActualPath(scenario, "async"));

        Assert.True(failure is null, failure);
    }

    // The same cells through the data reader source: the second way rows reach intake.
    [Fact]
    public void The_trades_result_is_the_same_when_the_rows_come_from_a_data_reader()
    {
        var scenario = GoldenTrades.Scenario;
        using var left = GoldenTrades.LeftTable();
        using var right = GoldenTrades.RightTable();
        var result = Reconciler.Run(
            scenario.Compile(),
            RecordSources.FromDataReader("left", left.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", right.CreateDataReader, new TabularOptions()),
            scenario.Reference);

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(scenario, result)), GoldenPath(scenario), ActualPath(scenario, "reader"));

        Assert.True(failure is null, failure);
    }

    // What the trades inputs are there to cover. If an edit to the inputs drops a case, this says which.
    [Fact]
    public void The_trades_inputs_cover_every_case_they_are_there_for()
    {
        var scenario = GoldenTrades.Scenario;
        var result = GoldenResult.Run(scenario);
        var see = new Seen(result);

        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Exact, "P1"), Shape(see.Entry("OMS-EXACT")));
        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.WithinTolerance, "P2"), Shape(see.Entry("OMS-WITHIN")));
        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Break, "P1"), Shape(see.Entry("OMS-BREAK")));
        Assert.Equal(Alignment.LeftOnly, see.Entry("OMS-ORPHAN").Alignment);
        Assert.Equal(Alignment.RightOnly, see.Entry("ST-ALLEGED").Alignment);
        Assert.Equal((Alignment.Ambiguous, 2, 1), (see.Entry("ST-AMB").Alignment, see.Entry("ST-AMB").Left.Length, see.Entry("ST-AMB").Right.Length));
        Assert.Equal((Alignment.SearchIncomplete, "BlockTooLarge"), (see.Entry("ST-BIG").Alignment, see.Entry("ST-BIG").Explanation.Trail[0].Detail));

        Assert.Equal(["DecimalParse"], see.Codes("OMS-BADQTY"), StringComparer.Ordinal);
        Assert.Equal(["PriceParse"], see.Codes("ST-BADPX"), StringComparer.Ordinal);
        Assert.Equal(["DateParse", "RequiredMissing", "UnknownParty"], see.Codes("ST-NOPARTY"), StringComparer.Ordinal);
        Assert.Equal(["RequiredMissing"], see.Codes("OMS-NODATE"), StringComparer.Ordinal);
        Assert.Equal(["DuplicateConflict"], see.Codes("OMS-CONFLICT"), StringComparer.Ordinal);
        Assert.Equal(["DuplicateRowCollapsed"], see.Codes("OMS-TWICE"), StringComparer.Ordinal);
        Assert.Equal(["IncompletePackage"], see.Codes("OMS-HALF-PAY"), StringComparer.Ordinal);
        Assert.Equal(["MissingLifecycleTarget"], see.Codes("ST-LOST-CORR"), StringComparer.Ordinal);
        Assert.All(new[] { "OMS-BADQTY", "ST-BADPX", "OMS-CONFLICT", "OMS-HALF-PAY", "ST-LOST-CORR", "ST-NOPARTY", "OMS-NODATE" }, rowKey => see.Quarantined(rowKey));
        Assert.Equal(2, see.Rows("OMS-CONFLICT").Length);
        Assert.Equal(2, see.Rows("OMS-TWICE").Length);
        Assert.All(see.Rows("OMS-TWICE"), row => Assert.Equal((Disposition.Matched, InputValidity.ValidWithWarnings), (row.Disposition, row.Validity)));
        Assert.Contains(result.Rows, row => row.Disposition is Disposition.Quarantined && row.Diagnostics.Any(diagnostic => diagnostic.Code is "MissingKey"));

        Assert.Equal((Alignment.Matched, Composition.OneToOne | Composition.LifecycleCollapsed, FieldOutcome.Exact, "P1"), Shape(see.Entry("OMS-LIFE")));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded), see.Fate("ST-LIFE-NEW"));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.CancelEvent), see.Fate("ST-LIFE-CANC"));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.FullyCancelled), see.Fate("ST-GONE-NEW"));

        Assert.Equal((Alignment.Matched, Composition.ManyToOne | Composition.Package, FieldOutcome.Exact, "P1"), Shape(see.Entry("OMS-LEG-PAY")));
        Assert.Equal((Alignment.Matched, Composition.ManyToOne, FieldOutcome.Exact, "P3"), Shape(see.Entry("ST-BLOCK")));
        Assert.Equal(3, see.Entry("ST-BLOCK").Left.Length);

        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Break, "P4"), Shape(see.Entry("OMS-FILL")));
        Assert.Equal(["Quantity"], see.Diff("OMS-FILL", "Accrued").AttributedTo, StringComparer.Ordinal);
        Assert.Equal(["Quantity", "Accrued"], see.Diff("OMS-FILL", "NetCash").AttributedTo, StringComparer.Ordinal);
        Assert.Equal(["Accrued"], see.Diff("OMS-WITHIN", "NetCash").AttributedTo, StringComparer.Ordinal);
        Assert.Equal((FieldOutcome.WithinTolerance, 1), (see.Diff("OMS-WITHIN", "SettleDate").Outcome, see.Diff("OMS-WITHIN", "SettleDate").Delta.Days));

        Assert.Equal(["", "bonds", "equities", "swaps"], result.Entries.Select(entry => entry.PathId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("", see.Entry("OMS-FX").PathId);
        Assert.Equal((FieldOutcome.Break, false, true), (see.Diff("OMS-NOSETTLE", "SettleDate").Outcome, see.Diff("OMS-NOSETTLE", "SettleDate").Left.IsNull, see.Diff("OMS-NOSETTLE", "SettleDate").Right.IsNull));
        Assert.Equal(["P1", "P2", "P3", "P4"], see.CommittingPasses(), StringComparer.Ordinal);
        Assert.Equal((CultureOrigin.Run, CultureOrigin.Side), (result.Run.Reading["left"].Origin, result.Run.Reading["right"].Origin));
        Assert.Equal(scenario.Left.Count + scenario.Right.Count, result.Rows.Length);
    }

    // What the structures inputs add. Each line names the behavior the trades inputs do not reach.
    [Fact]
    public void The_structures_inputs_cover_every_case_they_are_there_for()
    {
        var scenario = GoldenStructures.Scenario;
        var result = GoldenResult.Run(scenario);
        var see = new Seen(result);

        // route.all, and match.sharedRef with its default key.
        Assert.All(result.Entries, entry => Assert.Equal("all", entry.PathId));
        Assert.Equal(("P1", "ACME · EX-1"), (see.Entry("L-ABS").Explanation.CommittedBy, see.Entry("L-ABS").Explanation.Trail[0].Key));

        // A tolerance with two bounds: inside the absolute one, inside the relative one only, and outside both.
        Assert.Equal(("AbsoluteOrRelative", FieldOutcome.WithinTolerance, 0.0004m), (see.Diff("L-ABS", "Price").Tolerance.Value, see.Diff("L-ABS", "Price").Outcome, see.Diff("L-ABS", "Price").Delta.Number));
        Assert.Equal((FieldOutcome.WithinTolerance, 0.15m), (see.Diff("L-REL", "Price").Outcome, see.Diff("L-REL", "Price").Delta.Number));
        Assert.Equal((FieldOutcome.Break, 0.02m), (see.Diff("L-PXB", "Price").Outcome, see.Diff("L-PXB", "Price").Delta.Number));

        // A tolerance with a condition, and a field that is not compared: NotCompared fields are left out of Diffs.
        Assert.Equal(FieldOutcome.WithinTolerance, see.Diff("L-EQ", "Fees").Outcome);
        Assert.Equal(FieldOutcome.Break, see.Diff("L-BD", "Fees").Outcome);
        Assert.DoesNotContain(see.Entry("L-EQ").Diffs, diff => diff.Field.Name is "Accrued");
        Assert.Equal((1.00m, 2.00m), (result.Canonical(see.Entry("L-EQ").Left[0])["Accrued"].Number, result.Canonical(see.Entry("L-EQ").Right[0])["Accrued"].Number));

        // The TradeRef veto: two shared references that differ keep the rows apart; two broker references do not.
        Assert.Equal((Alignment.LeftOnly, Alignment.RightOnly), (see.Entry("L-VETO").Alignment, see.Entry("R-VETO").Alignment));
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact, "P2"), (see.Entry("L-BRK").Alignment, see.Entry("L-BRK").Outcome, see.Entry("L-BRK").Explanation.CommittedBy));

        // Midpoint away from zero: 2.125 is 2.13.
        Assert.Equal((Alignment.Matched, 2.13m), (see.Entry("L-MID").Alignment, result.Canonical(see.Entry("L-MID").Left[0])["Quantity"].Number));

        // match.groupSum with the many rows on the right: OneToMany, with MembersDisagree on a field the group does not agree on.
        Assert.Equal((Alignment.Matched, Composition.OneToMany, "P3", 3), (see.Entry("L-BLK").Alignment, see.Entry("L-BLK").Composition, see.Entry("L-BLK").Explanation.CommittedBy, see.Entry("L-BLK").Right.Length));
        Assert.Contains("MembersDisagree", see.Codes("R-A1"), StringComparer.Ordinal);

        // A contested group and a search that ran out of nodes.
        Assert.Equal((Alignment.Ambiguous, "P3"), (see.Entry("L-TWIN").Alignment, see.Entry("L-TWIN").Explanation.CommittedBy));
        Assert.Equal("BudgetExhausted", see.Entry("L-BUD").Explanation.Trail[0].Detail);

        // A package on the right with summed amounts: OneToMany and Package, fees 1.00 + 2.00 and cash 100.00 + 150.00.
        Assert.Equal((Alignment.Matched, Composition.OneToMany | Composition.Package, FieldOutcome.Exact), (see.Entry("L-FX").Alignment, see.Entry("L-FX").Composition, see.Entry("L-FX").Outcome));
        Assert.Equal((3.00m, 250.00m), (result.Projection(see.Entry("L-FX").Id, Side.Right)["Fees"].Number, result.Projection(see.Entry("L-FX").Id, Side.Right)["NetCash"].Number));
        Assert.Contains("MembersDisagree", see.Codes("R-NEAR"), StringComparer.Ordinal);
        Assert.Contains("AggregateOverflow", see.Codes("R-ON"), StringComparer.Ordinal);
        Assert.Equal(["PackageInconsistent"], see.Codes("R-BN"), StringComparer.Ordinal);

        // A lifecycle where the correction points at the new event, and the three ways a chain fails.
        Assert.Equal((Alignment.Matched, Composition.OneToOne | Composition.LifecycleCollapsed), (see.Entry("L-LC").Alignment, see.Entry("L-LC").Composition));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded), see.Fate("L-LN"));
        Assert.Equal(["LifecycleCycle"], see.Codes("L-CY1"), StringComparer.Ordinal);
        Assert.Equal(["LifecycleConflict"], see.Codes("L-K1"), StringComparer.Ordinal);
        Assert.Equal(["UndeterminedSide"], see.Codes("L-Q0"), StringComparer.Ordinal);
        Assert.Equal(["ChainMemberQuarantined"], see.Codes("L-Q1"), StringComparer.Ordinal);

        // Invalid input the trades inputs do not have.
        Assert.Equal(["SideSignConflict"], see.Codes("R-SIGN"), StringComparer.Ordinal);
        Assert.Equal(["UnknownSymbol"], see.Codes("R-WHO"), StringComparer.Ordinal);
        Assert.Equal(["FloatValue"], see.Codes("R-FLOAT"), StringComparer.Ordinal);
        Assert.All(new[] { "L-CY1", "L-CY2", "L-K0", "L-K1", "L-K2", "L-Q0", "L-Q1", "R-BN", "R-BF", "R-SIGN", "R-WHO", "R-FLOAT" }, rowKey => see.Quarantined(rowKey));

        Assert.Equal(["P1", "P2", "P3"], see.CommittingPasses(), StringComparer.Ordinal);
        Assert.Equal(scenario.Left.Count + scenario.Right.Count, result.Rows.Length);
    }

    [Fact]
    public void A_changed_result_fails_with_the_first_differing_lines_and_never_rewrites_the_golden_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Goldens", "proof");
        Directory.CreateDirectory(folder);
        var goldenPath = Path.Combine(folder, "sample.txt");
        var actualPath = Path.Combine(folder, "sample.actual.txt");
        string[] golden = ["[summary]", "Matched=3", "Break=1", "[rows]", "row left:A#0 disposition=Matched"];
        string[] changed = ["[summary]", "Matched=3", "Break=2", "[rows]", "row left:A#0 disposition=LeftOnly", "row left:B#1 disposition=LeftOnly"];
        File.WriteAllLines(goldenPath, golden);
        var committed = File.ReadAllText(goldenPath);
        File.Delete(actualPath);

        var failure = GoldenText.Check(changed, goldenPath, actualPath);

        Assert.NotNull(failure);
        Assert.Contains("3 lines differ, of 5 in the golden file and 6 now.", failure, StringComparison.Ordinal);
        Assert.Contains("line 3\n  golden: Break=1\n  actual: Break=2", failure, StringComparison.Ordinal);
        Assert.Contains("line 5\n  golden: row left:A#0 disposition=Matched\n  actual: row left:A#0 disposition=LeftOnly", failure, StringComparison.Ordinal);
        Assert.Contains("line 6\n  golden: <no line>\n  actual: row left:B#1 disposition=LeftOnly", failure, StringComparison.Ordinal);
        Assert.Contains(goldenPath, failure, StringComparison.Ordinal);
        Assert.Contains(actualPath, failure, StringComparison.Ordinal);
        Assert.Contains("Nothing rewrites the golden file for you.", failure, StringComparison.Ordinal);
        Assert.Equal(committed, File.ReadAllText(goldenPath));
        Assert.Equal(changed, GoldenText.Read(actualPath));
        Assert.Null(GoldenText.Check(golden, goldenPath, actualPath));
    }

    [Fact]
    public void A_missing_golden_file_fails_and_is_not_created()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Goldens", "proof");
        Directory.CreateDirectory(folder);
        var goldenPath = Path.Combine(folder, "absent.txt");
        File.Delete(goldenPath);

        var failure = GoldenText.Check(["[summary]"], goldenPath, Path.Combine(folder, "absent.actual.txt"));

        Assert.NotNull(failure);
        Assert.Contains("is missing", failure, StringComparison.Ordinal);
        Assert.False(File.Exists(goldenPath));
    }

    private static GoldenScenario Scenario(string name) =>
        name switch
        {
            "trades" => GoldenTrades.Scenario,
            "structures" => GoldenStructures.Scenario,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "There is no such golden scenario."),
        };

    private static string GoldenPath(GoldenScenario scenario) =>
        Path.Combine(ProfileCorpus.Source, "RecDiffy.Tests", "Goldens", $"golden-{scenario.Name}.txt");

    private static string ActualPath(GoldenScenario scenario, string run) =>
        Path.Combine(AppContext.BaseDirectory, "Goldens", $"golden-{scenario.Name}.{run}.actual.txt");

    private static (Alignment Alignment, Composition Composition, FieldOutcome Outcome, string? CommittedBy) Shape(ResultEntry entry) =>
        (entry.Alignment, entry.Composition, entry.Outcome, entry.Explanation.CommittedBy);

    // A Fisher-Yates shuffle with a fixed seed: position i of the shuffled input holds row Shuffled[i] of the fixed input.
    private static int[] Shuffled(int count, int seed)
    {
        var order = Enumerable.Range(0, count).ToArray();
        var random = new Random(seed);
        for (var index = order.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }

        return order;
    }

    private sealed class Seen(IReconciliationResult result)
    {
        public ResultEntry Entry(string rowKey) =>
            result.Entries.Single(entry => entry.Left.Concat(entry.Right).Any(member => member.RowKey == rowKey));

        public RowResult[] Rows(string rowKey) => [.. result.Rows.Where(row => row.Row.RowKey == rowKey)];

        public string[] Codes(string rowKey) =>
            [.. Rows(rowKey).SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        public FieldDiff Diff(string rowKey, string field) => Entry(rowKey).Diffs.Single(diff => diff.Field.Name == field);

        public (Disposition Disposition, SuppressionReason? Suppression) Fate(string rowKey) => (Rows(rowKey)[0].Disposition, Rows(rowKey)[0].Suppression);

        public void Quarantined(string rowKey) =>
            Assert.All(Rows(rowKey), row => Assert.Equal((Disposition.Quarantined, InputValidity.Invalid, (GroupId?)null), (row.Disposition, row.Validity, row.EntryId)));

        public IEnumerable<string> CommittingPasses() =>
            result.Entries.Where(entry => entry.Alignment is Alignment.Matched).Select(entry => entry.Explanation.CommittedBy).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    }
}

internal sealed record GoldenScenario(string Name, string Profile, ReferenceDataSet Reference, GoldenSide Left, GoldenSide Right, IReadOnlyList<GoldenView> Views)
{
    public CompiledProfile Compile() =>
        Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore()));
}

// Run builds the side's source with its rows in the given order, and hands it to the run. A null order is the fixed order.
internal sealed record GoldenSide(IReadOnlyList<string> Columns, int Count, Func<IReadOnlyList<int>?, Func<RecordSource, IReconciliationResult>, IReconciliationResult> Run);

internal sealed record GoldenView(string Title, SideBySideSpec Spec);

internal static class GoldenResult
{
    // A name no profile declares and no source has, to pin what the engine returns for one.
    private const string Unknown = "NotDeclared";

    public static IReconciliationResult Run(GoldenScenario scenario, IReadOnlyList<int>? left = null, IReadOnlyList<int>? right = null, RunOptions? options = null) =>
        scenario.Left.Run(left, leftSource => scenario.Right.Run(right, rightSource => Reconciler.Run(scenario.Compile(), leftSource, rightSource, scenario.Reference, options)));

    // left and right say which fixed row each input position holds, so a row reads the same wherever it was fed in.
    // anyOrder puts the three orders that follow the input into a fixed order; it changes nothing else.
    public static string Text(GoldenScenario scenario, IReconciliationResult result, bool anyOrder = false, IReadOnlyList<int>? left = null, IReadOnlyList<int>? right = null) =>
        string.Join('\n', new Writer(scenario, result, anyOrder, left, right).Lines());

    private sealed class Writer(GoldenScenario scenario, IReconciliationResult result, bool anyOrder, IReadOnlyList<int>? left, IReadOnlyList<int>? right)
    {
        private readonly string[] _fields = [.. scenario.Compile().Document.Fields.Select(field => field.Name), Unknown];

        private readonly HashSet<(string Path, string Pass)> _groupPasses =
            [.. scenario.Compile().Document.Paths.SelectMany(path => path.Passes.Where(pass => pass.Pass is "match.groupSum").Select(pass => (path.Id, pass.Id)))];

        public IEnumerable<string> Lines() =>
        [
            $"# What the engine returns for the fixed {scenario.Name} inputs of GoldenResultsTests, in the order the engine returns it:",
            "# every entry, member, diff, explanation, row, diagnostic, raw cell, canonical value and trace step, the summary,",
            "# and the side-by-side views. A value is written with every part it has, and with what Format and Text() make of it.",
            "# Not here: the engine version, which changes with every build, and the interned ids inside a canonical value.",
            "# One order is not the engine's: the entries one group pass leaves ambiguous or search-incomplete on one route come out",
            "# in a different order in every process, so they are written sorted by their members. See GroupResidueOrderTests.",
            "# GoldenResultsTests compares the engine with this file on net8.0 and net10.0, and fails on any difference.",
            "# To accept an intended change, copy the .actual.txt file the failing test names over this file and commit it.",
            "",
            "[run]",
            $"profileHash={result.Run.ProfileHash}",
            $"reference={result.Run.ReferenceId} hash={Quote(result.Run.ReferenceHash)}",
            .. result.Run.Reading.OrderBy(side => side.Key, StringComparer.Ordinal).Select(side =>
                $"reading {side.Key}: culture={Quote(side.Value.Culture)} decimal={Quote(side.Value.DecimalSeparator)} group={Quote(side.Value.GroupSeparator)} origin={side.Value.Origin.ToInvariant()} strictDateFormats={Flag(side.Value.StrictDateFormats)}"),
            "",
            "[summary]",
            $"Matched={result.Summary.Matched.ToInvariant()} Exact={result.Summary.Exact.ToInvariant()} WithinTolerance={result.Summary.WithinTolerance.ToInvariant()} Break={result.Summary.Break.ToInvariant()}",
            $"LeftOnly={result.Summary.LeftOnly.ToInvariant()} RightOnly={result.Summary.RightOnly.ToInvariant()} Ambiguous={result.Summary.Ambiguous.ToInvariant()} SearchIncomplete={result.Summary.SearchIncomplete.ToInvariant()}",
            $"Quarantined={result.Summary.Quarantined.ToInvariant()} Suppressed={result.Summary.Suppressed.ToInvariant()}",
            $"entries={result.Entries.Length.ToInvariant()} rows={result.Rows.Length.ToInvariant()}",
            "",
            "[entries]",
            .. Entries().SelectMany(Entry),
            "[rows]",
            .. Rows().SelectMany(Row),
            .. scenario.Views.SelectMany(View),
        ];

        // The entries one group pass leaves ambiguous or search-incomplete on one route come out in a different order
        // from one process to the next: GroupIndex.Residue lists a dictionary keyed by text, and text hashes differ in
        // every process (#426, GroupResidueOrderTests). So that one run is always written sorted by its members.
        // Unmatched entries of one alignment on one route come in input order, so anyOrder sorts each such run too.
        private IEnumerable<ResultEntry> Entries()
        {
            // Remove this sort when #426 is fixed: it hides that order, the fix included. GroupResidueOrderTests then covers it.
            var stable = Sorted(result.Entries, GroupResidue);
            return anyOrder ? Sorted(stable, SameUnmatchedKind) : stable;
        }

        private IEnumerable<ResultEntry> Sorted(IEnumerable<ResultEntry> entries, Func<ResultEntry, ResultEntry, bool> together) =>
            Runs(entries, together).SelectMany(run => run.OrderBy(entry => Refs(Sorted(entry.Left.Concat(entry.Right))), StringComparer.Ordinal));

        private static IEnumerable<IReadOnlyList<ResultEntry>> Runs(IEnumerable<ResultEntry> entries, Func<ResultEntry, ResultEntry, bool> together)
        {
            var run = new List<ResultEntry>();
            foreach (var entry in entries)
            {
                if (run is [.., var last] && together(last, entry) is false)
                {
                    yield return run;
                    run = [];
                }

                run.Add(entry);
            }

            if (run.Count > 0)
                yield return run;
        }

        private static bool SameUnmatchedKind(ResultEntry first, ResultEntry second) =>
            first.Alignment is not Alignment.Matched && first.Alignment == second.Alignment && first.PathId == second.PathId;

        private bool GroupResidue(ResultEntry first, ResultEntry second) =>
            first.Alignment is Alignment.Ambiguous or Alignment.SearchIncomplete
            && second.Alignment is Alignment.Ambiguous or Alignment.SearchIncomplete
            && first.PathId == second.PathId
            && first.Explanation.CommittedBy is { } pass
            && pass == second.Explanation.CommittedBy
            && _groupPasses.Contains((first.PathId, pass));

        private IEnumerable<RowResult> Rows()
        {
            var byRef = result.Rows.ToDictionary(row => row.Row);
            return anyOrder ? Sorted(result.Rows.Select(row => row.Row)).Select(row => byRef[row]) : result.Rows;
        }

        // The members of an unmatched entry come in input order, so anyOrder sorts them. A matched entry's members keep
        // the engine's order, except that the copies of one row key, which come in input order, are put in position order.
        private IEnumerable<SourceRef> Members(IEnumerable<SourceRef> members, bool matched) =>
            (anyOrder, matched) switch
            {
                (false, _) => members,
                (true, false) => Sorted(members),
                (true, true) => members.GroupBy(member => (member.Source, member.RowKey)).SelectMany(copies => copies.OrderBy(Position)),
            };

        private IEnumerable<SourceRef> Sorted(IEnumerable<SourceRef> rows) =>
            rows.OrderBy(row => row.Source, StringComparer.Ordinal).ThenBy(row => row.RowKey, StringComparer.Ordinal).ThenBy(Position);

        private int Position(SourceRef row) => Order(row) is { } order ? order[row.Ordinal] : row.Ordinal;

        private IReadOnlyList<int>? Order(SourceRef row) => row.Source is "left" ? left : right;

        private string Ref(SourceRef row) => $"{row.Source}:{(row.RowKey.Length is 0 ? "<no key>" : row.RowKey)}#{Position(row).ToInvariant()}";

        private string Refs(IEnumerable<SourceRef> rows) => $"[{string.Join(", ", rows.Select(Ref))}]";

        private IEnumerable<string> Entry(ResultEntry entry)
        {
            var matched = entry.Alignment is Alignment.Matched;
            return
            [
                $"entry {entry.Id.Value} path={Quote(entry.PathId)} alignment={entry.Alignment.ToInvariant()} composition={entry.Composition.ToInvariant()} outcome={entry.Outcome.ToInvariant()}",
                $"  left={Refs(Members(entry.Left, matched))}",
                $"  right={Refs(Members(entry.Right, matched))}",
                .. entry.Diffs.SelectMany(diff => (IEnumerable<string>)
                [
                    $"  diff {diff.Field.Name}: outcome={diff.Outcome.ToInvariant()} delta={diff.Delta.Kind.ToInvariant()}/{diff.Delta.Number.ToInvariant()}/{diff.Delta.Days.ToInvariant()} tolerance={Quote(diff.Tolerance.Value)} attribution={Quote(diff.Attribution.Value)} attributedTo=[{string.Join(", ", diff.AttributedTo.IsDefault ? [] : diff.AttributedTo)}]",
                    $"    left: {Value(diff.Left)}",
                    $"    right: {Value(diff.Right)}",
                ]),
                $"  committedBy={entry.Explanation.CommittedBy ?? "<none>"}",
                .. entry.Explanation.Trail.Select(attempt =>
                    $"  trail {attempt.PassId}: verdict={attempt.Verdict.ToInvariant()} key={Quote(attempt.Key)} candidates={Refs(Members(attempt.Candidates, matched))} detail={Quote(attempt.Detail)}"),
                $"  hints={entry.Explanation.Hints.Count.ToInvariant()}",
                .. entry.Explanation.Hints.Select(hint => $"  hint {hint.Code}: {hint.Detail}"),
                .. matched
                    ? [.. Canonical("  projection left", result.Projection(entry.Id, Side.Left)), .. Canonical("  projection right", result.Projection(entry.Id, Side.Right))]
                    : Array.Empty<string>(),
                "",
            ];
        }

        private IEnumerable<string> Row(RowResult row) =>
        [
            $"row {Ref(row.Row)} disposition={row.Disposition.ToInvariant()} suppression={row.Suppression?.ToInvariant() ?? "<none>"} validity={row.Validity.ToInvariant()} entry={row.EntryId?.Value ?? "<none>"}",
            .. row.Diagnostics.Select(diagnostic =>
                $"  diagnostic {diagnostic.Code}: severity={diagnostic.Severity.ToInvariant()} rule={diagnostic.RuleId} field={Quote(diagnostic.Field)} raw={Quote(diagnostic.RawText)}"),
            $"  raw: {string.Join(" ", Columns(row.Row).Select(column => $"{column}={Quote(result.Raw(row.Row, column))}"))}",
            .. Canonical("  canonical", result.Canonical(row.Row)),
            .. result.Trace(row.Row).Select(step => $"  trace {step.RuleId}: {step.Field} {Quote(step.Raw)} -> {Quote(step.Canonical)}"),
            "",
        ];

        private IEnumerable<string> Columns(SourceRef row) =>
            [.. (row.Source is "left" ? scenario.Left : scenario.Right).Columns, Unknown];

        // Every declared field, null or not, and one name the profile does not declare: the value the row hands out, what
        // Format makes of it, and the row's own Text and IsNull.
        private IEnumerable<string> Canonical(string title, CanonicalRow row) =>
        [
            $"{title}:",
            .. _fields.Select(field => $"    {field}: {Value(row[field])} Text()={Quote(row.Text(field))} IsNull()={Flag(row.IsNull(field))}"),
        ];

        private string Value(CanonicalValue value) =>
            $"type={value.Type.ToInvariant()} null={Flag(value.IsNull)} number={value.Number.ToInvariant()} date={value.Date.ToInvariant("yyyy-MM-dd")} text={Quote(value.Text)} Format={Quote(result.Format(in value))}";

        // A line is one row label and column label. Its cells are a dictionary, so they are written by measure name.
        private IEnumerable<string> View(GoldenView view) =>
        [
            $"[side by side: {view.Title}] rows={string.Join(",", view.Spec.Rows)} columns={string.Join(",", view.Spec.Columns)} measures={string.Join(",", view.Spec.Measures.Select(measure => measure.Field))} filter={(view.Spec.Filter is { } filter ? $"{filter.Field}={Quote(filter.EqualsValue)}" : "<none>")}",
            .. result.BuildSideBySide(view.Spec).Lines.SelectMany(line => line.Cells.OrderBy(cell => cell.Key, StringComparer.Ordinal).Select(cell =>
                $"{Quote(line.Row)} | {Quote(line.Column)} | {cell.Key}: left={cell.Value.Left.ToInvariant()} right={cell.Value.Right.ToInvariant()} delta={cell.Value.Delta.ToInvariant()} leftOnly={cell.Value.LeftOnly.ToInvariant()} rightOnly={cell.Value.RightOnly.ToInvariant()} matchedDelta={cell.Value.MatchedDelta.ToInvariant()}")),
            "",
        ];

        private static string Quote(string? text) => text is null ? "<null>" : $"\"{text}\"";

        private static string Flag(bool value) => value ? "true" : "false";
    }
}

// Compares text with a committed golden file, line by line. The golden file is only ever read.
internal static class GoldenText
{
    private const int ListedLines = 12;

    public static IReadOnlyList<string> Lines(string text) => Content(text.Split('\n'));

    public static IReadOnlyList<string> Read(string path) => Content(File.ReadAllLines(path));

    // Null when the lines equal the golden file. Otherwise the message a failing test shows.
    public static string? Check(IReadOnlyList<string> actual, string goldenPath, string actualPath)
    {
        var golden = File.Exists(goldenPath) ? Read(goldenPath) : null;
        var difference = golden is null ? null : Difference(golden, actual);
        if (golden is not null && difference is null)
            return null;
        Directory.CreateDirectory(Path.GetDirectoryName(actualPath) ?? ".");
        File.WriteAllText(actualPath, string.Join('\n', [.. actual, ""]));
        var accept = string.Join('\n',
            "Accept it by hand: review the text this run wrote to",
            $"  {actualPath}",
            "copy that file over",
            $"  {goldenPath}",
            "and commit the golden file with the change. Nothing rewrites the golden file for you.");
        return golden is null
            ? $"The golden file {goldenPath} is missing.\n{accept}"
            : $"The engine's result differs from the golden file.\n{difference}\nCompare the two files below to see every difference. If the change is not intended, the engine's behavior has moved: find what moved it. If it is intended:\n{accept}";
    }

    // The first lines that differ, by position. Null when the two are the same.
    public static string? Difference(IReadOnlyList<string> golden, IReadOnlyList<string> actual)
    {
        int[] differing = [.. Enumerable.Range(0, Math.Max(golden.Count, actual.Count)).Where(index => At(golden, index) != At(actual, index))];
        if (differing.Length is 0)
            return null;
        var differ = differing.Length is 1 ? " differs" : "s differ";
        return $"{differing.Length.ToInvariant()} line{differ}, of {golden.Count.ToInvariant()} in the golden file and {actual.Count.ToInvariant()} now. The first {Math.Min(differing.Length, ListedLines).ToInvariant()}:"
            + string.Concat(differing.Take(ListedLines).Select(index =>
                $"\nline {(index + 1).ToInvariant()}\n  golden: {At(golden, index) ?? "<no line>"}\n  actual: {At(actual, index) ?? "<no line>"}"));
    }

    private static string? At(IReadOnlyList<string> lines, int index) => index < lines.Count ? lines[index] : null;

    // Every line counts, comments too, so the file a person copies over the golden file is the whole file. Line endings
    // and blank lines at the end do not count.
    private static string[] Content(IEnumerable<string> lines)
    {
        string[] trimmed = [.. lines.Select(line => line.TrimEnd('\r'))];
        return trimmed[..(Array.FindLastIndex(trimmed, line => line.Length > 0) + 1)];
    }
}
