namespace RecDiffy.IO.Tests.OracleMatrix;

// Every workbook and oracle that spec v3 §12.2 lists, with the row keys of its two input tabs.
// HeaderRow is 1-based: the v2 workbook has three title rows above its header.
internal sealed record OracleDataset(string Name, string Workbook, string Oracle, int HeaderRow, string LeftKey, string RightKey, Func<string, IReadOnlyList<OracleCase>> Read)
{
    private readonly Lazy<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _left = new(() => SheetRows.Read(Workbook, 1, HeaderRow));
    private readonly Lazy<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _right = new(() => SheetRows.Read(Workbook, 2, HeaderRow));

    // Rows of an input tab that are not trades, such as a total line under the data. The oracle names none of them.
    public IReadOnlyList<string> NotTrades { get; init; } = [];

    // Right-side columns that hold a right row key, so an ID scramble renames them with the keys (spec v3 §10.1).
    public IReadOnlyList<string> RightKeyReferences { get; init; } = ["Cancels_Prior_Report_ID"];

    // Evidence identifiers: one value space across both sides, renamed together in the second scramble variant.
    public IReadOnlyList<string> Evidence { get; init; } = ["UTI_Exec_ID", "Shared_Market_Exec_Ref", "Parent_Link_ID"];

    public IReadOnlyList<OracleCase> Cases() => Read(Oracle);

    // The trades of each input tab: every row but those listed as NotTrades.
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Left() => Trades(_left.Value, LeftKey);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Right() => Trades(_right.Value, RightKey);

    public IReadOnlyList<string> LeftRows() => SheetRows.Keys(Left(), LeftKey);

    public IReadOnlyList<string> RightRows() => SheetRows.Keys(Right(), RightKey);

    public IReadOnlyDictionary<string, object?>? RightRow(string key) =>
        Right().FirstOrDefault(row => SheetRows.Text(row.GetValueOrDefault(RightKey)) == key);

    // Every keyed row of each tab, trades or not.
    public IReadOnlyList<string> LeftTab() => SheetRows.Keys(_left.Value, LeftKey);

    public IReadOnlyList<string> RightTab() => SheetRows.Keys(_right.Value, RightKey);

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> Trades(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string key) =>
        [.. rows.Where(row => NotTrades.Contains(SheetRows.Text(row.GetValueOrDefault(key)) ?? "", StringComparer.Ordinal) is false)];

    public override string ToString() => Name;
}

internal static class OracleDatasets
{
    private static string Dataset(string folder, string file) => Path.Combine(TestFiles.TestData, "trade-reconciliation-dataset", folder, file);

    private static string Fixture(string file) => Path.Combine(TestFiles.TestData, "trade-reconciliation-fixtures", file);

    public static OracleDataset Sonnet { get; } = new(
        "claude-sonnet-5.5",
        Dataset("claude-sonnet-5.5", "RecDiffy_Trade_Reconciliation_Dataset.xlsx"),
        Dataset("claude-sonnet-5.5", "RecDiffy_Hidden_Test_Oracle.json"),
        1,
        "Internal_Trade_ID",
        "Cpty_Report_ID",
        Oracles.Sonnet);

    public static OracleDataset Fixture20 { get; } = new(
        "fixture 2.0",
        Fixture("RecDiffy_Trade_Reconciliation_Dataset.xlsx"),
        Fixture("RecDiffy_Hidden_Test_Oracle.json"),
        1,
        "Internal_Trade_ID",
        "Cpty_Report_ID",
        Oracles.Fixture20);

    public static OracleDataset FixtureV1 { get; } = new(
        "fixture v1",
        Fixture("RecDiffy.NET-fixture-v1.xlsx"),
        Fixture("RecDiffy.NET-fixture-v1.xlsx"),
        1,
        "RowID",
        "RowID",
        Oracles.FixtureV1)
    {
        RightKeyReferences = [],
        Evidence = ["ExecutionRef"],
    };

    public static OracleDataset Opus { get; } = new(
        "claude-opus-5.5",
        Fixture("RecDiffy_Trade_Reconciliation_Dataset-claude-opus-5.5.xlsx"),
        Fixture("RecDiffy_Hidden_Test_Oracle-claude-opus-5.5.json"),
        1,
        "Internal_Trade_ID",
        "Cpty_Report_ID",
        Oracles.Opus);

    public static OracleDataset V2 { get; } = new(
        "fixture v2",
        Fixture("RecDiffy_BackOffice_Trade_Reconciliation_Dataset-v2.xlsx"),
        Fixture("RecDiffy_Hidden_Test_Oracle-v2.json"),
        4,
        "Internal_Trade_ID",
        "Cpty_Report_ID",
        Oracles.V2)
    {
        NotTrades = ["TOTAL (300 TRADES)", "TOTAL (200 LINES)"],
    };

    public static OracleDataset Qwen { get; } = new(
        "qwen-3.8",
        Dataset("qwen-3.8", "RecDiffy_Trade_Reconciliation_Dataset.xlsx"),
        Dataset("qwen-3.8", "RecDiffy_Hidden_Test_Oracle.json"),
        1,
        "Internal_Trade_ID",
        "Cpty_Report_ID",
        Oracles.Qwen);

    public static OracleDataset DeepSeek { get; } = new(
        "deepseek-v4-pro",
        Dataset("deepseek-v4-pro", "RecDiffy_Trade_Reconciliation_Dataset.xlsx"),
        Dataset("deepseek-v4-pro", "RecDiffy_Hidden_Test_Oracle.json"),
        1,
        "Internal_Trade_ID",
        "Cpty_Report_ID",
        Oracles.DeepSeek);

    public static IReadOnlyList<OracleDataset> All { get; } = [Sonnet, Fixture20, FixtureV1, Opus, V2, Qwen, DeepSeek];

    public static TheoryData<string> Names => [.. All.Select(dataset => dataset.Name)];

    public static OracleDataset Named(string name) => All.Single(dataset => dataset.Name == name);
}
