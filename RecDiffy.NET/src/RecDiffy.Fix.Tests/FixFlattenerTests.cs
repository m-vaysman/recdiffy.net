using System.Collections.Frozen;
using RecDiffy.Results;

namespace RecDiffy.Fix.Tests;

public class FixFlattenerTests
{
    private static readonly FixFilter OrderStates = new(
        Include: new Dictionary<int, IReadOnlyList<string>> { [35] = ["8"] },
        Exclude: new Dictionary<int, IReadOnlyList<string>> { [150] = ["A", "6", "E", "F", "I"] });

    [Fact]
    public void A_row_per_message_has_its_tags_as_columns_and_folds_single_entry_groups()
    {
        var row = Assert.Single(Flatten(Wire.Message("8", (11, "C1"), (17, "E1"), (150, "0"), (453, "1"), (448, "GS"), (452, "1"), (136, "2"), (137, "1"), (137, "2"))).Rows);

        Assert.Equal(("C1", "E1", "0", "GS"), (row["11"], row["17"], row["150"], row["448"]));
        Assert.False(row.ContainsKey("137"));
        Assert.Equal("2", row["136"]);
    }

    [Fact]
    public void At_the_account_grain_each_account_carries_its_message_and_its_single_fee()
    {
        var rows = Flatten(Allocation(), new FixFlattenOptions(Grain: 78)).Rows;

        Assert.Equal(["AL1|FUND-A|200|IA1|12.50", "AL1|FUND-B|100|IA2|6.25"], rows.Select(row => $"{row["70"]}|{row["79"]}|{row["80"]}|{row["467"]}|{row["137"]}"), StringComparer.Ordinal);
        Assert.All(rows, row => Assert.False(row.ContainsKey("448")));
    }

    [Fact]
    public void At_a_nested_grain_each_entry_carries_its_enclosing_entry()
    {
        var text = Wire.Message("J",
        [
            (70, "AL1"), (78, "2"),
            (79, "FUND-A"), (80, "200"), (136, "1"), (137, "12.50"), (139, "7"),
            (79, "FUND-B"), (80, "100"), (136, "2"), (137, "6.25"), (139, "7"), (137, "1.00"), (139, "4"),
        ]);

        var rows = Flatten(text, new FixFlattenOptions(Grain: 136)).Rows;

        Assert.Equal(["AL1|FUND-A|12.50|7", "AL1|FUND-B|6.25|7", "AL1|FUND-B|1.00|4"], rows.Select(row => $"{row["70"]}|{row["79"]}|{row["137"]}|{row["139"]}"), StringComparer.Ordinal);
    }

    [Fact]
    public void A_filter_keeps_only_the_messages_it_names_and_session_messages_never_become_rows()
    {
        var flat = Flatten(
            Wire.Message("0"),
            Wire.Ack("0", "E1", "C1", null, "100"),
            Wire.Ack("E", "E2", "C2", "C1", "150"),
            Wire.Message("J", (70, "AL1")));

        var filtered = FixFlattener.Flatten(FixReader.Read(string.Concat(Wire.Message("0"), Wire.Ack("0", "E1", "C1", null, "100"), Wire.Ack("E", "E2", "C2", "C1", "150"), Wire.Message("J", (70, "AL1"))), Wire.Options), Wire.Options, new FixFlattenOptions(Filter: OrderStates));

        Assert.Equal(3, flat.Rows.Count);
        Assert.Equal(["E1"], filtered.Rows.Select(row => row["17"]), StringComparer.Ordinal);
        Assert.Equal(["session message", "filtered out", "filtered out"], filtered.Skipped.Select(skip => skip.Reason), StringComparer.Ordinal);
    }

