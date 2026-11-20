using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Fix.Tests;

// The code behind docs/fix: each scenario's profile built with the lambda builders, proven to compile to the same hash
// as its JSON twin in test-data/fix-reconciliation, and run end to end (read and flatten FIX, push, snapshot).
// The doc pages quote these builders and messages, so a change here must change the pages too.
public class FixScenarioProfilesInCodeTests
{
    public static TheoryData<string> Scenarios => ["orders", "fills", "equity-allocations", "futures-allocations", "bond-allocations"];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void A_scenario_profile_built_in_code_compiles_to_the_hash_of_its_json_profile(string scenario)
    {
        var json = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Sample($"generic-{scenario}.profile.json"))), RuleRegistry.CreateCore());

        Assert.Equal(json.Hash, Compile(Builder(scenario)).Hash);
    }

    [Fact]
    public void Orders_chain_from_fix_text_to_a_snapshot()
    {
        var orders = new FixFlattenOptions(Filter: new FixFilter(Include: new Dictionary<int, IReadOnlyList<string>> { [35] = ["8"] }));
        var ours = FixFlattener.Read(Wire.Ack("0", "E1", "M1", null, "100") + Wire.Ack("5", "E2", "M2", "M1", "150"), Wire.Options, orders);
        var theirs = FixFlattener.Read(Wire.Ack("0", "E1", "M1", null, "100") + Wire.Ack("5", "E2", "M2", "M1", "150"), Wire.Options, orders);

        var (view, _) = Compile(Orders()).Push(ours.Rows, theirs.Rows).Snapshot();

        var replaced = Assert.Single(view.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "E2"));
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (replaced.Alignment, replaced.Outcome));
        Assert.Empty(view.Breaks);
    }

    [Fact]
    public void Fills_chain_follows_a_correction_and_shows_the_fill_only_the_broker_has()
    {
        var fills = new FixFlattenOptions(Filter: new FixFilter(Include: new Dictionary<int, IReadOnlyList<string>> { [35] = ["8"] }));
        var both = Wire.Fill("F", "F1", "C1", "100", "50.00") + Wire.Fill("F", "F2", "C1", "100", "50.00") + Wire.Fill("G", "F2c", "C1", "100", "50.10", "F2");

        var (view, _) = Compile(Fills())
            .Push(FixFlattener.Read(both, Wire.Options, fills).Rows, FixFlattener.Read(both + Wire.Fill("F", "F3", "C1", "100", "50.00"), Wire.Options, fills).Rows)
            .Snapshot();

        Assert.Equal(FieldOutcome.Exact, Assert.Single(view.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "F2c")).Outcome);
        Assert.Equal(Alignment.RightOnly, Assert.Single(view.Result.Entries, entry => entry.Right.Any(row => row.RowKey is "F3")).Alignment);
    }

    [Fact]
    public void Equity_allocations_chain_matches_instruction_accounts_to_confirmations()
    {
        var ours = FixFlattener.Read(EquityInstruction(), Wire.Options, new FixFlattenOptions(
            Grain: 78,
            Keys: new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["Key"] = [70, 79], ["TargetKey"] = [72, 79] },
            Reference: (Id: 70, Ref: 72)));
        var theirs = FixFlattener.Read(EquityConfirmations(), Wire.Options);

        var (view, _) = Compile(EquityAllocations()).Push(ours.Rows, theirs.Rows).Snapshot();

        Assert.Equal(FieldOutcome.Exact, Assert.Single(view.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "AL1|FUND-A")).Outcome);
        var netMoney = Assert.Single(Assert.Single(view.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "AL1|FUND-B")).Diffs, diff => diff.Field.Name is "NetMoney");
        Assert.Equal((FieldOutcome.WithinTolerance, 0.03m), (netMoney.Outcome, netMoney.Delta.Number));
    }

    [Fact]
    public void Futures_allocations_chain_matches_one_account_to_reshaped_rows()
    {
        var ours = FixFlattener.Read(FuturesInstruction(), Wire.Options, new FixFlattenOptions(Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["Key"] = [70, 79] }));
        var theirs = FixFlattener.Read(FuturesReport(), Wire.Options, new FixFlattenOptions(Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["Key"] = [755, 79, 366] }));

        var (view, _) = Compile(FuturesAllocations()).Push(ours.Rows, theirs.Rows).Snapshot();

        var reshaped = Assert.Single(view.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "AL1|FUND-A"));
        Assert.True(reshaped.Composition.HasFlag(Composition.OneToMany));
        Assert.Equal(FieldOutcome.Exact, reshaped.Outcome);
    }

    [Fact]
    public void Bond_allocations_chain_compares_net_money_and_accrued()
    {
        var ours = FixFlattener.Read(BondInstruction(), Wire.Options, new FixFlattenOptions(Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["Key"] = [70, 79] }));
        var theirs = FixFlattener.Read(BondReport(), Wire.Options, new FixFlattenOptions(Grain: 78, Keys: new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal) { ["Key"] = [755, 79] }));

        var (view, _) = Compile(BondAllocations()).Push(ours.Rows, theirs.Rows).Snapshot();

        Assert.Equal(FieldOutcome.WithinTolerance, Assert.Single(view.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "BJ1|FUND-A")).Outcome);
        Assert.Equal(["BJ1|FUND-B"], view.Breaks.SelectMany(entry => entry.Left).Select(row => row.RowKey), StringComparer.Ordinal);
    }

    private static ProfileBuilder Orders() =>
        ProfileBuilder.Create("fix-orders-generic-columns", "1.0.0")
            .Culture(CultureInfo.GetCultureInfo("en-US"))
            .RowKeys("17", "17")
            .Key("Counterparty", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Key("Root", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Economic("OrdStatus", FieldType.Symbol)
            .Lifecycle("Action", FieldType.Symbol)
            .Lifecycle("Target", FieldType.Text)
            .Left(OrderColumns)
            .Right(OrderColumns)
            .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
            .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
            .Path("orders", path => path.NaturalKey("P1", "Counterparty", "Account", "Instrument", "Side", "Root"));

    private static SideBuilder OrderColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("1", "Account")
            .MapSymbol("55", "Instrument")
            .MapSymbol("54", "Side", new Dictionary<string, string>(StringComparer.Ordinal) { ["1"] = "BUY", ["2"] = "SELL" })
            .MapSymbol("RootClOrdID", "Root")
            .MapSymbol("39", "OrdStatus")
            .MapSymbol("150", "Action", new Dictionary<string, string>(StringComparer.Ordinal) { ["0"] = "NEW", ["5"] = "CORR", ["D"] = "CORR", ["4"] = "CANC", ["C"] = "CANC" })
            .CopyText("PrevExecID", "Target")
            .ParseDecimal("38", "Quantity")
            .ParseDecimal("44", "Price");

    private static ProfileBuilder Fills() =>
        ProfileBuilder.Create("fix-fills-generic-columns", "1.0.0")
            .Culture(CultureInfo.GetCultureInfo("en-US"))
            .RowKeys("17", "17")
            .Key("Counterparty", FieldType.Symbol)
            .Identity("ExecId", FieldType.Symbol)
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
            .MapSymbol("17", "ExecId")
            .MapSymbol("150", "Action", new Dictionary<string, string>(StringComparer.Ordinal) { ["F"] = "NEW", ["1"] = "NEW", ["2"] = "NEW", ["G"] = "CORR", ["H"] = "CANC" })
            .CopyText("19", "Target")
            .ParseDecimal("32", "Quantity")
            .ParseDecimal("31", "Price")
            .ParseDecimal("60:ns", "TransactNanos");

    private static ProfileBuilder EquityAllocations() =>
        ProfileBuilder.Create("fix-equity-allocations-generic-columns", "1.0.0")
            .Culture(CultureInfo.GetCultureInfo("en-US"))
            .RowKeys("Key", "664")
            .Key("Counterparty", FieldType.Symbol)
            .Identity("IndividualAllocId", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("AvgPx", FieldType.Decimal, scale: 6)
            .Economic("NetMoney", FieldType.Decimal, scale: 2)
            .Economic("Commission", FieldType.Decimal, scale: 2)
            .Economic("Fees", FieldType.Decimal, scale: 2)
            .Identity("AllocType", FieldType.Symbol)
            .Lifecycle("Action", FieldType.Symbol)
            .Lifecycle("Target", FieldType.Text)
            .Left(ours => ours
                .MapSymbol("Counterparty", "Counterparty")
                .MapSymbol("467", "IndividualAllocId")
                .MapSymbol("79", "Account")
                .MapSymbol("55", "Instrument")
                .MapSymbol("626", "AllocType")
                .MapSymbol("71", "Action", new Dictionary<string, string>(StringComparer.Ordinal) { ["0"] = "NEW", ["1"] = "CORR", ["2"] = "CANC" })
                .CopyText("TargetKey", "Target")
                .ParseDecimal("80", "Quantity")
                .ParseDecimal("6", "AvgPx")
                .ParseDecimal("154", "NetMoney")
                .ParseDecimal("12", "Commission")
                .ParseDecimal("137", "Fees"))
            .Right(theirs => theirs
                .MapSymbol("Counterparty", "Counterparty")
                .MapSymbol("467", "IndividualAllocId")
                .MapSymbol("79", "Account")
                .MapSymbol("55", "Instrument")
                .MapSymbol("35", "AllocType", new Dictionary<string, string>(StringComparer.Ordinal) { ["AK"] = "2" }, passthrough: false)
                .MapSymbol("666", "Action", new Dictionary<string, string>(StringComparer.Ordinal) { ["0"] = "NEW", ["1"] = "CORR", ["2"] = "CANC" })
                .CopyText("772", "Target")
                .ParseDecimal("80", "Quantity")
                .ParseDecimal("6", "AvgPx")
                .ParseDecimal("118", "NetMoney")
                .ParseDecimal("2640", "Commission")
                .ParseDecimal("137", "Fees"))
            .Lifecycle(Side.Left, action: "Action", target: "Target", scope: "Counterparty")
            .Lifecycle(Side.Right, action: "Action", target: "Target", scope: "Counterparty")
            .Path("allocations", path => path
                .NaturalKey("P1", "Counterparty", "IndividualAllocId")
                .ReducedKey("P2", "Counterparty", "Account", "Instrument", "Quantity")
                .Tolerance("NetMoney", absolute: 0.05m, when: Condition.Equal("AllocType", "2")));

    private static ProfileBuilder FuturesAllocations() =>
        ProfileBuilder.Create("fix-futures-allocations-generic-columns", "1.0.0")
            .Culture(CultureInfo.GetCultureInfo("en-US"))
            .RowKeys("Key", "Key")
            .Key("Counterparty", FieldType.Symbol)
            .Key("IndividualAllocId", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Left(AllocationIds)
            .Right(AllocationIds)
            .Path("allocations", path => path
                .NaturalKey("P1", "Counterparty", "IndividualAllocId")
                .GroupSum("P2", Side.Right, block: ["Counterparty", "IndividualAllocId"], conserve: ["Quantity"]));

    private static SideBuilder AllocationIds(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("467", "IndividualAllocId")
            .ParseDecimal("80", "Quantity");

    private static ProfileBuilder BondAllocations() =>
        ProfileBuilder.Create("fix-bond-allocations-generic-columns", "1.0.0")
            .Culture(CultureInfo.GetCultureInfo("en-US"))
            .RowKeys("Key", "Key")
            .Key("Counterparty", FieldType.Symbol)
            .Key("Account", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Economic("NetMoney", FieldType.Decimal, scale: 2)
            .Economic("Accrued", FieldType.Decimal, scale: 2)
            .Left(BondColumns)
            .Right(BondColumns)
            .Path("allocations", path => path
                .NaturalKey("P1", "Counterparty", "Account", "Instrument", "Side")
                .Tolerance("NetMoney", absolute: 0.02m));

    private static SideBuilder BondColumns(SideBuilder side) =>
        side
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("79", "Account")
            .MapSymbol("55", "Instrument")
            .MapSymbol("54", "Side")
            .ParseDecimal("80", "Quantity")
            .ParseDecimal("154", "NetMoney")
            .ParseDecimal("742", "Accrued");

    private static string EquityInstruction() =>
        Message("J", "US", "GS",
            (70, "AL1"), (71, "0"), (626, "2"), (54, "1"), (55, "IBM"), (6, "150.25"), (78, "2"),
            (79, "FUND-A"), (80, "200"), (467, "IA1"), (154, "30050.00"), (12, "10.00"), (136, "1"), (137, "2.50"), (139, "7"),
            (79, "FUND-B"), (80, "100"), (467, "IA2"), (154, "15025.00"), (12, "5.00"), (136, "1"), (137, "1.25"), (139, "7"));

    private static string EquityConfirmations() =>
        Message("AK", "GS", "US",
            (664, "CF1"), (666, "0"), (773, "2"), (665, "4"), (467, "IA1"), (79, "FUND-A"), (80, "200"), (55, "IBM"), (6, "150.25"),
            (118, "30050.00"), (2639, "1"), (2640, "10.00"), (136, "1"), (137, "2.50"), (139, "7"))
        + Message("AK", "GS", "US",
            (664, "CF2"), (666, "0"), (773, "2"), (665, "4"), (467, "IA2"), (79, "FUND-B"), (80, "100"), (55, "IBM"), (6, "150.25"),
            (118, "15025.03"), (2639, "1"), (2640, "5.00"), (136, "1"), (137, "1.25"), (139, "7"));

    private static string FuturesInstruction() =>
        Message("J", "US", "GS", (70, "AL1"), (71, "0"), (55, "ESZ6"), (78, "1"), (79, "FUND-A"), (80, "200"), (467, "IA1"));

    private static string FuturesReport() =>
        Message("AS", "GS", "US", (755, "AR1"), (70, "AL1"), (55, "ESZ6"), (78, "2"),
            (79, "FUND-A"), (366, "10.25"), (80, "120"), (467, "IA1"),
            (79, "FUND-A"), (366, "10.50"), (80, "80"), (467, "IA1"));

    private static string BondInstruction() =>
        Message("J", "US", "GS", (70, "BJ1"), (71, "0"), (54, "1"), (55, "US912828XY"), (78, "2"),
            (79, "FUND-A"), (80, "1000000"), (154, "1002345.67"), (742, "1234.56"),
            (79, "FUND-B"), (80, "500000"), (154, "501172.84"), (742, "617.28"));

    private static string BondReport() =>
        Message("AS", "GS", "US", (755, "BR1"), (54, "1"), (55, "US912828XY"), (78, "2"),
            (79, "FUND-A"), (80, "1000000"), (154, "1002345.68"), (742, "1234.56"),
            (79, "FUND-B"), (80, "500000"), (154, "501172.84"), (742, "617.30"));

    private static ProfileBuilder Builder(string scenario) => scenario switch
    {
        "orders" => Orders(),
        "fills" => Fills(),
        "equity-allocations" => EquityAllocations(),
        "futures-allocations" => FuturesAllocations(),
        "bond-allocations" => BondAllocations(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "No FIX scenario has this name."),
    };

    private static CompiledProfile Compile(ProfileBuilder builder)
    {
        var built = builder.Build(RuleRegistry.CreateCore());
        Assert.True(built.Succeeded, string.Join("; ", built.Problems.Select(problem => $"{problem.Location}: {problem.Code}")));
        return Reconciler.Compile(built.Document, RuleRegistry.CreateCore());
    }

    private static string Message(string msgType, string sender, string target, params (int Tag, string Value)[] body) =>
        Wire.Message(msgType, "FIX.4.4", FixOptions.Soh, sender, target, body);

    private static string Sample(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "RecDiffy.NET", "test-data", "fix-reconciliation", file);
    }
}
