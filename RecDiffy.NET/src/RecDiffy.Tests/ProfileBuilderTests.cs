using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;
using static RecDiffy.ProfileProblemCodes;

namespace RecDiffy.Tests;

public class ProfileBuilderTests
{
    public static TheoryData<string> CorpusIds => [.. ProfileCorpus.Profiles.Keys];

    private static RuleRegistry Registry => RuleRegistry.CreateCore().Let(TradingRulePack.AddTo);

    [Fact]
    public void The_fixture_v1_profile_built_in_code_equals_the_json_one()
    {
        var json = ProfileDocument.Parse(Corpus("RecDiffy.Tests/WorkbookAcceptanceTests.cs:"));
        var built = Built(FixtureV1());

        Assert.Equal(ProfileShape.Of(json), ProfileShape.Of(built));
        Assert.Equal(Reconciler.Compile(json, Registry).Hash, Reconciler.Compile(built, Registry).Hash);
    }

    // The JSON profile passes all 310 expected cases in WorkbookAcceptanceTests; identical results carry that over.
    [Fact]
    public void The_fixture_v1_profile_built_in_code_gives_identical_results_on_the_workbook()
    {
        var book = Path.Combine(ProfileCorpus.RepoRoot(), "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx");
        var left = Acceptance.XlsxSheet.Read(book, 1);
        var right = Acceptance.XlsxSheet.Read(book, 2);

        var fromJson = Run(ProfileDocument.Parse(Corpus("RecDiffy.Tests/WorkbookAcceptanceTests.cs:")), left, right);
        var fromCode = Run(Built(FixtureV1()), left, right);

        Assert.Equal(310, Acceptance.XlsxSheet.Read(book, 3).Count);
        Assert.Equal(left.Count + right.Count, fromCode.Rows.Count);
        Assert.Equal(Fingerprint(fromJson), Fingerprint(fromCode));
    }

    [Fact]
    public void The_order_independence_profile_with_lifecycle_and_packages_built_in_code_equals_the_json_one()
    {
        var json = ProfileDocument.Parse(Corpus("RecDiffy.Tests/OrderIndependenceTests.cs:243"));
        var built = Built(OrderIndependence());

        Assert.Equal(ProfileShape.Of(json), ProfileShape.Of(built));
        Assert.Equal(Reconciler.Compile(json, RuleRegistry.CreateCore()).Hash, Reconciler.Compile(built, RuleRegistry.CreateCore()).Hash);
    }

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Loading_a_repository_profile_and_building_it_gives_back_the_same_document(string id)
    {
        var document = ProfileDocument.Parse(ProfileCorpus.Profiles[id]);
        var compiles = Record.Exception(() => Reconciler.Compile(document, Registry)) is null;

        var rebuilt = ProfileBuilder.From(document).Build(Registry);

        Assert.Equal(compiles, rebuilt.Succeeded);
        if (rebuilt.Succeeded)
        {
            Assert.Equal(ProfileShape.Of(document), ProfileShape.Of(rebuilt.Document));
            Assert.Equal(Reconciler.Compile(document, Registry).Hash, Reconciler.Compile(rebuilt.Document, Registry).Hash);
        }
    }

    [Fact]
    public void A_builder_never_changes_once_made()
    {
        var start = ProfileBuilder.Create("p", "1").Key("Sku", FieldType.Symbol).Path("all", path => path.NaturalKey("P1", "Sku"));
        var before = ProfileShape.Of(Built(start));

        _ = start.Key("Other", FieldType.Symbol).Left(side => side.MapSymbol("Sku", "Sku")).Midpoint(MidpointMode.AwayFromZero);

        Assert.Equal(before, ProfileShape.Of(Built(start)));
    }

    [Fact]
    public void Each_rule_lands_in_its_phase_and_keeps_its_order_within_the_phase()
    {
        var side = SideBuilder.Empty
            .Required("A")
            .ParseDecimal("x", "B")
            .MapSymbol("y", "A")
            .ParseDate("z", "C", "yyyy-MM-dd")
            .CopyText("w", "D")
            .Rule(RulePhase.Derive, "custom.rule", new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "v" });
        var document = Built(Minimal().Key("A", FieldType.Symbol).Economic("B", FieldType.Decimal).Economic("C", FieldType.Date).Field("D", FieldType.Text, FieldRole.Text).Left(side));
        var rules = document.Normalization["left"];