    [Fact]
    public void The_counterparty_is_the_other_party_and_a_message_between_others_is_rejected()
    {
        var ours = Assert.Single(Flatten(Wire.Ack("0", "E1", "C1", null, "100")).Rows);
        var others = Flatten(Wire.Message("8", "FIX.4.4", FixOptions.Soh, "MS", "JPM", (11, "C1"), (17, "E1"), (150, "0")));

        Assert.Equal("GS", ours["Counterparty"]);
        Assert.Contains("is ours", Assert.Single(others.Rejected).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_execution_report_knows_its_chain_root_and_the_previous_kept_report()
    {
        var rows = Flatten(
            Wire.Ack("0", "E1", "M1", null, "100"),
            Wire.Ack("5", "E2", "M2", "M1", "150"),
            Wire.Ack("5", "E3", "M3", "M2", "120"),
            Wire.Ack("4", "E4", "M3", null, "120", "4")).Rows;

        Assert.Equal(["M1|-", "M1|E1", "M1|E2", "M1|E3"], rows.Select(row => $"{row["RootClOrdID"]}|{row["PrevExecID"] ?? "-"}"), StringComparer.Ordinal);
    }

    [Fact]
    public void A_utc_timestamp_also_appears_as_exact_nanoseconds()
    {
        var row = Assert.Single(Flatten(Wire.Ack("0", "E1", "C1", null, "100")).Rows);

        Assert.Equal(("20261119-15:00:01.123456789", "1795100401123456789"), (row["60"], row["60:ns"]));
        Assert.Equal("1795100400000000000", row["52:ns"]);
        Assert.False(row.ContainsKey("38:ns"));
    }

    [Fact]
    public void A_profile_reconciles_the_generic_rows_and_decides_what_each_value_means()
    {
        var ours = string.Concat(
            Wire.Ack("0", "E1", "M1", null, "100"), Wire.Ack("E", "E2", "M2", "M1", "150"), Wire.Ack("5", "E3", "M2", "M1", "150"),
            Wire.Ack("0", "E5", "U1", null, "80"), Wire.Ack("4", "E6", "U1", null, "80", "4"),
            Wire.Ack("0", "E9", "L1", null, "75"));
        var broker = string.Concat(
            Wire.Ack("0", "E1", "M1", null, "100"), Wire.Ack("5", "E3", "M2", "M1", "150"),
            Wire.Ack("0", "E5", "U1", null, "80"), Wire.Ack("4", "E6", "U1", null, "80", "4"));
        var options = new FixFlattenOptions(Filter: OrderStates);
        var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Sample("generic-orders.profile.json"))), RuleRegistry.CreateCore());

        var result = Reconciler.Run(
            profile,
            Source("left", FixFlattener.Read(ours, Wire.Options, options).Rows),
            Source("right", FixFlattener.Read(broker, Wire.Options, options).Rows),
            ReferenceDataSet.Empty);

        var replaced = Assert.Single(result.Entries, entry => entry.Left.Any(row => row.RowKey is "E3"));
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (replaced.Alignment, replaced.Outcome));
        Assert.All(["E5", "E6"], key => Assert.Equal(SuppressionReason.FullyCancelled, Assert.Single(result.Rows, row => row.Row.Source is "left" && row.Row.RowKey == key).Suppression));
        Assert.Equal(Alignment.LeftOnly, Assert.Single(result.Entries, entry => entry.Left.Any(row => row.RowKey is "E9")).Alignment);
    }

    private static FixFlat Flatten(params string[] messages) => FixFlattener.Read(string.Concat(messages), Wire.Options);

    private static FixFlat Flatten(string message, FixFlattenOptions options) => FixFlattener.Read(message, Wire.Options, options);

    private static string Allocation() =>
        Wire.Message("J",
        [
            (70, "AL1"), (71, "0"), (73, "2"), (11, "C1"), (37, "O1"), (38, "200"), (11, "C2"), (37, "O2"), (38, "100"),
            (78, "2"),
            (79, "FUND-A"), (80, "200"), (467, "IA1"), (136, "1"), (137, "12.50"), (139, "7"),
            (79, "FUND-B"), (80, "100"), (467, "IA2"), (136, "1"), (137, "6.25"), (139, "7"),
            (453, "2"), (448, "US"), (452, "13"), (448, "GS"), (452, "1"),
        ]);

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
