using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.IO.Tests;

public class FixProfilesBuiltInCodeTests
{
    private static readonly CsvOptions Csv = new() { HasHeader = true, Delimiter = CsvDelimiter.Comma };

    public static TheoryData<string> Names => [.. RecPushFixtures.Names];

    [Theory]
    [MemberData(nameof(Names))]
    public void A_fix_profile_built_in_code_compiles_to_the_hash_of_its_json_profile(string name)
    {
        Assert.Equal(RecPushFixtures.Profile(name).Hash, Reconciler.Compile(Built(name), RuleRegistry.CreateCore()).Hash);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_fix_profile_built_in_code_gives_the_json_profiles_result_on_its_fixtures(string name)
    {
        var folder = Path.Combine(TestFiles.TestData, "fix-reconciliation");

        var fromCode = Reconciler.Run(
            Reconciler.Compile(Built(name), RuleRegistry.CreateCore()),
            FileSources.FromCsv("left", Path.Combine(folder, $"{name}-left.csv"), Csv),
            FileSources.FromCsv("right", Path.Combine(folder, $"{name}-right.csv"), Csv),
            ReferenceDataSet.Empty);

        Assert.Equal(RecPushFixtures.Fingerprint(RecPushFixtures.Batch(name)), RecPushFixtures.Fingerprint(fromCode));
    }

    [Fact]
    public void The_typed_builder_matches_booked_executions_to_fills_as_the_json_profile_does()
    {
        var booked = Executions("left");
        var fills = Executions("right");

        var typed = RecBuilder.Between(booked, fills)
            .MatchOn(execution => execution.Counterparty, fill => fill.Counterparty)
                .And(execution => execution.Account, fill => fill.Account)
                .And(execution => execution.Symbol, fill => fill.Symbol)
                .And(execution => execution.Side, fill => fill.Side)
                .And(execution => execution.Root, fill => fill.Root)
            .Sum(execution => execution.LastQty, fill => fill.LastQty)
            .Run();

        Assert.Equal(
            Entries(RecPushFixtures.Batch("executions").Entries, row => row.RowKey, row => row.RowKey),
            Entries(typed.Result.Entries, row => booked[row.Ordinal].ExecID, row => fills[row.Ordinal].ExecID));
    }

    private static ProfileDocument Built(string name)
    {
        var built = Builder(name).Build(RuleRegistry.CreateCore());
        Assert.True(built.Succeeded, string.Join("; ", built.Problems.Select(problem => $"{problem.Location}: {problem.Code}")));
        return built.Document;
    }

    private static ProfileBuilder Builder(string name) => name switch
    {
        "orders" => Orders(),
        "fills" => Fills(),
        "executions" => BookedExecutions(),
        "allocations" => Allocations(),
        "trades" => Trades(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No FIX sample has this name."),
    };

    private static ProfileBuilder Fix(string profileId, string rowKey) =>
        ProfileBuilder.Create(profileId, "1.0.0")
            .Culture(CultureInfo.GetCultureInfo("en-US"))
            .RowKeys(rowKey, rowKey);

    private static ProfileBuilder Orders() =>
        Fix("fix-orders", "ExecID")
            .Key("Counterparty", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Key("Root", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Economic("OrdStatus", FieldType.Symbol)
            .Economic("ClOrdId", FieldType.Symbol)
            .Economic("OrigClOrdId", FieldType.Symbol)
            .Economic("ExecType", FieldType.Symbol)
            .Lifecycle("Action", FieldType.Symbol)
            .Lifecycle("Target", FieldType.Text)
            .Lifecycle("SessionDay", FieldType.Symbol)
            .Left(OrderColumns)
            .Right(OrderColumns)
            .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
            .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
            .Path("orders", path => path.NaturalKey("P1", "Counterparty", "Account", "Instrument", "Side", "Root"));

    private static SideBuilder OrderColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("Account", "Account")
            .MapSymbol("Symbol", "Instrument")
            .MapSymbol("Side", "Side")
            .MapSymbol("Root", "Root")
            .MapSymbol("Action", "Action")
            .CopyText("Target", "Target")
            .MapSymbol("OrdStatus", "OrdStatus")
            .MapSymbol("ClOrdID", "ClOrdId")
            .MapSymbol("OrigClOrdID", "OrigClOrdId")
            .MapSymbol("ExecType", "ExecType")
            .MapSymbol("SessionDay", "SessionDay")
            .ParseDecimal("OrderQty", "Quantity")
            .ParseDecimal("Price", "Price");

    private static ProfileBuilder Fills() =>
        Fix("fix-fills", "ExecID")
            .Key("Counterparty", FieldType.Symbol)
            .Identity("ExecId", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Key("Root", FieldType.Symbol)
            .Economic("ClOrdId", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Economic("TransactNanos", FieldType.Decimal, scale: 0)
            .Lifecycle("Action", FieldType.Symbol)
            .Lifecycle("Target", FieldType.Text)
            .Left(FillColumns)
            .Right(FillColumns)
            .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
            .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
            .Path("fills", path => path
                .NaturalKey("P1", "Counterparty", "ExecId")
                .Tolerance("TransactNanos", absolute: 500_000_000m));

    private static SideBuilder FillColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("ExecID", "ExecId")
            .MapSymbol("Account", "Account")
            .MapSymbol("Symbol", "Instrument")
            .MapSymbol("Side", "Side")
            .MapSymbol("Root", "Root")
            .MapSymbol("ClOrdID", "ClOrdId")
            .MapSymbol("Action", "Action")
            .CopyText("Target", "Target")
            .ParseDecimal("LastQty", "Quantity")
            .ParseDecimal("LastPx", "Price")
            .ParseDecimal("TransactNanos", "TransactNanos");

    private static ProfileBuilder BookedExecutions() =>
        Fix("fix-booked-executions", "ExecID")
            .Key("Counterparty", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Key("Root", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Left(ExecutionColumns)
            .Right(ExecutionColumns)
            .Path("executions", path => path
                .NaturalKey("P1", "Counterparty", "Account", "Instrument", "Side", "Root")
                .GroupSum("P2", Side.Right, block: ["Counterparty", "Account", "Instrument", "Side", "Root"], conserve: ["Quantity"]));

    private static SideBuilder ExecutionColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("Account", "Account")
            .MapSymbol("Symbol", "Instrument")
            .MapSymbol("Side", "Side")
            .MapSymbol("Root", "Root")
            .ParseDecimal("LastQty", "Quantity");

    private static ProfileBuilder Allocations() =>
        Fix("fix-allocations", "AllocRow")
            .Key("Counterparty", FieldType.Symbol)
            .Key("AllocRoot", FieldType.Symbol)
            .Key("AllocAccount", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("AvgPrice", FieldType.Decimal, scale: 6)
            .Lifecycle("Action", FieldType.Symbol)
            .Lifecycle("Target", FieldType.Text)
            .Left(AllocationColumns)
            .Right(AllocationColumns)
            .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
            .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
            .Path("allocations", path => path.NaturalKey("P1", "Counterparty", "AllocRoot", "AllocAccount"));

    private static SideBuilder AllocationColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("AllocRoot", "AllocRoot")
            .MapSymbol("AllocAccount", "AllocAccount")
            .MapSymbol("Action", "Action")
            .CopyText("Target", "Target")
            .ParseDecimal("AllocQty", "Quantity")
            .ParseDecimal("AvgPx", "AvgPrice");

    private static ProfileBuilder Trades() =>
        Fix("fix-trade-capture", "TradeReportID")
            .Key("Counterparty", FieldType.Symbol)
            .Key("TradeRoot", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Lifecycle("Action", FieldType.Symbol)
            .Lifecycle("Target", FieldType.Text)
            .Left(TradeColumns)
            .Right(TradeColumns)
            .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
            .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
            .Path("trades", path => path.NaturalKey("P1", "Counterparty", "TradeRoot", "Account", "Instrument", "Side"));

    private static SideBuilder TradeColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("TradeRoot", "TradeRoot")
            .MapSymbol("Account", "Account")
            .MapSymbol("Symbol", "Instrument")
            .MapSymbol("Side", "Side")
            .MapSymbol("Action", "Action")
            .CopyText("Target", "Target")
            .ParseDecimal("LastQty", "Quantity")
            .ParseDecimal("LastPx", "Price");

    private static IReadOnlyList<BookedExecution> Executions(string side) =>
        [.. RecPushFixtures.Rows("executions", side).Select(row => new BookedExecution(
            Cell(row, "ExecID"),
            Cell(row, "Counterparty"),
            Cell(row, "Account"),
            Cell(row, "Symbol"),
            Cell(row, "Side"),
            Cell(row, "Root"),
            decimal.Parse(Cell(row, "LastQty"), NumberStyles.Number, CultureInfo.InvariantCulture)))];

    private static string Cell(IReadOnlyDictionary<string, string?> row, string column) =>
        row[column] ?? throw new InvalidDataException($"The executions fixture has a blank {column}.");

    private static IReadOnlyList<string> Entries(IEnumerable<ResultEntry> entries, Func<SourceRef, string> leftId, Func<SourceRef, string> rightId) =>
        [.. entries
            .Select(entry => $"{entry.Alignment.ToInvariant()}|{entry.Composition.ToInvariant()}|{entry.Outcome.ToInvariant()}|{Ids(entry.Left, leftId)}|{Ids(entry.Right, rightId)}")
            .Order(StringComparer.Ordinal)];

    private static string Ids(IEnumerable<SourceRef> rows, Func<SourceRef, string> id) =>
        string.Join(',', rows.Select(id).Order(StringComparer.Ordinal));

    private sealed record BookedExecution(string ExecID, string Counterparty, string Account, string Symbol, string Side, string Root, decimal LastQty);
}
