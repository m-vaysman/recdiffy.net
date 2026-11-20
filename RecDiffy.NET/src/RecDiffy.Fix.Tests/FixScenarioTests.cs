using System.Collections.Frozen;
using RecDiffy.Results;

namespace RecDiffy.Fix.Tests;

// Each scenario flattens FIX messages with the generic flattener and reconciles them with a sample profile from
// test-data/fix-reconciliation. Every meaning (which tag is the key, what 150 or 71 means, what is compared) lives in
// the profile; the flattener only turns tags into columns.
public class FixScenarioTests
{
    private const char Soh = FixOptions.Soh;

    [Fact]
    public void Fills_match_by_exec_id_and_corrections_and_busts_follow_exec_ref_id()
    {
        var ours = string.Concat(Fills(), Wire.Fill("F", "F5", "C1", "100", "50.00"));
        var theirs = string.Concat(Fills(), Wire.Fill("F", "F4", "C1", "100", "50.00"));
        var fills = new FixFlattenOptions(Filter: Only(35, "8"));

        var result = Reconcile("generic-fills.profile.json", ours, fills, theirs, fills);

        Assert.Equal(FieldOutcome.Exact, Entry(result, "F1").Outcome);
        Assert.Equal(["F2c"], Keys(Entry(result, "F2c").Right), StringComparer.Ordinal);
        Assert.Equal(SuppressionReason.Superseded, Row(result, "left", "F2").Suppression);
        Assert.All(["F3", "F3x"], key => Assert.Equal(SuppressionReason.FullyCancelled, Row(result, "right", key).Suppression));
        Assert.Equal(Alignment.LeftOnly, Entry(result, "F5").Alignment);
        Assert.Equal(Alignment.RightOnly, RightEntry(result, "F4").Alignment);
    }

    [Fact]
    public void Equity_allocations_match_our_instruction_to_their_confirmations_on_individual_alloc_id()
    {
        var result = Equities();

        Assert.Equal(FieldOutcome.Exact, Entry(result, "AL2|FUND-A").Outcome);
        Assert.Equal(["CF1"], Keys(Entry(result, "AL2|FUND-A").Right), StringComparer.Ordinal);
    }

    [Fact]
    public void A_cancelled_instruction_without_accounts_cancels_every_account_it_allocated()
    {
        var result = Equities();

        Assert.All(
            ["AL1|FUND-A", "AL1|FUND-B", "AL1x|FUND-A", "AL1x|FUND-B"],
            key => Assert.Equal(SuppressionReason.FullyCancelled, Row(result, "left", key).Suppression));
    }

