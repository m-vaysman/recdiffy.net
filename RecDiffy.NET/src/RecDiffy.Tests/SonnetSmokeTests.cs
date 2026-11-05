using System.Data;
using System.Text.Json;
using ExcelDataReader;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class SonnetSmokeTests
{
    [Fact]
    public void Sonnet_membership_matches_oracle_counts()
    {
        var root = FindRepo();
        var folder = Path.Combine(root, "test-data", "trade-reconciliation-dataset", "claude-sonnet-5.5");
        var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Profiles", "trade-sonnet.json"))), RuleRegistry.CreateDefault().AddTradingRules());
        var reference = ReferenceData(Path.Combine(folder, "RecDiffy_Hidden_Test_Oracle.json"));
        using var left = Open(Path.Combine(folder, "RecDiffy_Trade_Reconciliation_Dataset.xlsx"), "Internal_Trades");
        using var right = Open(Path.Combine(folder, "RecDiffy_Trade_Reconciliation_Dataset.xlsx"), "Counterparty_Trades");
        var result = Reconciler.Run(profile, RecordSources.FromDataReader("left", () => Open(Path.Combine(folder, "RecDiffy_Trade_Reconciliation_Dataset.xlsx"), "Internal_Trades"), new TabularOptions(0, "Internal_Trade_ID")), RecordSources.FromDataReader("right", () => Open(Path.Combine(folder, "RecDiffy_Trade_Reconciliation_Dataset.xlsx"), "Counterparty_Trades"), new TabularOptions(0, "Cpty_Report_ID")), reference, new RunOptions(1));
        Assert.Equal(0, result.Summary.Quarantined);
        Assert.Equal(183, result.Summary.MatchedEntries);
        Assert.Equal(86, result.Summary.CommitsByPass.GetValueOrDefault("P1"));
        Assert.Equal(81, result.Summary.CommitsByPass.GetValueOrDefault("P2"));
        Assert.Equal(12, result.Summary.CommitsByPass.GetValueOrDefault("P3"));
        Assert.Equal(4, result.Summary.CommitsByPass.GetValueOrDefault("P4"));
        var odd = string.Join(" || ", result.Entries.Where(e => e.Alignment == Alignment.Matched && e.Outcome != FieldOutcome.Exact).Take(12).Select(e => e.Explanation.CommittedBy + " " + e.Outcome + " " + e.RootField + " " + string.Join("+", e.Left.Select(r => r.RowKey)) + "/" + string.Join("+", e.Right.Select(r => r.RowKey))));
        Assert.True(result.Summary.Exact == 145, odd);
        Assert.Equal(18, result.Summary.WithinTolerance);
        Assert.Equal(20, result.Summary.Breaks);
        Assert.Equal(85, result.Summary.LeftOnly);
        Assert.Equal(7, result.Summary.RightOnly);
        var oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "RecDiffy_Hidden_Test_Oracle.json")));
        static string Key(IEnumerable<string> ids) => string.Join("|", ids.Order(StringComparer.Ordinal));
        var expected = oracle.RootElement.GetProperty("groups").EnumerateArray().Select(group => Key(
            group.GetProperty("internal_trade_ids").EnumerateArray().Select(id => id.GetString()!)
                .Concat(group.GetProperty("cpty_report_ids").EnumerateArray().Select(id => id.GetString()!)))).ToHashSet(StringComparer.Ordinal);
        var actual = result.Entries.Select(entry => Key(result.Rows.Where(row => row.EntryId == entry.Id).Select(row => row.Row.RowKey))).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(set => !actual.Contains(set)).Take(5);
        Assert.True(expected.All(actual.Contains), string.Join(" || ", missing));
    }

    private sealed class SetEq : IEqualityComparer<string[]>
    {
        public bool Equals(string[]? x, string[]? y) => x is not null && y is not null && x.SequenceEqual(y);
        public int GetHashCode(string[] obj) => string.Join("|", obj).GetHashCode(StringComparison.Ordinal);
    }

    private static ReferenceDataSet ReferenceData(string oraclePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(oraclePath));
        var map = doc.RootElement.GetProperty("metadata").GetProperty("counterparty_alias_map");
        var leis = new Dictionary<string, string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var formats = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var party in map.EnumerateObject())
        {
            leis[party.Value.GetProperty("lei").GetString()!] = party.Name;
            aliases[party.Value.GetProperty("legal_name").GetString()!] = party.Name;
            foreach (var alias in party.Value.GetProperty("aliases").EnumerateArray())
                aliases[alias.GetString()!] = party.Name;
        }
        var dateFormats = doc.RootElement.GetProperty("metadata").GetProperty("conventions").GetProperty("date_formats_by_counterparty");
        foreach (var format in dateFormats.EnumerateObject())
            formats[format.Name] = format.Value.GetString() switch
            {
                "%Y-%m-%d" => "yyyy-MM-dd",
                "%m/%d/%Y" => "MM/dd/yyyy",
                "%d/%m/%Y" => "dd/MM/yyyy",
                "%Y%m%d" => "yyyyMMdd",
                "DMONY" => "dd-MMM-yyyy",
                var other => other ?? "yyyy-MM-dd",
            };
        return new ReferenceDataSet("sonnet", new DateOnly(2025, 6, 13), "sonnet", leis, aliases, formats);
    }

    private static IDataReader Open(string path, string sheet)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reader = ExcelReaderFactory.CreateReader(stream);
        while (!string.Equals(reader.Name, sheet, StringComparison.OrdinalIgnoreCase))
        {
            if (!reader.NextResult())
                throw new InvalidOperationException($"Missing sheet {sheet}");
        }
        return reader;
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "test-data")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo not found");
    }
}
