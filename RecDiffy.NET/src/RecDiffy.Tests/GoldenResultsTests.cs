using System.Data;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// What the engine returns for one fixed pair of inputs, as text, compared with Goldens/golden-results.txt. Any change
// in the result fails here, on net8.0 and net10.0: entries, diffs, rows, diagnostics, the summary, explanations, the
// canonical values and trace of every row, and one side-by-side view (spec v3.1 §10.1).
//
// Accepting an intended change is a manual step: copy the .actual.txt file the failure names over the golden file and
// commit it. Nothing rewrites the golden file.
public class GoldenResultsTests
{
    private static readonly int[] Seeds = [1, 2, 3, 5, 8, 13, 21, 34];

    private static string GoldenPath => Path.Combine(ProfileCorpus.Source, "RecDiffy.Tests", "Goldens", "golden-results.txt");

    private static string ActualPath(string name) => Path.Combine(AppContext.BaseDirectory, "Goldens", $"golden-results.{name}.actual.txt");

    [Fact]
    public void The_result_matches_the_golden_file()
    {
        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(GoldenResult.Run())), GoldenPath, ActualPath("run"));

        Assert.True(failure is null, failure);
    }

    // Spec v3.1 §10.1: the same rows in any order give the same result. A SourceRef carries the row's position in its
    // input, so each shuffled position is mapped back to the position the golden file knows. Three things follow the
    // input order, and the golden file keeps them in its last section, which is left out here: the order of the
    // unmatched entries, the order of the members inside an entry, and the order of the rows. §10.1 allows that: commit
    // order "affects output order only", and output is compared as sets.
    [Fact]
    public void The_result_is_the_same_for_rows_in_any_order()
    {
        var expected = AnyOrder(GoldenText.Read(GoldenPath));
        var shuffles = Seeds.Select(seed => (Name: $"seed {seed.ToString(CultureInfo.InvariantCulture)}", Left: Shuffled(GoldenInputs.Left.Length, seed), Right: Shuffled(GoldenInputs.Right.Length, seed + 1000)))
            .Append((Name: "reversed", Left: [.. Enumerable.Range(0, GoldenInputs.Left.Length).Reverse()], Right: [.. Enumerable.Range(0, GoldenInputs.Right.Length).Reverse()]));

        string[] failures =
        [
            .. shuffles
                .Select(shuffle => (shuffle.Name, Lines: AnyOrder(GoldenText.Lines(GoldenResult.Text(GoldenResult.Run(shuffle.Left, shuffle.Right), shuffle.Left, shuffle.Right)))))
                .Select(run => GoldenText.Difference(expected, run.Lines) is { } difference ? $"Rows shuffled with {run.Name} give a different result:\n{difference}" : null)
                .OfType<string>(),
        ];

        Assert.True(failures.Length == 0, string.Join("\n\n", failures));
    }

    // The profile declares its cultures, so the machine's culture changes nothing (spec v3.1 §3.5).
    [Theory]
    [InlineData("")]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void The_result_is_the_same_under_any_machine_culture(string machine)
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(machine);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(machine);

            var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(GoldenResult.Run())), GoldenPath, ActualPath("culture"));

            Assert.True(failure is null, failure);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    // RunOptions: "The value does not change the result."
    [Fact]
    public void The_result_is_the_same_when_result_building_runs_in_parallel()
    {
        var result = GoldenResult.Run(options: new RunOptions(MaxDegreeOfParallelism: 4));

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(result)), GoldenPath, ActualPath("parallel"));

        Assert.True(failure is null, failure);
    }

    [Fact]
    public async Task The_result_is_the_same_from_RunAsync()
    {
        var result = await Reconciler.RunAsync(GoldenInputs.Compile(), GoldenInputs.LeftSource(), GoldenInputs.RightSource(), GoldenInputs.Reference);

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(result)), GoldenPath, ActualPath("async"));

        Assert.True(failure is null, failure);
    }

    // The same cells through the data reader source: the second way rows reach intake.
    [Fact]
    public void The_result_is_the_same_when_the_rows_come_from_a_data_reader()
    {
        using var left = GoldenInputs.LeftTable();
        using var right = GoldenInputs.RightTable();
        var result = Reconciler.Run(
            GoldenInputs.Compile(),
            RecordSources.FromDataReader("left", left.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", right.CreateDataReader, new TabularOptions()),
            GoldenInputs.Reference);

        var failure = GoldenText.Check(GoldenText.Lines(GoldenResult.Text(result)), GoldenPath, ActualPath("reader"));

        Assert.True(failure is null, failure);
    }

    // What the inputs are there to cover. If an edit to the inputs drops a case, this says which.
    [Fact]
    public void The_inputs_cover_every_case_the_golden_file_is_there_for()
    {
        var result = GoldenResult.Run();
        ResultEntry Entry(string rowKey) => result.Entries.Single(entry => entry.Left.Concat(entry.Right).Any(member => member.RowKey == rowKey));
        RowResult[] Rows(string rowKey) => [.. result.Rows.Where(row => row.Row.RowKey == rowKey)];
        string[] Codes(string rowKey) => [.. Rows(rowKey).SelectMany(row => row.Diagnostics).Select(diagnostic => diagnostic.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Exact, "P1"), Shape(Entry("OMS-EXACT")));
        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.WithinTolerance, "P2"), Shape(Entry("OMS-WITHIN")));
        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Break, "P1"), Shape(Entry("OMS-BREAK")));
        Assert.Equal(Alignment.LeftOnly, Entry("OMS-ORPHAN").Alignment);
        Assert.Equal(Alignment.RightOnly, Entry("ST-ALLEGED").Alignment);
        Assert.Equal((Alignment.Ambiguous, 2, 1), (Entry("ST-AMB").Alignment, Entry("ST-AMB").Left.Length, Entry("ST-AMB").Right.Length));
        Assert.Equal(Alignment.SearchIncomplete, Entry("ST-BIG").Alignment);

        Assert.Equal(["DecimalParse"], Codes("OMS-BADQTY"), StringComparer.Ordinal);
        Assert.Equal(["PriceParse"], Codes("ST-BADPX"), StringComparer.Ordinal);
        Assert.Equal(["DuplicateConflict"], Codes("OMS-CONFLICT"), StringComparer.Ordinal);
        Assert.Equal(["DuplicateRowCollapsed"], Codes("OMS-TWICE"), StringComparer.Ordinal);
        Assert.All(new[] { "OMS-BADQTY", "ST-BADPX", "OMS-CONFLICT", "OMS-HALF-PAY", "ST-LOST-CORR", "ST-NOPARTY", "OMS-NODATE" }, rowKey =>
            Assert.All(Rows(rowKey), row => Assert.Equal((Disposition.Quarantined, InputValidity.Invalid, (GroupId?)null), (row.Disposition, row.Validity, row.EntryId))));
        Assert.Equal(2, Rows("OMS-CONFLICT").Length);
        Assert.Equal(2, Rows("OMS-TWICE").Length);
        Assert.All(Rows("OMS-TWICE"), row => Assert.Equal((Disposition.Matched, InputValidity.ValidWithWarnings), (row.Disposition, row.Validity)));
        Assert.Contains(result.Rows, row => row.Disposition is Disposition.Quarantined && row.Diagnostics.Any(diagnostic => diagnostic.Code == "MissingKey"));

        Assert.Equal((Alignment.Matched, Composition.OneToOne | Composition.LifecycleCollapsed, FieldOutcome.Exact, "P1"), Shape(Entry("OMS-LIFE")));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded), (Rows("ST-LIFE-NEW")[0].Disposition, Rows("ST-LIFE-NEW")[0].Suppression));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.CancelEvent), (Rows("ST-LIFE-CANC")[0].Disposition, Rows("ST-LIFE-CANC")[0].Suppression));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.FullyCancelled), (Rows("ST-GONE-NEW")[0].Disposition, Rows("ST-GONE-NEW")[0].Suppression));

        Assert.Equal((Alignment.Matched, Composition.ManyToOne | Composition.Package, FieldOutcome.Exact, "P1"), Shape(Entry("OMS-LEG-PAY")));
        Assert.Equal((Alignment.Matched, Composition.ManyToOne, FieldOutcome.Exact, "P3"), Shape(Entry("ST-BLOCK")));
        Assert.Equal(3, Entry("ST-BLOCK").Left.Length);

        var fill = Entry("OMS-FILL");
        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Break, "P4"), Shape(fill));
        Assert.Equal(["Quantity"], fill.Diffs.Single(diff => diff.Field.Name == "Accrued").AttributedTo, StringComparer.Ordinal);
        Assert.Equal(["Quantity", "Accrued"], fill.Diffs.Single(diff => diff.Field.Name == "NetCash").AttributedTo, StringComparer.Ordinal);
        Assert.Equal(["Accrued"], Entry("OMS-WITHIN").Diffs.Single(diff => diff.Field.Name == "NetCash").AttributedTo, StringComparer.Ordinal);

        Assert.Equal(["", "bonds", "equities", "swaps"], result.Entries.Select(entry => entry.PathId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("", Entry("OMS-FX").PathId);

        var settle = Entry("OMS-NOSETTLE").Diffs.Single(diff => diff.Field.Name == "SettleDate");
        Assert.Equal((FieldOutcome.Break, false, true), (settle.Outcome, settle.Left.IsNull, settle.Right.IsNull));

        Assert.Equal(["P1", "P2", "P3", "P4"], result.Entries.Where(entry => entry.Alignment is Alignment.Matched).Select(entry => entry.Explanation.CommittedBy).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(GoldenInputs.Left.Length + GoldenInputs.Right.Length, result.Rows.Length);
    }

    // The comparison itself: a changed line fails with the first lines that differ, the failure says how to accept, the
    // actual text is written beside the test output, and the golden file is left as it was.
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

    private static (Alignment Alignment, Composition Composition, FieldOutcome Outcome, string? CommittedBy) Shape(ResultEntry entry) =>
        (entry.Alignment, entry.Composition, entry.Outcome, entry.Explanation.CommittedBy);

    private static IReadOnlyList<string> AnyOrder(IReadOnlyList<string> lines) =>
        GoldenText.Lines(string.Join('\n', lines.TakeWhile(line => line != GoldenResult.InputOrderSection)));

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
}

// The fixed inputs: an internal blotter on the left, read as en-US, and a street report on the right, read as de-DE with
// dates in each counterparty's format. Core rules and the trading pack normalize them, and three routes match them.
internal static class GoldenInputs
{
    public const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "golden-results",
          "version": "1.0.0",
          "rounding": { "midpoint": "ToEven" },
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Trade_Id" }, "right": { "rowKey": "Report_Id", "culture": "de-DE" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Uti", "type": "Identifier", "role": "Identity" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "identifier.map", "from": "Uti_Ref", "to": "Uti", "scheme": "UTI" },
                { "rule": "text.copy", "from": "Parent", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Leg", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Dt", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "Settle_Dt", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ],
              "validate": [ { "rule": "required.present", "fields": ["Counterparty", "TradeDate"] } ]
            },
            "right": {
              "identify": [
                { "rule": "party.resolveLei", "from": "Lei", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "symbol.map", "from": "Product", "to": "AssetClass", "map": { "BD": "Bond", "EQ": "Equity", "SW": "Swap" } },
                { "rule": "identifier.map", "from": "Uti_Ref", "to": "Uti", "scheme": "UTI" },
                { "rule": "symbol.map", "from": "Event", "to": "EventAction" },
                { "rule": "text.copy", "from": "Prior", "to": "EventTarget" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Dt", "to": "TradeDate", "formatBy": "Counterparty" },
                { "rule": "date.parse", "from": "Settle_Dt", "to": "SettleDate", "formatBy": "Counterparty" },
                { "rule": "price.from32nds", "from": "Px", "to": "Price", "when": { "field": "Conv", "equals": "32NDS" } },
                { "rule": "decimal.parse", "from": "Px", "to": "Price", "when": { "field": "Conv", "notEquals": "32NDS" } },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty", "invertWhen": "DEALER", "perspective": "View" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ],
              "validate": [ { "rule": "required.present", "fields": ["Counterparty", "TradeDate"] } ]
            }
          },
          "consolidation": {
            "left": { "packages": [ {
              "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"],
              "aggregate": { "Quantity": "Same", "Price": "FromRole(PAY)", "Side": "FromRole(PAY)", "NetCash": "DifferenceByRole(PAY,REC)", "Fees": "Sum" }
            } ] },
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
          },
          "paths": [
            {
              "id": "bonds",
              "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Bond" },
              "passes": [
                { "id": "P1", "pass": "match.sharedRef", "key": ["Uti"] },
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
                { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
              ],
              "diff": { "fields": {
                "Quantity": { "tolerance": { "relative": 0.0001 } },
                "Accrued": { "tolerance": { "absolute": 1.25 }, "attributed": "trading.proRataToQuantity", "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
                "NetCash": { "tolerance": { "attributed": "trading.netCash" } },
                "SettleDate": { "tolerance": { "days": 1 } }
              } }
            },
            {
              "id": "swaps",
              "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Swap" },
              "passes": [
                { "id": "P1", "pass": "match.sharedRef", "key": ["Uti"] },
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] }
              ],
              "diff": { "fields": { "NetCash": { "tolerance": { "absolute": 0.05 } } } }
            },
            {
              "id": "equities",
              "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Equity" },
              "passes": [
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] }
              ]
            }
          ],
          "limits": { "maxBlockItems": 4 }
        }
        """;

    private const string Alfa = "5493000000000000ALFA";
    private const string Brav = "5493000000000000BRAV";
    private const string Char = "5493000000000000CHAR";

    public static readonly ReferenceDataSet Reference = ReferenceDataSet.Create(
        "golden-reference",
        new DateOnly(2026, 3, 6),
        "golden-reference-hash",
        new Dictionary<string, string>(StringComparer.Ordinal) { [Alfa] = "ALFA", [Brav] = "BRAV", [Char] = "CHAR" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Alfa Bank"] = "ALFA", ["Bravo Sec"] = "BRAV", ["Charlie Capital"] = "CHAR" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["ALFA"] = "yyyy-MM-dd", ["BRAV"] = "dd/MM/yyyy", ["CHAR"] = "MM/dd/yyyy" });

    public static readonly Internal[] Left =
    [
        // An exact match on the shared reference. The street quotes the price in 32nds and reports from the dealer's side.
        new("OMS-EXACT", "Alfa Bank", "US0000000001", "Bond", "2026-03-02", "B", "3,000,000.00", "96.078125", "12,500.00", "150.00", "2,894,993.75", "2026-03-04", "UTI-EXACT"),
        // Within tolerance on the natural key: accrued is 0.94 lower, net cash follows it, and settlement is a day later.
        new("OMS-WITHIN", "Bravo Sec", "US0000000002", "Bond", "2026-03-02", "S", "-2,000,000", "101.5", "8,333.33", "100.00", "2,038,233.33", "2026-03-04"),
        // A break on the shared reference: the price differs, and net cash is attributed to it.
        new("OMS-BREAK", "Alfa Bank", "US0000000003", "Bond", "2026-03-03", "B", "1,000,000", "96.572", "5,000.00", "50.00", "970,770.00", "2026-03-05", "UTI-BREAK"),
        // Left only, twice on one route.
        new("OMS-ORPHAN", "Charlie Capital", "US0000000004", "Bond", "2026-03-03", "S", "500,000", "100", null, null, "500,000.00", "2026-03-05"),
        new("OMS-ORPHAN-2", "Bravo Sec", "US0000000019", "Bond", "2026-03-05", "B", "250,000", "101", null, null, "252,500.00", "2026-03-09"),
        // An ambiguous key: two identical trades against one report.
        new("OMS-AMB-1", "Bravo Sec", "US0000000006", "Equity", "2026-03-02", "B", "2,200", "114", null, null, "250,800.00"),
        new("OMS-AMB-2", "Bravo Sec", "US0000000006", "Equity", "2026-03-02", "B", "2,200", "114", null, null, "250,800.00"),
        // Invalid input: a quantity that is not a number, a row with no key, and a row with no trade date.
        new("OMS-BADQTY", "Alfa Bank", "US0000000007", "Bond", "2026-03-02", "B", "abc", "100", null, null, "1,000.00"),
        new(null, "Alfa Bank", "US0000000007", "Bond", "2026-03-02", "B", "1,000", "100", null, null, "1,000.00"),
        new("OMS-NODATE", "Alfa Bank", "US0000000007", "Bond", null, "B", "1,000", "100", null, null, "1,000.00"),
        // A conflicting duplicate: one key, two quantities.
        new("OMS-CONFLICT", "Charlie Capital", "US0000000013", "Equity", "2026-03-04", "B", "100", "10", null, null, "1,000.00"),
        new("OMS-CONFLICT", "Charlie Capital", "US0000000013", "Equity", "2026-03-04", "B", "200", "10", null, null, "2,000.00"),
        // A collapsed duplicate: the same row twice, matched once.
        new("OMS-TWICE", "Charlie Capital", "US0000000008", "Equity", "2026-03-04", "S", "1,500", "20.5", null, null, "30,750.00"),
        new("OMS-TWICE", "Charlie Capital", "US0000000008", "Equity", "2026-03-04", "S", "1,500", "20.5", null, null, "30,750.00"),
        // The trade a lifecycle chain on the street side ends in.
        new("OMS-LIFE", "Alfa Bank", "US0000000009", "Bond", "2026-03-03", "B", "2,000,000", "105.149", null, null, "2,102,980.00", "2026-03-05", "UTI-LIFE"),
        // A package: two swap legs against one street row. A second package has one leg only.
        new("OMS-LEG-PAY", "Bravo Sec", "XS0000000010", "Swap", "2026-03-02", "P", "13,000,000", "4.268", null, null, "278,961.22", null, "UTI-SWAP", "PKG-1", "PAY"),
        new("OMS-LEG-REC", "Bravo Sec", "XS0000000010", "Swap", "2026-03-02", "R", "13,000,000", "3.9645", null, null, "259,124.12", null, "UTI-SWAP", "PKG-1", "REC"),
        new("OMS-HALF-PAY", "Bravo Sec", "XS0000000015", "Swap", "2026-03-02", "P", "5,000,000", "4.1", null, null, "100,000.00", null, null, "PKG-2", "PAY"),
        // Many to one: three allocations that sum to one street block.
        new("OMS-ALLOC-1", "Charlie Capital", "US0000000011", "Bond", "2026-03-04", "B", "2,200", "114", null, null, "2,508.00", "2026-03-06"),
        new("OMS-ALLOC-2", "Charlie Capital", "US0000000011", "Bond", "2026-03-04", "B", "5,400", "114", null, null, "6,156.00", "2026-03-06"),
        new("OMS-ALLOC-3", "Charlie Capital", "US0000000011", "Bond", "2026-03-04", "B", "1,400", "114", null, null, "1,596.00", "2026-03-06"),
        // An attributed break on the reduced key: an 80% fill, with accrued pro rata and net cash following both.
        new("OMS-FILL", "Alfa Bank", "US0000000012", "Bond", "2026-03-04", "S", "1,000,000", "100", "4,784.72", "50.00", "1,004,734.72", "2026-03-06"),
        // No route takes FX.
        new("OMS-FX", "Alfa Bank", "EURUSD", "FX", "2026-03-02", "B", "1,000,000", "1.0930", null, null, "1,093,000.00"),
        // A null on one side of a compared field: the street row has no settle date.
        new("OMS-NOSETTLE", "Bravo Sec", "US0000000014", "Bond", "2026-03-02", "B", "400,000", "99.5", null, null, "398,000.00", "2026-03-04", "UTI-NULL"),
        // A block over the limit of four items: the search is not run.
        new("OMS-BIG-1", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-2", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-3", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-4", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-5", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
    ];

    public static readonly Street[] Right =
    [
        new("ST-EXACT", Alfa, "US0000000001", "BD", "2026-03-02", "S", "DEALER", "3.000.000,00", "96-02+", "32NDS", "12.500,00", "150,00", "2.894.993,75", "2026-03-04", "UTI-EXACT"),
        new("ST-WITHIN", Brav, "US0000000002", "BD", "02/03/2026", "B", "DEALER", "2.000.000", "101,5", null, "8.332,39", "100,00", "2.038.232,39", "05/03/2026"),
        new("ST-BREAK", Alfa, "US0000000003", "BD", "2026-03-03", "B", "CLIENT", "1.000.000", "99,966", null, "5.000,00", "50,00", "1.004.710,00", "2026-03-05", "UTI-BREAK"),
        // Right only.
        new("ST-ALLEGED", Char, "US0000000005", "BD", "03/04/2026", "B", "CLIENT", "750.000", "100", null, null, null, "750.000,00", "03/06/2026"),
        new("ST-AMB", Brav, "US0000000006", "EQ", "02/03/2026", "B", "CLIENT", "2.200", "114", null, null, null, "250.800,00"),
        // Invalid input: a price that is not in 32nds, and a counterparty the reference data does not know.
        new("ST-BADPX", Alfa, "US0000000007", "BD", "2026-03-02", "B", "CLIENT", "1.000", "98-1X", "32NDS", null, null, "1.000,00"),
        new("ST-NOPARTY", "5493000000000000ZULU", "US0000000007", "BD", "2026-03-02", "B", "CLIENT", "1.000", "100", null, null, null, "1.000,00"),
        new("ST-TWICE", Char, "US0000000008", "EQ", "03/04/2026", "B", "DEALER", "1.500", "20,5", null, null, null, "30.750,00"),
        // A lifecycle chain: a new report, its cancel, and the correction that replaces it.
        new("ST-LIFE-NEW", Alfa, "US0000000009", "BD", "2026-03-03", "B", "CLIENT", "2.000.000", "102,604", null, null, null, "2.052.080,00", "2026-03-05", "UTI-LIFE", "NEW"),
        new("ST-LIFE-CANC", Alfa, "US0000000009", "BD", "2026-03-03", "B", "CLIENT", "2.000.000", "102,604", null, null, null, "2.052.080,00", "2026-03-05", "UTI-LIFE", "CANC", "ST-LIFE-NEW"),
        new("ST-LIFE-CORR", Alfa, "US0000000009", "BD", "2026-03-03", "B", "CLIENT", "2.000.000", "105,149", null, null, null, "2.102.980,00", "2026-03-05", "UTI-LIFE", "CORR", "ST-LIFE-CANC"),
        // A chain that is cancelled and not replaced, and a correction whose target does not exist.
        new("ST-GONE-NEW", Brav, "US0000000016", "BD", "02/03/2026", "B", "CLIENT", "300.000", "100", null, null, null, "300.000,00", null, null, "NEW"),
        new("ST-GONE-CANC", Brav, "US0000000016", "BD", "02/03/2026", "B", "CLIENT", "300.000", "100", null, null, null, "300.000,00", null, null, "CANC", "ST-GONE-NEW"),
        new("ST-LOST-CORR", Brav, "US0000000018", "BD", "02/03/2026", "B", "CLIENT", "300.000", "100", null, null, null, "300.000,00", null, null, "CORR", "ST-NOWHERE"),
        // The street reports the swap as one row, with a blank direction and a signed notional.
        new("ST-SWAP", Brav, "XS0000000010", "SW", "02/03/2026", null, "CLIENT", "-13.000.000", "4,268", null, null, null, "19.837,10", null, "UTI-SWAP"),
        new("ST-BLOCK", Char, "US0000000011", "BD", "03/04/2026", "B", "CLIENT", "9.000", "114", null, null, null, "10.260,00", "03/06/2026"),
        new("ST-FILL", Alfa, "US0000000012", "BD", "2026-03-04", "B", "DEALER", "800.000", "100", null, "3.827,78", "50,00", "803.777,78", "2026-03-06"),
        new("ST-FX", Alfa, "EURUSD", "FX", "2026-03-02", "B", "CLIENT", "1.000.000", "1,0930", null, null, null, "1.093.000,00"),
        new("ST-NOSETTLE", Brav, "US0000000014", "BD", "02/03/2026", "B", "CLIENT", "400.000", "99,5", null, null, null, "398.000,00", null, "UTI-NULL"),
        new("ST-BIG", Alfa, "US0000000017", "EQ", "2026-03-05", "B", "CLIENT", "500", "50", null, null, null, "25.000,00"),
    ];

    private static readonly (string Column, Func<Internal, string?> Read)[] LeftColumns =
    [
        ("Trade_Id", row => row.Id), ("Cpty", row => row.Cpty), ("Isin", row => row.Isin), ("Asset", row => row.Asset), ("Trade_Dt", row => row.TradeDate),
        ("Dir", row => row.Dir), ("Qty", row => row.Qty), ("Px", row => row.Px), ("Acc", row => row.Accrued), ("Fee", row => row.Fee), ("Cash", row => row.Cash),
        ("Settle_Dt", row => row.Settle), ("Uti_Ref", row => row.Uti), ("Parent", row => row.Parent), ("Leg", row => row.Leg),
    ];

    private static readonly (string Column, Func<Street, string?> Read)[] RightColumns =
    [
        ("Report_Id", row => row.Id), ("Lei", row => row.Lei), ("Isin", row => row.Isin), ("Product", row => row.Product), ("Trade_Dt", row => row.TradeDate),
        ("Dir", row => row.Dir), ("View", row => row.View), ("Qty", row => row.Qty), ("Px", row => row.Px), ("Conv", row => row.Convention), ("Acc", row => row.Accrued),
        ("Fee", row => row.Fee), ("Cash", row => row.Cash), ("Settle_Dt", row => row.Settle), ("Uti_Ref", row => row.Uti), ("Event", row => row.Event), ("Prior", row => row.Prior),
    ];

    public static CompiledProfile Compile() =>
        Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore()));

    public static RecordSource LeftSource(IReadOnlyList<int>? order = null) =>
        RecordSources.FromObjects("left", Ordered(Left, order), Map(LeftColumns));

    public static RecordSource RightSource(IReadOnlyList<int>? order = null) =>
        RecordSources.FromObjects("right", Ordered(Right, order), Map(RightColumns));

    public static DataTable LeftTable() => Table(Left, LeftColumns);

    public static DataTable RightTable() => Table(Right, RightColumns);

    private static T[] Ordered<T>(T[] rows, IReadOnlyList<int>? order) =>
        order is null ? rows : [.. order.Select(position => rows[position])];

    private static SourceMap<T> Map<T>((string Column, Func<T, string?> Read)[] columns) =>
        columns.Aggregate(new SourceMap<T>(), (map, column) => map.Text(column.Column, column.Read));

    private static DataTable Table<T>(T[] rows, (string Column, Func<T, string?> Read)[] columns)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        foreach (var (column, _) in columns)
            table.Columns.Add(column, typeof(string));
#pragma warning restore IDISP004
        foreach (var row in rows)
            table.Rows.Add([.. columns.Select(column => (object?)column.Read(row) ?? DBNull.Value)]);
        return table;
    }

    internal sealed record Internal(
        string? Id, string? Cpty, string? Isin, string? Asset, string? TradeDate, string? Dir, string? Qty, string? Px,
        string? Accrued = null, string? Fee = null, string? Cash = null, string? Settle = null, string? Uti = null, string? Parent = null, string? Leg = null);

    internal sealed record Street(
        string? Id, string? Lei, string? Isin, string? Product, string? TradeDate, string? Dir, string? View, string? Qty, string? Px,
        string? Convention = null, string? Accrued = null, string? Fee = null, string? Cash = null, string? Settle = null, string? Uti = null, string? Event = null, string? Prior = null);
}

// Runs the fixed inputs and writes the whole result as text. Everything is written with the invariant culture.
internal static class GoldenResult
{
    // Everything above this line is the same for the rows in any order. What is below follows the order of the input.
    public const string InputOrderSection = "[all entries with their members, then the rows, in the order the engine returned them: this follows the input order]";

    private static readonly SideBySideSpec View = new(["Counterparty"], ["AssetClass"], [new MeasureSpec("Quantity"), new MeasureSpec("NetCash")], null);

    public static IReconciliationResult Run(IReadOnlyList<int>? left = null, IReadOnlyList<int>? right = null, RunOptions? options = null) =>
        Reconciler.Run(GoldenInputs.Compile(), GoldenInputs.LeftSource(left), GoldenInputs.RightSource(right), GoldenInputs.Reference, options);

    // left and right say which fixed row each input position holds, so a row reads the same wherever it was fed in.
    public static string Text(IReconciliationResult result, IReadOnlyList<int>? left = null, IReadOnlyList<int>? right = null)
    {
        int Position(SourceRef row) => (row.Source == "left" ? left : right) is { } order ? order[row.Ordinal] : row.Ordinal;
        string Ref(SourceRef row) => $"{row.Source}:{(row.RowKey.Length == 0 ? "<no key>" : row.RowKey)}#{Position(row).ToString(CultureInfo.InvariantCulture)}";
        IEnumerable<SourceRef> Sorted(IEnumerable<SourceRef> rows) =>
            rows.OrderBy(row => row.Source, StringComparer.Ordinal).ThenBy(row => row.RowKey, StringComparer.Ordinal).ThenBy(Position);
        string Refs(IEnumerable<SourceRef> rows) => $"[{string.Join(", ", Sorted(rows).Select(Ref))}]";
        string[] fields = [.. GoldenInputs.Compile().Document.Fields.Select(field => field.Name)];
        var entries = result.Entries.OrderBy(entry => Refs(entry.Left.Concat(entry.Right)), StringComparer.Ordinal).ToArray();
        var byRef = result.Rows.ToDictionary(row => row.Row);
        RowResult[] rows = [.. Sorted(result.Rows.Select(row => row.Row)).Select(row => byRef[row])];
        IEnumerable<string> lines =
        [
            "# What the engine returns for the fixed inputs of GoldenResultsTests: every entry, diff, row, diagnostic and explanation,",
            "# the summary, each row's canonical values and trace, and one side-by-side view. Entries and rows are sorted by their members,",
            "# and the last two sections list them in the order the engine returned them.",
            "# Not here: the engine version, which changes with every build, and the interned ids inside a canonical value.",
            "# GoldenResultsTests compares the engine with this file on net8.0 and net10.0, and fails on any difference.",
            "# To accept an intended change, copy the .actual.txt file the failing test names over this file and commit it.",
            "",
            "[run]",
            $"profileHash={result.Run.ProfileHash}",
            $"reference={result.Run.ReferenceId} hash={result.Run.ReferenceHash}",
            .. result.Run.Reading.OrderBy(side => side.Key, StringComparer.Ordinal).Select(side =>
                $"reading {side.Key}: culture={Quote(side.Value.Culture)} decimal={Quote(side.Value.DecimalSeparator)} group={Quote(side.Value.GroupSeparator)} origin={side.Value.Origin} strictDateFormats={Flag(side.Value.StrictDateFormats)}"),
            "",
            "[summary]",
            $"Matched={Number(result.Summary.Matched)} Exact={Number(result.Summary.Exact)} WithinTolerance={Number(result.Summary.WithinTolerance)} Break={Number(result.Summary.Break)}",
            $"LeftOnly={Number(result.Summary.LeftOnly)} RightOnly={Number(result.Summary.RightOnly)} Ambiguous={Number(result.Summary.Ambiguous)} SearchIncomplete={Number(result.Summary.SearchIncomplete)}",
            $"Quarantined={Number(result.Summary.Quarantined)} Suppressed={Number(result.Summary.Suppressed)}",
            $"entries={Number(result.Entries.Length)} rows={Number(result.Rows.Length)}",
            "",
            "[entries]",
            .. entries.SelectMany(entry => Entry(result, entry, fields, Ref, Refs)),
            "[rows]",
            .. rows.SelectMany(row => Row(result, row, fields, Ref)),
            "[side by side] rows=Counterparty columns=AssetClass measures=Quantity,NetCash",
            .. result.BuildSideBySide(View).Lines.SelectMany(line => line.Cells.OrderBy(cell => cell.Key, StringComparer.Ordinal).Select(cell =>
                $"{line.Row} | {line.Column} | {cell.Key}: left={Number(cell.Value.Left)} right={Number(cell.Value.Right)} delta={Number(cell.Value.Delta)} leftOnly={Number(cell.Value.LeftOnly)} rightOnly={Number(cell.Value.RightOnly)} matchedDelta={Number(cell.Value.MatchedDelta)}")),
            "",
            "[matched entries, in the order the engine returned them]",
            .. result.Entries.Where(entry => entry.Alignment is Alignment.Matched).Select(entry => $"entry {entry.Id}"),
            "",
            InputOrderSection,
            .. result.Entries.Select(entry => $"entry {entry.Id} left=[{string.Join(", ", entry.Left.Select(Ref))}] right=[{string.Join(", ", entry.Right.Select(Ref))}]"),
            .. result.Rows.Select(row => $"row {Ref(row.Row)}"),
            "",
        ];
        return string.Join('\n', lines);
    }

    private static IEnumerable<string> Entry(IReconciliationResult result, ResultEntry entry, string[] fields, Func<SourceRef, string> name, Func<IEnumerable<SourceRef>, string> names) =>
    [
        $"entry {entry.Id} path={Quote(entry.PathId)} alignment={entry.Alignment} composition={entry.Composition} outcome={entry.Outcome}",
        $"  left={names(entry.Left)}",
        $"  right={names(entry.Right)}",
        .. entry.Diffs.Select(diff =>
            $"  diff {diff.Field.Name}: outcome={diff.Outcome} left={Value(diff.Left)} right={Value(diff.Right)} delta={diff.Delta.Kind}/{Number(diff.Delta.Number)}/{Number(diff.Delta.Days)} tolerance={Quote(diff.Tolerance.Value)} attribution={Quote(diff.Attribution.Value)} attributedTo=[{string.Join(", ", diff.AttributedTo.IsDefault ? [] : diff.AttributedTo)}]"),
        $"  committedBy={entry.Explanation.CommittedBy ?? "<none>"}",
        .. entry.Explanation.Trail.Select(attempt =>
            $"  trail {attempt.PassId}: verdict={attempt.Verdict} key={Quote(attempt.Key)} candidates={names(attempt.Candidates)} detail={Quote(attempt.Detail)}"),
        .. entry.Explanation.Hints.Select(hint => $"  hint {hint.Code}: {hint.Detail}"),
        .. entry.Alignment is Alignment.Matched
            ? [$"  projection left: {Canonical(result.Projection(entry.Id, Side.Left), fields)}", $"  projection right: {Canonical(result.Projection(entry.Id, Side.Right), fields)}"]
            : Array.Empty<string>(),
        "",
    ];

    private static IEnumerable<string> Row(IReconciliationResult result, RowResult row, string[] fields, Func<SourceRef, string> name) =>
    [
        $"row {name(row.Row)} disposition={row.Disposition} suppression={row.Suppression?.ToString() ?? "<none>"} validity={row.Validity} entry={row.EntryId?.Value ?? "<none>"}",
        .. row.Diagnostics.Select(diagnostic =>
            $"  diagnostic {diagnostic.Code}: severity={diagnostic.Severity} rule={diagnostic.RuleId} field={Quote(diagnostic.Field)} raw={Quote(diagnostic.RawText)}"),
        $"  canonical: {Canonical(result.Canonical(row.Row), fields)}",
        .. result.Trace(row.Row).Select(step => $"  trace {step.RuleId}: {step.Field} {Quote(step.Raw)} -> {Quote(step.Canonical)}"),
        "",
    ];

    private static string Canonical(CanonicalRow row, string[] fields) =>
        string.Join(" ", fields.Where(field => row.IsNull(field) is false).Select(field => $"{field}={Value(row[field])}"));

    // A value reads as its type and its text. Decimals keep their scale, so 1.5 and 1.50 read differently.
    private static string Value(CanonicalValue value) =>
        value switch
        {
            { IsNull: true } => "<null>",
            { Type: FieldType.Decimal } => $"Decimal:{Number(value.Number)}",
            { Type: FieldType.Date } => $"Date:{value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            _ => $"{value.Type}:{Quote(value.Text)}",
        };

    private static string Quote(string? text) => text is null ? "<null>" : $"\"{text}\"";

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
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
        return differing.Length == 0
            ? null
            : $"{differing.Length.ToString(CultureInfo.InvariantCulture)} line{(differing.Length == 1 ? " differs" : "s differ")}, of {golden.Count.ToString(CultureInfo.InvariantCulture)} in the golden file and {actual.Count.ToString(CultureInfo.InvariantCulture)} now. The first {Math.Min(differing.Length, ListedLines).ToString(CultureInfo.InvariantCulture)}:"
                + string.Concat(differing.Take(ListedLines).Select(index =>
                    $"\nline {(index + 1).ToString(CultureInfo.InvariantCulture)}\n  golden: {At(golden, index) ?? "<no line>"}\n  actual: {At(actual, index) ?? "<no line>"}"));
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