    [Fact]
    public void A_confirmation_cancelled_and_sent_again_matches_on_the_new_one()
    {
        var result = Equities();

        Assert.Equal(["CF3"], Keys(Entry(result, "AL2|FUND-B").Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, Entry(result, "AL2|FUND-B").Outcome);
        Assert.All(["CF2", "CF2x"], key => Assert.Equal(SuppressionReason.FullyCancelled, Row(result, "right", key).Suppression));
    }

    [Fact]
    public void A_confirmation_without_an_individual_alloc_id_falls_back_to_the_economic_key()
    {
        var entry = Entry(Equities(), "AL3|FUND-C");

        Assert.Equal(["CF4"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Theory]
    [InlineData("AL4|FUND-D", FieldOutcome.WithinTolerance)]
    [InlineData("AL5|FUND-E", FieldOutcome.Break)]
    public void Net_money_is_checked_within_tolerance_only_when_the_sell_side_calculates(string key, FieldOutcome outcome)
    {
        var diff = Assert.Single(Entry(Equities(), key).Diffs, diff => diff.Field.Name is "NetMoney");

        Assert.Equal((outcome, 0.03m), (diff.Outcome, diff.Delta.Number));
    }

    [Fact]
    public void Futures_allocations_match_one_account_to_the_brokers_reshaped_rows_by_quantity()
    {
        var ours = Message("J", "US", "GS",
            (70, "AL1"), (71, "0"), (55, "ESZ6"), (78, "3"),
            (79, "FUND-A"), (80, "200"), (467, "IA1"), (79, "FUND-B"), (80, "100"), (467, "IA2"), (79, "FUND-C"), (80, "50"), (467, "IA3"));
        var theirs = Message("AS", "GS", "US",
            (755, "AR1"), (70, "AL1"), (55, "ESZ6"), (78, "3"),
            (79, "FUND-A"), (366, "10.25"), (80, "120"), (467, "IA1"),
            (79, "FUND-A"), (366, "10.50"), (80, "80"), (467, "IA1"),
            (79, "FUND-B"), (366, "10.25"), (80, "100"), (467, "IA2"));

        var result = Reconcile(
            "generic-futures-allocations.profile.json",
            ours, new FixFlattenOptions(Grain: 78, Keys: KeysOf(("Key", [70, 79]))),
            theirs, new FixFlattenOptions(Grain: 78, Keys: KeysOf(("Key", [755, 79, 366]))));

        var reshaped = Entry(result, "AL1|FUND-A");
        Assert.True(reshaped.Composition.HasFlag(Composition.OneToMany));
        Assert.Equal(["AR1|FUND-A|10.25", "AR1|FUND-A|10.50"], Keys(reshaped.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, Entry(result, "AL1|FUND-B").Outcome);
        Assert.Equal(Alignment.LeftOnly, Entry(result, "AL1|FUND-C").Alignment);
    }

    [Fact]
    public void Bond_allocations_compare_the_dealers_net_money_and_accrued()
    {
        var ours = Message("J", "US", "GS",
            (70, "BJ1"), (71, "0"), (54, "1"), (55, "US912828XY"), (78, "2"),
            (79, "FUND-A"), (80, "1000000"), (154, "1002345.67"), (742, "1234.56"),
            (79, "FUND-B"), (80, "500000"), (154, "501172.84"), (742, "617.28"));
        var theirs = Message("AS", "GS", "US",
            (755, "BR1"), (54, "1"), (55, "US912828XY"), (78, "2"),
            (79, "FUND-A"), (80, "1000000"), (154, "1002345.68"), (742, "1234.56"),
            (79, "FUND-B"), (80, "500000"), (154, "501172.84"), (742, "617.30"));

        var result = Reconcile(
            "generic-bond-allocations.profile.json",
            ours, new FixFlattenOptions(Grain: 78, Keys: KeysOf(("Key", [70, 79]))),
            theirs, new FixFlattenOptions(Grain: 78, Keys: KeysOf(("Key", [755, 79]))));

        Assert.Equal(FieldOutcome.WithinTolerance, Entry(result, "BJ1|FUND-A").Outcome);
        var accrued = Assert.Single(Entry(result, "BJ1|FUND-B").Diffs, diff => diff.Field.Name is "Accrued");
        Assert.Equal((FieldOutcome.Break, 0.02m), (accrued.Outcome, accrued.Delta.Number));
    }

    private static IReconciliationResult Equities()
    {
        var instructions = string.Concat(
            Instruction("AL1", "0", null, "1", "IBM", "150.25", ("FUND-A", "200", "IA1", "30050.00", "10.00", "2.50"), ("FUND-B", "100", "IA2", "15025.00", "5.00", "1.25")),
            Message("J", "US", "GS", (70, "AL1x"), (71, "2"), (72, "AL1"), (55, "IBM")),
            Instruction("AL2", "0", null, "1", "IBM", "150.25", ("FUND-A", "150", "IA3", "22537.50", "7.50", "1.88"), ("FUND-B", "150", "IA4", "22537.50", "7.50", "1.88")),
            Instruction("AL3", "0", null, "1", "MSFT", "410.10", ("FUND-C", "50", "IA5", "20505.00", "2.50", "0.60")),
            Instruction("AL4", "0", null, "2", "AAPL", "225.00", ("FUND-D", "10", "IA6", "2250.00", "1.00", "0.10")),
            Instruction("AL5", "0", null, "1", "AAPL", "225.00", ("FUND-E", "10", "IA7", "2250.00", "1.00", "0.10")));
        var confirmations = string.Concat(
            Confirmation("CF1", "0", null, "IA3", "FUND-A", "IBM", "150", "150.25", "22537.50", "7.50", "1.88"),
            Confirmation("CF2", "0", null, "IA4", "FUND-B", "IBM", "150", "150.25", "22538.50", "7.50", "1.88"),
            Confirmation("CF2x", "2", "CF2", "IA4", "FUND-B", "IBM", "150", "150.25", "22538.50", "7.50", "1.88"),
            Confirmation("CF3", "0", null, "IA4", "FUND-B", "IBM", "150", "150.25", "22537.50", "7.50", "1.88"),
            Confirmation("CF4", "0", null, null, "FUND-C", "MSFT", "50", "410.10", "20505.00", "2.50", "0.60"),
            Confirmation("CF5", "0", null, "IA6", "FUND-D", "AAPL", "10", "225.00", "2250.03", "1.00", "0.10"),
            Confirmation("CF6", "0", null, "IA7", "FUND-E", "AAPL", "10", "225.00", "2250.03", "1.00", "0.10"));
        return Reconcile(
            "generic-equity-allocations.profile.json",
            instructions, new FixFlattenOptions(Grain: 78, Filter: Only(35, "J"), Keys: KeysOf(("Key", [70, 79]), ("TargetKey", [72, 79])), Reference: (70, 72)),
            confirmations, new FixFlattenOptions(Filter: Only(35, "AK")));
    }

    private static string Instruction(string allocId, string transType, string? refId, string allocType, string symbol, string avgPx, params (string Account, string Qty, string IndividualId, string NetMoney, string Commission, string Fee)[] accounts) =>
        Message("J", "US", "GS",
        [
            (70, allocId), (71, transType), .. refId is null ? Array.Empty<(int, string)>() : [(72, refId)], (626, allocType), (54, "1"), (55, symbol), (6, avgPx),
            (78, accounts.Length.ToInvariant()),
            .. accounts.SelectMany(account => new (int, string)[] { (79, account.Account), (80, account.Qty), (467, account.IndividualId), (154, account.NetMoney), (12, account.Commission), (136, "1"), (137, account.Fee), (139, "7") }),
        ]);

    private static string Confirmation(string confirmId, string transType, string? refId, string? individualId, string account, string symbol, string qty, string avgPx, string netMoney, string commission, string fee) =>
        Message("AK", "GS", "US",
        [
            (664, confirmId), (666, transType), .. refId is null ? Array.Empty<(int, string)>() : [(772, refId)], (773, "2"), (665, "4"),
            .. individualId is null ? Array.Empty<(int, string)>() : [(467, individualId)],
            (79, account), (80, qty), (54, "1"), (55, symbol), (6, avgPx), (118, netMoney), (2639, "1"), (2640, commission), (136, "1"), (137, fee), (139, "7"),
        ]);

    private static string Fills() =>
        string.Concat(
            Wire.Fill("F", "F1", "C1", "100", "50.00"),
            Wire.Fill("F", "F2", "C1", "100", "50.00"),
            Wire.Fill("G", "F2c", "C1", "100", "50.10", "F2"),
            Wire.Fill("F", "F3", "C1", "100", "50.00"),
            Wire.Fill("H", "F3x", "C1", "100", "50.00", "F3"));

    private static string Message(string msgType, string sender, string target, params (int Tag, string Value)[] body) =>
        Wire.Message(msgType, "FIX.4.4", Soh, sender, target, body);

    private static FixFilter Only(int tag, string value) => new(Include: new Dictionary<int, IReadOnlyList<string>> { [tag] = [value] });

    private static Dictionary<string, IReadOnlyList<int>> KeysOf(params (string Name, int[] Tags)[] keys) =>
        keys.ToDictionary(key => key.Name, key => (IReadOnlyList<int>)key.Tags, StringComparer.Ordinal);

    private static IReconciliationResult Reconcile(string profileFile, string left, FixFlattenOptions leftOptions, string right, FixFlattenOptions rightOptions)
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Sample(profileFile))), RuleRegistry.CreateCore());
        var leftFlat = FixFlattener.Read(left, Wire.Options, leftOptions);
        var rightFlat = FixFlattener.Read(right, Wire.Options, rightOptions);
        Assert.Empty(leftFlat.Rejected);
        Assert.Empty(rightFlat.Rejected);
        return Reconciler.Run(profile, Source("left", leftFlat.Rows), Source("right", rightFlat.Rows), ReferenceDataSet.Empty);
    }