        AssertSequence(["symbol.map", "text.copy"], rules.Identify.Select(rule => rule.Rule));
        AssertSequence(["decimal.parse", "date.parse"], rules.Parse.Select(rule => rule.Rule));
        AssertSequence(["custom.rule"], rules.Derive.Select(rule => rule.Rule));
        AssertSequence(["required.present"], rules.Validate.Select(rule => rule.Rule));
    }

    [Fact]
    public void Calling_a_side_twice_adds_to_it_and_a_value_overload_replaces_it()
    {
        var added = Built(Minimal().Left(side => side.MapSymbol("a", "Sku")).Left(side => side.CopyText("b", "Sku")));
        var replaced = Built(Minimal().Left(side => side.MapSymbol("a", "Sku")).Left(SideBuilder.Empty.CopyText("b", "Sku")));

        Assert.Equal(2, added.Normalization["left"].Identify.Count);
        Assert.Single(replaced.Normalization["left"].Identify);
    }

    [Fact]
    public void The_builder_works_without_lambdas()
    {
        var path = PathBuilder.Create("all").NaturalKey("P1", "Sku").Tolerance("Quantity", absolute: 1m);
        var side = SideBuilder.Empty.MapSymbol("Sku", "Sku").ParseDecimal("Quantity", "Quantity");
        var result = ProfileBuilder.Create("p", "1")
            .Key("Sku", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 2)
            .Left(side)
            .Right(side)
            .Path(path)
            .Build(RuleRegistry.CreateCore());

        Assert.True(result.Succeeded, Problems(result));
    }

    [Fact]
    public void Optional_rule_parameters_are_left_out_unless_given()
    {
        var plain = Built(Minimal().Left(side => side.ResolveSide("Dir", "Qty").MapSymbol("Sku", "Sku").MapIdentifier("I", "Sku", "S")));
        var full = Built(Minimal().Left(side => side
            .ResolveSide("Dir", "Qty", perspective: "View", invertWhen: "DEALER", asset: "Sku")
            .MapSymbol("Sku", "Sku", new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "A" }, passthrough: false)
            .MapIdentifier("I", "Sku", "S", scope: "global")));

        AssertSequence(["code", "quantity"], plain.Normalization["left"].Derive[0].Strings.Keys.Order(StringComparer.Ordinal));
        AssertSequence(["from", "to"], plain.Normalization["left"].Identify[0].Strings.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(plain.Normalization["left"].Identify[0].Maps);
        AssertSequence(["asset", "code", "invertWhen", "perspective", "quantity"], full.Normalization["left"].Derive[0].Strings.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("false", full.Normalization["left"].Identify[0].Strings["passthrough"]);
        Assert.Equal("global", full.Normalization["left"].Identify[1].Strings["scope"]);
    }

    // Each trading rule's parameter names must be the ones TradingRulePack registers, or compile reports MissingParameter.
    [Fact]
    public void Trading_rules_use_the_parameter_names_the_trading_pack_registers()
    {
        var result = ProfileBuilder.Create("trading", "1")
            .Key(TradeFields.Counterparty, FieldType.Identifier)
            .Key(TradeFields.AssetClass, FieldType.Symbol)
            .Key(TradeFields.Side, FieldType.Symbol)
            .Economic(TradeFields.Quantity, FieldType.Decimal, scale: 2)
            .Economic(TradeFields.Price, FieldType.Decimal, scale: 6)
            .Left(side => side
                .ResolveAlias("Cpty", TradeFields.Counterparty)
                .MapSymbol("Asset", TradeFields.AssetClass)
                .From32nds("Px", TradeFields.Price)
                .ResolveSide(code: "Dir", quantity: "Qty", perspective: "View", invertWhen: "DEALER", asset: TradeFields.AssetClass)
                .AbsoluteQuantity("Qty", TradeFields.Quantity))
            .Right(side => side
                .ResolveLei("Lei", TradeFields.Counterparty)
                .MapSymbol("Asset", TradeFields.AssetClass)
                .ParseDecimal("Px", TradeFields.Price)
                .ResolveSide(code: "Dir", quantity: "Qty")
                .AbsoluteQuantity("Qty", TradeFields.Quantity))
            .Path("trades", path => path.NaturalKey("P1", TradeFields.Counterparty, TradeFields.Side, TradeFields.Quantity))
            .Build(Registry);

        Assert.True(result.Succeeded, Problems(result));
        Assert.Equal(RulePhase.Identify, Phase(result.Document, "left", "party.resolveAlias"));
        Assert.Equal(RulePhase.Parse, Phase(result.Document, "left", "price.from32nds"));
        Assert.Equal(RulePhase.Derive, Phase(result.Document, "left", "side.resolve"));
        Assert.Equal(RulePhase.Derive, Phase(result.Document, "left", "quantity.absolute"));
        Assert.Equal(RulePhase.Identify, Phase(result.Document, "right", "party.resolveLei"));
    }

    [Fact]
    public void Tolerances_of_every_kind_build_and_compile()
    {
        var result = Bare()
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Economic("Fees", FieldType.Decimal, scale: 2)
            .Economic("NetCash", FieldType.Decimal, scale: 2)
            .Economic("Accrued", FieldType.Decimal, scale: 2)
            .Left(side => side.MapSymbol("Sku", "Sku").ParseDecimal("Q", "Quantity").ParseDecimal("P", "Price").ParseDecimal("F", "Fees").ParseDecimal("N", "NetCash").ParseDecimal("A", "Accrued"))
            .Right(side => side.MapSymbol("Sku", "Sku").ParseDecimal("Q", "Quantity").ParseDecimal("P", "Price").ParseDecimal("F", "Fees").ParseDecimal("N", "NetCash").ParseDecimal("A", "Accrued"))
            .Path(PathBuilder.Create("all")
                .NaturalKey("P1", "Sku")
                .Tolerance("Price", absolute: 0.0001m, when: Condition.Equal("Sku", "FX"))
                .Tolerance("Quantity", relative: 0.001m)
                .Tolerance("Accrued", absolute: 1.25m, relative: 0.01m, applicableWhen: Condition.In("Sku", "Bond", "Swap"))
                .Attribution("Accrued", "trading.proRataToQuantity")
                .AttributedTolerance("NetCash", "trading.netCash")
                .ApplicableWhen("Fees", Condition.NotEqual("Sku", "Swap")))
            .Build(Registry);

        Assert.True(result.Succeeded, Problems(result));
        var diff = result.Document.Paths[0].Diff!.Fields;
        Assert.Equal("Absolute", diff["Price"].Tolerance.Kind);
        Assert.Equal("Relative", diff["Quantity"].Tolerance.Kind);
        Assert.Equal("AbsoluteOrRelative", diff["Accrued"].Tolerance.Kind);
        Assert.Equal("trading.proRataToQuantity", diff["Accrued"].Attributed);
        Assert.Equal("Attributed", diff["NetCash"].Tolerance.Kind);
        Assert.Equal("Exact", diff["Fees"].Tolerance.Kind);
        Assert.Equal("Swap", diff["Fees"].ApplicableWhen!.NotEquals);
        Assert.Equal(ProfileShape.Of(result.Document), ProfileShape.Of(ProfileDocument.Parse(result.Document.ToJson())));
    }

    [Fact]
    public void A_tolerance_with_no_bound_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PathBuilder.Create("all").Tolerance("Quantity"));
    }

    [Fact]
    public void Group_sum_lifecycle_packages_limits_and_midpoint_reach_the_document()
    {
        var document = Built(OrderIndependence().Limits(SearchLimits.Default with { MaxGroupSize = 12 }).Midpoint(MidpointMode.AwayFromZero));

        Assert.Equal(12, document.Limits.MaxGroupSize);
        Assert.Equal(MidpointMode.AwayFromZero, document.Midpoint);
        Assert.Equal("left", document.Paths[0].Passes[2].Many);
        Assert.Equal("CANC", document.Consolidation["right"].Lifecycle!.CorrPointsAt);
        AssertSequence(["LEG_1_PAY", "LEG_2_RECEIVE"], document.Consolidation["left"].Packages[0].RequiredRoles);
    }

    [Fact]
    public void Problem_DuplicateField() =>
        AssertProblem(Minimal().Key("Sku", FieldType.Identifier), DuplicateField, "fields[2]");

    [Fact]
    public void Problem_UndeclaredRuleTarget() =>
        AssertProblem(Minimal().Left(side => side.ParseDecimal("q", "Qty")), UndeclaredRuleTarget, "normalization.left.parse[0].to");

    [Fact]
    public void Problem_UndeclaredField_in_a_required_list_a_format_field_a_route_and_consolidation()
    {
        AssertProblem(Minimal().Left(side => side.Required("Sku", "Missing")), UndeclaredField, "normalization.left.validate[0].fields[1]");
        AssertProblem(Minimal().Left(side => side.ParseDateBy("d", "Sku", "Party")), UndeclaredField, "normalization.left.parse[0].formatBy");
        AssertProblem(Minimal().Path("other", path => path.RouteWhen("Region", "EU")), UndeclaredField, "paths[other].route.field");
        AssertProblem(Minimal().Lifecycle(Side.Right), UndeclaredField, "consolidation.right.lifecycle.action");
        AssertProblem(Minimal().Packages(Side.Left, PackageBuilder.Create("Sku", "Sku").Sum("Amount")), UndeclaredField, "consolidation.left.packages[0].aggregate.Amount");
    }

    [Fact]
    public void Problem_UndeclaredPassField()
    {
        AssertProblem(Minimal().Path("p", path => path.RouteWhen("Sku", "A").NaturalKey("P1", "Sku", "Size")), UndeclaredPassField, "paths[p].passes[P1].key[1]");
        AssertProblem(Minimal().Path("p", path => path.RouteWhen("Sku", "A").GroupSum("P3", Side.Left, ["Sku", "Bin"], ["Quantity"])), UndeclaredPassField, "paths[p].passes[P3].block[1]");
        AssertProblem(Minimal().Path("p", path => path.RouteWhen("Sku", "A").GroupSum("P3", Side.Left, ["Sku"], ["Weight"])), UndeclaredPassField, "paths[p].passes[P3].conserve[0]");
    }

    [Fact]
    public void Problem_UndeclaredDiffField() =>
        AssertProblem(Minimal().Path("p", path => path.RouteWhen("Sku", "A").Tolerance("Price", absolute: 1m)), UndeclaredDiffField, "paths[p].diff[Price]");

    [Fact]
    public void Problem_ToleranceMismatch()
    {
        AssertProblem(Minimal().Path("p", path => path.RouteWhen("Sku", "A").Tolerance("Sku", absolute: 1m)), ToleranceMismatch, "paths[p].diff[Sku].tolerance");
        AssertProblem(Minimal().Key("Day", FieldType.Date).Path("p", path => path.RouteWhen("Sku", "A").AttributedTolerance("Day", "trading.netCash")), ToleranceMismatch, "paths[p].diff[Day].tolerance");
    }

    [Fact]
    public void Problem_DuplicatePath_and_DuplicatePass()
    {
        AssertProblem(Minimal().Path("all", path => path.RouteWhen("Sku", "A")), DuplicatePath, "paths[all]");
        AssertProblem(Minimal().Path("p", path => path.RouteWhen("Sku", "A").NaturalKey("P1", "Sku").ReducedKey("P1", "Sku")), DuplicatePass, "paths[p].passes[P1]");
    }

    [Fact]
    public void Problem_MissingPath() =>
        AssertProblem(ProfileBuilder.Create("p", "1").Key("Sku", FieldType.Symbol), MissingPath, "paths");

    [Fact]
    public void Problem_UnknownRule_with_a_registry()
    {
        var result = Minimal().Left(side => side.Rule(RulePhase.Parse, "no.such.rule")).Build(RuleRegistry.CreateCore());

        Assert.Contains(result.Problems, problem => problem is { Code: "UnknownRule", Location: "compile" });
        Assert.True(Minimal().Left(side => side.Rule(RulePhase.Parse, "no.such.rule")).Build().Succeeded);
    }

    [Fact]
    public void Problem_MissingParameter_with_a_registry()
    {
        var result = Minimal().Left(side => side.Rule(RulePhase.Identify, "identifier.map", new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "s", ["to"] = "Sku" })).Build(RuleRegistry.CreateCore());

        Assert.Contains(result.Problems, problem => problem is { Code: "MissingParameter", Location: "compile" } && problem.Message.Contains("scheme", StringComparison.Ordinal));
    }

    [Fact]
    public void Structural_problems_are_reported_before_compile_runs()
    {
        var result = Minimal().Key("Sku", FieldType.Symbol).Left(side => side.Rule(RulePhase.Parse, "no.such.rule")).Build(RuleRegistry.CreateCore());

        AssertSequence([DuplicateField], result.Problems.Select(problem => problem.Code));
        Assert.Null(result.Document);
    }

    [Fact]
    public void Every_problem_has_a_code_a_message_and_a_location()
    {
        var result = ProfileBuilder.Create("p", "1")
            .Key("A", FieldType.Symbol)
            .Key("A", FieldType.Symbol)
            .Left(side => side.ParseDecimal("x", "Nope"))
            .Path("p", path => path.NaturalKey("P1", "Missing").Tolerance("Ghost", absolute: 1m))
            .Build();

        Assert.False(result.Succeeded);
        AssertSequence([DuplicateField, UndeclaredRuleTarget, UndeclaredPassField, UndeclaredDiffField], result.Problems.Select(problem => problem.Code));
        Assert.All(result.Problems, problem => Assert.False(string.IsNullOrWhiteSpace(problem.Message) || string.IsNullOrWhiteSpace(problem.Location)));
    }

    private static void AssertProblem(ProfileBuilder builder, string code, string location)
    {
        var result = builder.Build();

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, problem => problem.Code == code && problem.Location == location);
    }

    private static void AssertSequence(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(expected, actual, StringComparer.Ordinal);

    private static ProfileBuilder Bare() =>
        ProfileBuilder.Create("minimal", "1.0.0")
            .Key("Sku", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 2);

    // A small valid profile the problem tests break one thing in at a time.
    private static ProfileBuilder Minimal() =>
        Bare()
            .Path("all", path => path.NaturalKey("P1", "Sku"));

    private static ProfileBuilder FixtureV1() =>
        ProfileBuilder.Create("fixture-v1", "1.0.0")
            .RowKeys(left: "RowID", right: "RowID")
            .Key("Counterparty", FieldType.Identifier)
            .Key("Instrument", FieldType.Identifier)
            .Key("Asset", FieldType.Symbol)
            .Key("Side", FieldType.Symbol)
            .Key("TradeDate", FieldType.Date)
            .Economic("SettleDate", FieldType.Date)
            .Economic("MaturityDate", FieldType.Date)
            .Economic("Currency", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 2)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Economic("FixedRate", FieldType.Decimal, scale: 6)
            .Identity("ExecRef", FieldType.Identifier)
            .Left(side => FixtureSide(side)
                .ResolveSide(code: "Direction", quantity: "Quantity", asset: "Asset")
                .AbsoluteQuantity("Quantity", "Quantity"))
            .Right(side => FixtureSide(side)
                .ResolveSide(code: "Direction", quantity: "Quantity", perspective: "Perspective", invertWhen: "Sender", asset: "Asset")
                .AbsoluteQuantity("Quantity", "Quantity"))
            .Path("trades", path => path
                .NaturalKey("shared", "ExecRef")
                .NaturalKey("natural", "Counterparty", "Instrument", "Asset", "TradeDate", "Side", "Quantity"));

    private static SideBuilder FixtureSide(SideBuilder side) =>
        side
            .ResolveAlias("CounterpartyName", "Counterparty")
            .MapIdentifier("InstrumentID", "Instrument", "INST")
            .MapSymbol("AssetClass", "Asset")
            .MapSymbol("Currency", "Currency")
            .MapIdentifier("ExecutionRef", "ExecRef", "EXEC")
            .ParseDate("TradeDate", "TradeDate", "yyyy-MM-dd")
            .ParseDate("SettleDate", "SettleDate", "yyyy-MM-dd")
            .ParseDate("MaturityDate", "MaturityDate", "yyyy-MM-dd")
            .ParseDecimal("PriceOrRate", "Price")
            .ParseDecimal("FixedRate", "FixedRate");

    private static ProfileBuilder OrderIndependence()
    {
        var side = SideBuilder.Empty
            .MapSymbol("Counterparty", "Counterparty")
            .MapSymbol("Instrument", "Instrument")
            .MapSymbol("Side", "Side")
            .MapSymbol("Currency", "Currency")
            .MapIdentifier("Uti", "Uti", "UTI")
            .MapSymbol("EventAction", "EventAction")
            .CopyText("EventTarget", "EventTarget")
            .CopyText("PackageRef", "PackageRef")
            .MapSymbol("LegRole", "LegRole")
            .ParseDate("TradeDate", "TradeDate", "yyyy-MM-dd")
            .ParseDecimal("Quantity", "Quantity")
            .ParseDecimal("Price", "Price")
            .ParseDecimal("NetCash", "NetCash");
        return ProfileBuilder.Create("order-independence", "1.0.0")
            .RowKeys(left: "Id", right: "Id")
            .Key("Counterparty", FieldType.Symbol)
            .Key("Instrument", FieldType.Symbol)
            .Key("TradeDate", FieldType.Date)
            .Key("Side", FieldType.Symbol)
            .Economic("Currency", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 2)
            .Economic("Price", FieldType.Decimal, scale: 6)
            .Economic("NetCash", FieldType.Decimal, scale: 2)
            .Identity("Uti", FieldType.Identifier)
            .Lifecycle("EventAction", FieldType.Symbol)
            .Lifecycle("EventTarget", FieldType.Text)
            .Package("PackageRef", FieldType.Text)
            .Package("LegRole", FieldType.Symbol)
            .Left(side)
            .Right(side)
            .Packages(Side.Left, PackageBuilder.Create(link: "PackageRef", role: "LegRole")
                .RequireRoles("LEG_1_PAY", "LEG_2_RECEIVE")
                .Same("Quantity")
                .FromRole("Price", "LEG_1_PAY")
                .FromRole("Side", "LEG_1_PAY")
                .DifferenceByRole("NetCash", "LEG_1_PAY", "LEG_2_RECEIVE"))
            .Lifecycle(Side.Right, action: "EventAction", target: "EventTarget", scope: "Counterparty", corrPointsAt: "CANC")
            .Path("trades", path => path
                .NaturalKey("P1", "Uti")
                .NaturalKey("P2", "Counterparty", "Instrument", "TradeDate", "Side", "Quantity")
                .GroupSum("P3", Side.Left, ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], ["Quantity", "NetCash"])
                .ReducedKey("P4", "Counterparty", "Instrument", "TradeDate", "Side"));
    }

    private static ProfileDocument Built(ProfileBuilder builder)
    {
        var result = builder.Build();
        Assert.True(result.Succeeded, Problems(result));
        return result.Document;
    }

    private static string Problems(ProfileBuildResult result) =>
        string.Join("; ", result.Problems.Select(problem => $"{problem.Code} at {problem.Location}: {problem.Message}"));

    private static RulePhase Phase(ProfileDocument document, string side, string rule)
    {
        var phases = document.Normalization[side];
        return (phases.Identify.Any(call => call.Rule == rule), phases.Parse.Any(call => call.Rule == rule), phases.Derive.Any(call => call.Rule == rule)) switch
        {
            (true, _, _) => RulePhase.Identify,
            (_, true, _) => RulePhase.Parse,
            (_, _, true) => RulePhase.Derive,
            _ => RulePhase.Validate,
        };
    }

    private static string Corpus(string prefix) =>
        Assert.Single(ProfileCorpus.Profiles, pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Value;

    private static ReconciliationResult Run(ProfileDocument document, IReadOnlyList<IReadOnlyDictionary<string, object?>> left, IReadOnlyList<IReadOnlyDictionary<string, object?>> right) =>
        Reconciler.Run(
            Reconciler.Compile(document, Registry),
            RecordSources.FromObjects("left", left, FixtureMap()),
            RecordSources.FromObjects("right", right, FixtureMap()),
            FixtureReference());

    private static IReadOnlyList<string> Fingerprint(ReconciliationResult result) =>
    [
        .. result.Entries.Select(entry => string.Join(
            "|",
            entry.Id,
            entry.PathId,
            entry.Alignment,
            entry.Composition,
            entry.Outcome,
            string.Join(",", entry.Left.Select(member => member.RowKey).Order(StringComparer.Ordinal)),
            string.Join(",", entry.Right.Select(member => member.RowKey).Order(StringComparer.Ordinal)),
            string.Join(",", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}:{diff.Delta.Days.ToString(CultureInfo.InvariantCulture)}")),
            entry.Explanation.CommittedBy)).Order(StringComparer.Ordinal),
        .. result.Rows.Select(row => $"{row.Row.Source}:{row.Row.RowKey}|{row.Disposition}|{row.Validity}|{row.EntryId}|{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code))}").Order(StringComparer.Ordinal),
    ];

    private static SourceMap<IReadOnlyDictionary<string, object?>> FixtureMap() =>
        new SourceMap<IReadOnlyDictionary<string, object?>>()
            .Text("RowID", row => Cell(row, "RowID"))
            .Text("ExecutionRef", row => Cell(row, "ExecutionRef"))
            .Text("CounterpartyName", row => Cell(row, "CounterpartyName"))
            .Text("AssetClass", row => Cell(row, "AssetClass"))
            .Text("InstrumentID", row => Cell(row, "InstrumentID"))
            .Text("TradeDate", row => Cell(row, "TradeDate"))
            .Text("SettleDate", row => Cell(row, "SettleDate"))
            .Text("MaturityDate", row => Cell(row, "MaturityDate"))
            .Text("Direction", row => Cell(row, "Direction"))
            .Text("Perspective", row => Cell(row, "Perspective"))
            .Text("Currency", row => Cell(row, "Currency"))
            .Decimal("Quantity", row => Number(row, "Quantity"))
            .Decimal("PriceOrRate", row => Number(row, "PriceOrRate"))
            .Decimal("FixedRate", row => Number(row, "FixedRate"));

    private static string? Cell(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) ? value?.ToString() : null;

    private static decimal? Number(IReadOnlyDictionary<string, object?> row, string column) =>
        decimal.TryParse(Cell(row, column), CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static ReferenceDataSet FixtureReference()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elm International Bank"] = "ELM",
            ["ELM INTL"] = "ELM",
            ["Granite Markets PLC"] = "GRANITE",
            ["GRANITE MKT"] = "GRANITE",
            ["Falcon Asset Partners"] = "FALCON",
            ["Falcon AP"] = "FALCON",
            ["Beacon Capital LLP"] = "BEACON",
            ["Beacon Cap"] = "BEACON",
            ["Harbor Hedge Fund LP"] = "HARBOR",
            ["Harbor Fund"] = "HARBOR",
            ["Alder Bank NA"] = "ALDER",
            ["ALDER BK"] = "ALDER",
            ["Cedar Securities Ltd"] = "CEDAR",
            ["CEDAR SEC"] = "CEDAR",
            ["Driftwood Fund LP"] = "DRIFTWOOD",
            ["Driftwood HF"] = "DRIFTWOOD",
        };
        return ReferenceDataSet.Create("fixture-v1", new DateOnly(2026, 11, 4), "fixture-v1", aliases, aliases, new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
