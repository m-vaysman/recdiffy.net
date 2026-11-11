namespace RecDiffy.IO.Tests.OracleMatrix;

// AC-1 and AC-5 compare each row's entry with one oracle case, so each oracle must name every input row exactly once,
// and only rows its workbook has. This checks the oracle and its adapter, before any engine test relies on them.
public class OraclePartitionTests
{
    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void The_oracle_names_every_left_row_once(string name)
    {
        var dataset = OracleDatasets.Named(name);

        AssertPartition(dataset, dataset.LeftTab(), dataset.Cases().SelectMany(item => item.Left));
    }

    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void The_oracle_names_every_right_row_once(string name)
    {
        var dataset = OracleDatasets.Named(name);

        AssertPartition(dataset, dataset.RightTab(), dataset.Cases().SelectMany(item => item.Right));
    }

    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void A_lifecycle_survivor_is_one_of_its_cases_right_rows(string name)
    {
        var misplaced = OracleDatasets.Named(name).Cases()
            .Where(item => item.Survivor is { } survivor && item.Right.Contains(survivor, StringComparer.Ordinal) is false)
            .Select(item => $"{item.Id}: survivor {item.Survivor} is not in [{string.Join(", ", item.Right)}]")
            .ToArray();

        Assert.True(misplaced.Length == 0, string.Join(Environment.NewLine, misplaced));
    }

    // A row listed as not a trade must be in the tab, so the exclusion can't hide a missing row.
    [Theory]
    [MemberData(nameof(OracleDatasets.Names), MemberType = typeof(OracleDatasets))]
    public void Every_row_listed_as_not_a_trade_is_in_an_input_tab_and_in_no_case(string name)
    {
        var dataset = OracleDatasets.Named(name);
        var rows = dataset.LeftTab().Concat(dataset.RightTab()).ToHashSet(StringComparer.Ordinal);
        var named = dataset.Cases().SelectMany(item => item.Left.Concat(item.Right)).ToHashSet(StringComparer.Ordinal);

        Assert.All(dataset.NotTrades, row => Assert.Contains(row, rows));
        Assert.All(dataset.NotTrades, row => Assert.DoesNotContain(row, named));
    }

    private static void AssertPartition(OracleDataset dataset, IReadOnlyList<string> all, IEnumerable<string> named)
    {
        var rows = all.Where(row => dataset.NotTrades.Contains(row, StringComparer.Ordinal) is false).ToArray();
        var counts = named.GroupBy(key => key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var problems = rows.Where(row => counts.ContainsKey(row) is false).Select(row => $"{row}: in no case")
            .Concat(counts.Where(pair => pair.Value > 1).Select(pair => $"{pair.Key}: in {pair.Value} cases"))
            .Concat(counts.Keys.Except(rows, StringComparer.Ordinal).Select(key => $"{key}: not a row of the workbook"))
            .ToArray();

        Assert.NotEmpty(rows);
        Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
        Assert.True(problems.Length == 0, $"{problems.Length} problems:{Environment.NewLine}{string.Join(Environment.NewLine, problems.Take(40))}");
    }
}