    private static ResultEntry Entry(IReconciliationResult result, string leftKey) =>
        Assert.Single(result.Entries, entry => entry.Left.Any(row => string.Equals(row.RowKey, leftKey, StringComparison.Ordinal)));

    private static ResultEntry RightEntry(IReconciliationResult result, string rightKey) =>
        Assert.Single(result.Entries, entry => entry.Right.Any(row => string.Equals(row.RowKey, rightKey, StringComparison.Ordinal)));

    private static RowResult Row(IReconciliationResult result, string side, string key) =>
        Assert.Single(result.Rows, row => string.Equals(row.Row.Source, side, StringComparison.Ordinal) && string.Equals(row.Row.RowKey, key, StringComparison.Ordinal));

    private static IReadOnlyList<string> Keys(IEnumerable<SourceRef> rows) => [.. rows.Select(row => row.RowKey).Order(StringComparer.Ordinal)];

    private static RecordSource Source(string side, IReadOnlyList<FrozenDictionary<string, string?>> rows) =>
        RecordSources.FromObjects(
            side,
            rows,
            rows.SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Aggregate(new SourceMap<FrozenDictionary<string, string?>>(), (map, column) => map.Text(column, row => row.GetValueOrDefault(column))));

    private static string Sample(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "RecDiffy.NET", "test-data", "fix-reconciliation", file);
    }
}
