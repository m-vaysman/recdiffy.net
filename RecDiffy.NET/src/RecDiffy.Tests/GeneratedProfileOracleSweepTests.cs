using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class GeneratedProfileOracleSweepTests
{
    private static readonly RuleRegistry Registry = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    private static readonly ReferenceDataSet ReferenceData = ReferenceDataSet.Create(
        "t1-ref",
        new DateOnly(2025, 6, 1),
        "hash-t1",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["549300AAA11111111111"] = "CP_A",
            ["549300BBB22222222222"] = "CP_B",
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ALIAS_A"] = "CP_A",
            ["ALIAS_B"] = "CP_B",
        },
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CP_A"] = "yyyy-MM-dd",
            ["CP_B"] = "dd/MM/yyyy",
        });

    public static IEnumerable<object[]> Seeds() =>
        Enumerable.Range(0, 24).Select(seed => new object[] { seed });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generated_profile_matches_slow_reference_oracle(int seed)
    {
        var spec = ProfileChoice.FromSeed(seed);
        var document = BuildProfile(spec);
        var compiled = Reconciler.Compile(document, Registry);
        var plan = GeneratedRun.Create(seed, spec);

        using var leftTable = ToDataTable(plan.Left);
        using var rightTable = ToDataTable(plan.Right);
        var leftSource = CreateSource("left", plan.Left, leftTable, spec.LeftSource);
        var rightSource = CreateSource("right", plan.Right, rightTable, spec.RightSource);

        var result = Reconciler.Run(compiled, leftSource, rightSource, ReferenceData);

        VerifyRows(plan, result);
        VerifyEntries(plan, result);
        VerifySideBySide(plan, result);
    }

    [Fact]
    public void Sweep_covers_every_targeted_profile_dimension()
    {
        var choices = Enumerable.Range(0, 24).Select(ProfileChoice.FromSeed).ToArray();

        Assert.Contains(choices, c => c.RefScheme is "broker");
        Assert.Contains(choices, c => c.RefScheme is "shared");
        Assert.Contains(choices, c => c.RefScheme is "ISIN");
        Assert.Contains(choices, c => c.RouteByInstrument);
        Assert.Contains(choices, c => !c.RouteByInstrument);
        Assert.Contains(choices, c => c.UsePartyRules);
        Assert.Contains(choices, c => c.UseFormatBy);
        Assert.Contains(choices, c => c.Use32nds);
        Assert.Contains(choices, c => c.UseSideAndAbsQty);
        Assert.Contains(choices, c => c.UseNetCashAttribution);
        Assert.Contains(choices, c => c.LeftSource is SourceKind.DataReader || c.RightSource is SourceKind.DataReader);
    }

    private static void VerifyRows(GeneratedRun plan, ReconciliationResult result)
    {
        var actualRows = result.Rows.ToDictionary(r => (r.Row.Source, r.Row.RowKey));
        Assert.Equal(plan.ExpectedRows.Count, actualRows.Count);

        foreach (var (key, expected) in plan.ExpectedRows)
        {
            Assert.True(actualRows.TryGetValue(key, out var actual), $"Missing row {key}");
            Assert.Equal(expected.Disposition, actual.Disposition);
            Assert.Equal(expected.Validity, actual.Validity);
            Assert.Equal(expected.Suppression, actual.Suppression);
        }
    }

    private static void VerifyEntries(GeneratedRun plan, ReconciliationResult result)
    {
        string Signature(IEnumerable<(string Source, string RowKey)> left, IEnumerable<(string Source, string RowKey)> right) =>
            string.Join(",", left.Concat(right).Select(m => $"{m.Source}:{m.RowKey}").OrderBy(s => s, StringComparer.Ordinal));

        var actualByMembers = result.Entries.ToDictionary(
            e => Signature(e.Left.Select(m => (m.Source, m.RowKey)), e.Right.Select(m => (m.Source, m.RowKey))),
            StringComparer.Ordinal);
        Assert.Equal(plan.ExpectedEntries.Count, actualByMembers.Count);

        foreach (var expected in plan.ExpectedEntries)
        {
            var sig = Signature(expected.Left, expected.Right);
            Assert.True(actualByMembers.TryGetValue(sig, out var actual), $"Missing entry for members [{sig}]");
            Assert.Equal(expected.Alignment, actual.Alignment);
            Assert.Equal(expected.Composition, actual.Composition);
            Assert.Equal(expected.Outcome, actual.Outcome);
            if (expected.CommittedBy is not null)
                Assert.Equal(expected.CommittedBy, actual.Explanation.CommittedBy);

            foreach (var (field, expectedDiff) in expected.Diffs)
            {
                if (expectedDiff.Outcome is FieldOutcome.Exact)
                {
                    Assert.DoesNotContain(actual.Diffs, d => d.Field.Name == field);
                }
                else
                {
                    var diff = Assert.Single(actual.Diffs, d => d.Field.Name == field);
                    Assert.Equal(expectedDiff.Outcome, diff.Outcome);
                    Assert.Equal(expectedDiff.AttributedTo, diff.AttributedTo);
                }
            }
        }
    }

    private static void VerifySideBySide(GeneratedRun plan, ReconciliationResult result)
    {
        var sbs = result.BuildSideBySide(new SideBySideSpec([], [], [new("Quantity"), new("Price"), new("NetCash")], null));
        var line = Assert.Single(sbs.Lines);
        var qtyCell = line.Cells["Quantity"];
        var priceCell = line.Cells["Price"];

        Assert.Equal(plan.ExpectedMatchedQtyLeft + plan.ExpectedLeftOnlyQty, qtyCell.Left);
        Assert.Equal(plan.ExpectedMatchedQtyRight + plan.ExpectedRightOnlyQty, qtyCell.Right);
        Assert.Equal(-plan.ExpectedLeftOnlyQty, qtyCell.LeftOnly);
        Assert.Equal(plan.ExpectedRightOnlyQty, qtyCell.RightOnly);
        Assert.Equal(-plan.ExpectedLeftOnlyPrice, priceCell.LeftOnly);
    }

    private static RecordSource CreateSource(string name, IReadOnlyList<RawRow> rows, DataTable table, SourceKind kind) =>
        kind switch
        {
            SourceKind.Objects => RecordSources.FromObjects(name, rows, RawMap),
            SourceKind.DataReader => RecordSources.FromDataReader(name, table.CreateDataReader, new TabularOptions()),
            _ => throw new InvalidOperationException($"Unknown source kind {kind}"),
        };

    private static readonly SourceMap<RawRow> RawMap = new SourceMap<RawRow>()
        .Text("Id", r => r.Id)
        .Text("Cpty", r => r.Cpty)
        .Text("Lei", r => r.Lei)
        .Text("Alias", r => r.Alias)
        .Text("Asset", r => r.Asset)
        .Text("Instr", r => r.Instr)
        .Text("Ref", r => r.Ref)
        .Text("TradeDate", r => r.TradeDate)
        .Text("SettleDate", r => r.SettleDate)
        .Text("SideCode", r => r.SideCode)
        .Text("Qty", r => r.Qty)
        .Text("Price", r => r.Price)
        .Text("Cash", r => r.Cash)
        .Text("Note", r => r.Note)
        .Text("Action", r => r.Action)
        .Text("Target", r => r.Target)
        .Text("Pkg", r => r.Pkg)
        .Text("Role", r => r.Role);

    private static DataTable ToDataTable(IReadOnlyList<RawRow> rows)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        string[] columns = ["Id", "Cpty", "Lei", "Alias", "Asset", "Instr", "Ref", "TradeDate", "SettleDate", "SideCode", "Qty", "Price", "Cash", "Note", "Action", "Target", "Pkg", "Role"];
#pragma warning disable IDISP004 // The table owns the columns and rows it adds.
        foreach (var col in columns)
            table.Columns.Add(col, typeof(object));

        foreach (var r in rows)
        {
            table.Rows.Add(
                Cell(r.Id),
                Cell(r.Cpty),
                Cell(r.Lei),
                Cell(r.Alias),
                Cell(r.Asset),
                Cell(r.Instr),
                Cell(r.Ref),
                Cell(r.TradeDate),
                Cell(r.SettleDate),
                Cell(r.SideCode),
                Cell(r.Qty),
                Cell(r.Price),
                Cell(r.Cash),
                Cell(r.Note),
                Cell(r.Action),
                Cell(r.Target),
                Cell(r.Pkg),
                Cell(r.Role));
        }
#pragma warning restore IDISP004

        return table;
    }

    private static object Cell(string? value) => value is null ? DBNull.Value : value;

    private static ProfileDocument BuildProfile(ProfileChoice spec)
    {
        var fields = new List<FieldDeclaration>
        {
            new("Counterparty", spec.UsePartyRules ? FieldType.Identifier : FieldType.Symbol, FieldRole.Key, 0),
            new("AssetClass", FieldType.Symbol, FieldRole.Key, 0),
            new("Instrument", FieldType.Identifier, FieldRole.Key, 0),
            new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
            new("TradeDate", FieldType.Date, FieldRole.Key, 0),
            new("SettleDate", FieldType.Date, FieldRole.Economic, 0),
            new("Side", FieldType.Symbol, FieldRole.Economic, 0),
            new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
            new("Price", FieldType.Decimal, FieldRole.Economic, 4),
            new("Accrued", FieldType.Decimal, FieldRole.Economic, 2),
            new("Fees", FieldType.Decimal, FieldRole.Economic, 2),
            new("NetCash", FieldType.Decimal, FieldRole.Economic, 2),
            new("Note", FieldType.Text, FieldRole.Text, 0),
            new("EventAction", FieldType.Text, FieldRole.Lifecycle, 0),
            new("EventTarget", FieldType.Text, FieldRole.Lifecycle, 0),
            new("PackageRef", FieldType.Text, FieldRole.Package, 0),
            new("LegRole", FieldType.Symbol, FieldRole.Package, 0),
        };

        PassDeclaration[] passes =
        [
            new("P1", "match.sharedRef", [], [], [], null, null),
            new("P2", "match.naturalKey", ["Counterparty", "Instrument", "TradeDate", "Quantity"], [], [], null, null),
            new("P3", "match.groupSum", [], ["Counterparty", "Instrument", "TradeDate", "Price"], ["Quantity", "NetCash"], "left", 6),
        ];

        var diffFields = new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
        {
            ["SettleDate"] = new(
                new ToleranceDeclaration(
                    "Days",
                    null,
                    null,
                    null,
                    new WhenClause("Note", "", null, null),
                    2),
                null,
                null),
            ["Price"] = new(
                new ToleranceDeclaration("Absolute", 0.50m, null, null, null),
                null,
                null),
            ["NetCash"] = spec.UseNetCashAttribution
                ? new(
                    new ToleranceDeclaration("Attributed", null, null, "trading.netCash", null),
                    "trading.netCash",
                    null)
                : new(
                    new ToleranceDeclaration("Absolute", 50.00m, null, null, null),
                    null,
                    null),
        };
        var diff = new DiffDeclaration(diffFields);

        PathDeclaration[] paths = spec.RouteByInstrument
            ?
            [
                new("bond-1", new RouteDeclaration("route.byField", "Instrument", "XS000001"), passes, diff),
                new("bond-2", new RouteDeclaration("route.byField", "Instrument", "XS000002"), passes, diff),
            ]
            :
            [
                new("all", new RouteDeclaration("route.all", null, null), passes, diff),
            ];

        return new ProfileDocument
        {
            FormatVersion = 1,
            ProfileId = "t1-generated",
            Version = "1.0.0",
            Midpoint = MidpointMode.ToEven,
            Culture = "",
            SourceKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["left"] = "Id", ["right"] = "Id" },
            Fields = fields,
            Normalization = new Dictionary<string, NormalizationSide>(StringComparer.Ordinal)
            {
                ["left"] = BuildNormalization(spec, includePackage: true, includeLifecycle: false),
                ["right"] = BuildNormalization(spec, includePackage: false, includeLifecycle: true),
            },
            Consolidation = new Dictionary<string, ConsolidationSide>(StringComparer.Ordinal)
            {
                ["left"] = new ConsolidationSide(
                    null,
                    [
                        new PackagePlan(
                            "PackageRef",
                            "LegRole",
                            ["PAY", "REC"],
                            [
                                new FieldAggregate("Counterparty", AggregateKind.Same, null, null),
                                new FieldAggregate("AssetClass", AggregateKind.Same, null, null),
                                new FieldAggregate("Instrument", AggregateKind.Same, null, null),
                                new FieldAggregate("TradeDate", AggregateKind.Same, null, null),
                                new FieldAggregate("SettleDate", AggregateKind.Same, null, null),
                                new FieldAggregate("Quantity", AggregateKind.Same, null, null),
                                new FieldAggregate("Price", AggregateKind.FromRole, "PAY", null),
                                new FieldAggregate("NetCash", AggregateKind.DifferenceByRole, "PAY", "REC"),
                            ]),
                    ]),
                ["right"] = new ConsolidationSide(
                    new LifecyclePlan("EventAction", "EventTarget", "Counterparty", "CANC"),
                    []),
            },
            Limits = new SearchLimits(8, 12, 8, 128),
            Paths = paths,
        };
    }

    private static NormalizationSide BuildNormalization(ProfileChoice spec, bool includePackage, bool includeLifecycle)
    {
        var identify = new List<RuleCall>();
        if (spec.UsePartyRules)
        {
            identify.Add(Rule("party.resolveLei", ("from", "Lei"), ("to", "Counterparty")));
            identify.Add(Rule("party.resolveAlias", ("from", "Alias"), ("to", "Counterparty")));
        }
        else
        {
            identify.Add(Rule("symbol.map", ("from", "Cpty"), ("to", "Counterparty")));
        }

        identify.Add(Rule("symbol.map", ("from", "Asset"), ("to", "AssetClass")));
        identify.Add(Rule("identifier.map", ("from", "Instr"), ("to", "Instrument"), ("scheme", "ISIN")));
        identify.Add(Rule("identifier.map", ("from", "Ref"), ("to", "TradeRef"), ("scheme", spec.RefScheme)));
        identify.Add(Rule("text.copy", ("from", "Note"), ("to", "Note")));

        if (includePackage)
        {
            identify.Add(Rule("text.copy", ("from", "Pkg"), ("to", "PackageRef")));
            identify.Add(Rule("symbol.map", ("from", "Role"), ("to", "LegRole")));
        }

        if (includeLifecycle)
        {
            identify.Add(Rule("text.copy", ("from", "Action"), ("to", "EventAction")));
            identify.Add(Rule("text.copy", ("from", "Target"), ("to", "EventTarget")));
        }

        var parse = new List<RuleCall>();
        if (spec.UseFormatBy)
        {
            parse.Add(Rule("date.parse", ("from", "TradeDate"), ("to", "TradeDate"), ("formatBy", "Counterparty")));
            parse.Add(Rule("date.parse", ("from", "SettleDate"), ("to", "SettleDate"), ("formatBy", "Counterparty")));
        }
        else
        {
            parse.Add(RuleWithList("date.parse", "formats", ["yyyy-MM-dd", "dd/MM/yyyy"], ("from", "TradeDate"), ("to", "TradeDate")));
            parse.Add(RuleWithList("date.parse", "formats", ["yyyy-MM-dd", "dd/MM/yyyy"], ("from", "SettleDate"), ("to", "SettleDate")));
        }

        if (spec.Use32nds)
            parse.Add(Rule("price.from32nds", ("from", "Price"), ("to", "Price")));
        else
            parse.Add(Rule("decimal.parse", ("from", "Price"), ("to", "Price")));

        parse.Add(Rule("decimal.parse", ("from", "Accrued"), ("to", "Accrued")));
        parse.Add(Rule("decimal.parse", ("from", "Fees"), ("to", "Fees")));
        parse.Add(Rule("decimal.parse", ("from", "Cash"), ("to", "NetCash")));

        var derive = new List<RuleCall>();
        if (spec.UseSideAndAbsQty)
        {
            derive.Add(Rule("side.resolve", ("code", "SideCode"), ("quantity", "Qty")));
            derive.Add(Rule("quantity.absolute", ("from", "Qty"), ("to", "Quantity")));
        }
        else
        {
            identify.Add(Rule("symbol.map", ("from", "SideCode"), ("to", "Side")));
            parse.Add(Rule("decimal.parse", ("from", "Qty"), ("to", "Quantity")));
        }

        return new NormalizationSide(identify, parse, derive, []);
    }

    private static RuleCall Rule(string name, params (string Key, string Value)[] strings) =>
        new(
            name,
            null,
            strings.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);

    private static RuleCall RuleWithList(string name, string listKey, IReadOnlyList<string> listVal, params (string Key, string Value)[] strings) =>
        new(
            name,
            null,
            strings.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [listKey] = listVal }.ToImmutableDictionary(StringComparer.Ordinal),
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);

    private enum SourceKind
    {
        Objects,
        DataReader,
    }

    private sealed record ProfileChoice(
        string RefScheme,
        bool RouteByInstrument,
        bool UsePartyRules,
        bool UseFormatBy,
        bool Use32nds,
        bool UseSideAndAbsQty,
        bool UseNetCashAttribution,
        SourceKind LeftSource,
        SourceKind RightSource)
    {
        private static readonly string[] Schemes = ["shared", "ISIN", "broker"];

        public static ProfileChoice FromSeed(int seed) =>
            new(
                RefScheme: Schemes[seed % Schemes.Length],
                RouteByInstrument: seed % 2 == 0,
                UsePartyRules: (seed / 2) % 2 == 0,
                UseFormatBy: (seed / 3) % 2 == 0,
                Use32nds: (seed / 4) % 2 == 0,
                UseSideAndAbsQty: (seed / 5) % 2 == 0,
                UseNetCashAttribution: (seed / 6) % 2 == 0,
                LeftSource: seed % 3 == 0 ? SourceKind.DataReader : SourceKind.Objects,
                RightSource: seed % 3 == 1 ? SourceKind.DataReader : SourceKind.Objects);
    }

    private sealed record RawRow(
        string Id,
        string Cpty,
        string? Lei,
        string? Alias,
        string Asset,
        string Instr,
        string? Ref,
        string? TradeDate,
        string? SettleDate,
        string? SideCode,
        string? Qty,
        string? Price,
        string? Cash,
        string? Note = null,
        string? Action = null,
        string? Target = null,
        string? Pkg = null,
        string? Role = null);

    private sealed record ExpectedRow(Disposition Disposition, InputValidity Validity, SuppressionReason? Suppression);

    private sealed record ExpectedFieldDiff(FieldOutcome Outcome, IReadOnlyList<string> AttributedTo);

    private sealed record ExpectedEntry(
        IReadOnlyList<(string Source, string RowKey)> Left,
        IReadOnlyList<(string Source, string RowKey)> Right,
        Alignment Alignment,
        Composition Composition,
        FieldOutcome Outcome,
        string? CommittedBy,
        IReadOnlyDictionary<string, ExpectedFieldDiff> Diffs);

    private sealed record GeneratedRun(
        IReadOnlyList<RawRow> Left,
        IReadOnlyList<RawRow> Right,
        IReadOnlyDictionary<(string Source, string RowKey), ExpectedRow> ExpectedRows,
        IReadOnlyList<ExpectedEntry> ExpectedEntries,
        decimal ExpectedMatchedQtyLeft,
        decimal ExpectedMatchedQtyRight,
        decimal ExpectedLeftOnlyQty,
        decimal ExpectedRightOnlyQty,
        decimal ExpectedLeftOnlyPrice)
    {
        public static GeneratedRun Create(int seed, ProfileChoice spec)
        {
            var random = new Random(seed * 101 + 7);
            var left = new List<RawRow>();
            var right = new List<RawRow>();
            var expectedRows = new Dictionary<(string Source, string RowKey), ExpectedRow>();
            var expectedEntries = new List<ExpectedEntry>();
            var matchedQtyLeft = 0m;
            var matchedQtyRight = 0m;
            var leftOnlyQty = 0m;
            var rightOnlyQty = 0m;
            var leftOnlyPrice = 0m;

            static (string? Lei, string? Alias) PartyFields(string canonical, bool useAlias) =>
                canonical is "CP_A"
                    ? (useAlias ? ((string?)"", (string?)"ALIAS_A") : ("549300AAA11111111111", ""))
                    : (useAlias ? (null, "ALIAS_B") : ("549300BBB22222222222", null));

            static string FormatDate(string party, DateOnly date) =>
                party is "CP_B"
                    ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string FormatPrice(decimal whole, int half32nds) =>
                spec.Use32nds
                    ? $"{whole.ToString("F0", CultureInfo.InvariantCulture)}-{half32nds.ToString("D2", CultureInfo.InvariantCulture)}"
                    : (whole + half32nds / 32m).ToString("F4", CultureInfo.InvariantCulture);

            string SideToken() => spec.UseSideAndAbsQty ? "B" : "BUY";

            RawRow MakeRow(
                string id,
                string cpty,
                bool useAlias,
                string instr,
                string? reference,
                string? tradeDate,
                string? settleDate,
                string? sideCode,
                string? qty,
                string? price,
                string? cash,
                string? note = null,
                string? action = null,
                string? target = null,
                string? pkg = null,
                string? role = null)
            {
                var (lei, alias) = PartyFields(cpty, useAlias);
                return new RawRow(
                    id,
                    cpty,
                    lei,
                    alias,
                    "Corporate",
                    instr,
                    reference,
                    tradeDate,
                    settleDate,
                    sideCode,
                    qty,
                    price,
                    cash,
                    note,
                    action,
                    target,
                    pkg,
                    role);
            }

            // Scenario 1: SharedRef vs Broker scheme test (different Quantity & Price so only P1 can pair them).
            {
                var cpty = "CP_A";
                var instr = "XS000001";
                var tDate = new DateOnly(2025, 6, 10);
                left.Add(MakeRow(
                    "L_REF",
                    cpty,
                    useAlias: false,
                    instr,
                    "TR-100",
                    FormatDate(cpty, tDate),
                    FormatDate(cpty, tDate),
                    SideToken(),
                    "100",
                    FormatPrice(100m, 0),
                    "10000.00",
                    note: ""));
                right.Add(MakeRow(
                    "R_REF",
                    cpty,
                    useAlias: true,
                    instr,
                    "TR-100",
                    FormatDate(cpty, tDate),
                    FormatDate(cpty, tDate),
                    SideToken(),
                    "110",
                    FormatPrice(101m, 0),
                    "10000.00",
                    note: "   "));

                if (spec.RefScheme is "broker")
                {
                    expectedRows[("left", "L_REF")] = new(Disposition.LeftOnly, InputValidity.Valid, null);
                    expectedRows[("right", "R_REF")] = new(Disposition.RightOnly, InputValidity.Valid, null);
                    expectedEntries.Add(new(
                        [("left", "L_REF")],
                        [],
                        Alignment.LeftOnly,
                        Composition.None,
                        FieldOutcome.NotCompared,
                        null,
                        ImmutableDictionary<string, ExpectedFieldDiff>.Empty));
                    expectedEntries.Add(new(
                        [],
                        [("right", "R_REF")],
                        Alignment.RightOnly,
                        Composition.None,
                        FieldOutcome.NotCompared,
                        null,
                        ImmutableDictionary<string, ExpectedFieldDiff>.Empty));
                    leftOnlyQty += 100m;
                    rightOnlyQty += 110m;
                    leftOnlyPrice += 100m;
                }
                else
                {
                    expectedRows[("left", "L_REF")] = new(Disposition.Matched, InputValidity.Valid, null);
                    expectedRows[("right", "R_REF")] = new(Disposition.Matched, InputValidity.Valid, null);
                    expectedEntries.Add(new(
                        [("left", "L_REF")],
                        [("right", "R_REF")],
                        Alignment.Matched,
                        Composition.OneToOne,
                        FieldOutcome.Break,
                        "P1",
                        new Dictionary<string, ExpectedFieldDiff>(StringComparer.Ordinal)
                        {
                            ["SettleDate"] = new(FieldOutcome.Exact, []),
                            ["Price"] = new(FieldOutcome.Break, []),
                            ["NetCash"] = new(FieldOutcome.Exact, []),
                        }));
                    matchedQtyLeft += 100m;
                    matchedQtyRight += 110m;
                }
            }

            // Scenario 2: NaturalKey match testing Days tolerance `when` condition (#289) and NetCash attribution (R4).
            {
                var cpty = "CP_B";
                var instr = "XS000002";
                var tDate = new DateOnly(2025, 6, 11);
                var noteBlocksDateTol = random.Next(2) == 0;
                var cashUnexplainedBreak = random.Next(2) == 0;
                var leftCash = 10000.00m;
                var rightCash = cashUnexplainedBreak ? 10500.00m : 10025.00m; // Qty=100 * Price=100.25 -> 10025 explained by trading.netCash

                left.Add(MakeRow(
                    "L_NAT",
                    cpty,
                    useAlias: true,
                    instr,
                    null,
                    FormatDate(cpty, tDate),
                    FormatDate(cpty, tDate),
                    SideToken(),
                    "100",
                    FormatPrice(100m, 0),
                    leftCash.ToString("F2", CultureInfo.InvariantCulture),
                    note: ""));
                right.Add(MakeRow(
                    "R_NAT",
                    cpty,
                    useAlias: false,
                    instr,
                    null,
                    FormatDate(cpty, tDate),
                    FormatDate(cpty, tDate.AddDays(1)),
                    SideToken(),
                    "100",
                    FormatPrice(100m, 8), // 8/32 = 0.25
                    rightCash.ToString("F2", CultureInfo.InvariantCulture),
                    note: noteBlocksDateTol ? "STRICT" : "  "));

                var settleOutcome = noteBlocksDateTol ? FieldOutcome.Break : FieldOutcome.WithinTolerance;
                var cashOutcome = cashUnexplainedBreak ? FieldOutcome.Break : FieldOutcome.WithinTolerance;
                IReadOnlyList<string> cashAttr = spec.UseNetCashAttribution && !cashUnexplainedBreak ? ["Price"] : [];
                var overallOutcome = settleOutcome is FieldOutcome.Break || cashOutcome is FieldOutcome.Break
                    ? FieldOutcome.Break
                    : FieldOutcome.WithinTolerance;

                expectedRows[("left", "L_NAT")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("right", "R_NAT")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedEntries.Add(new(
                    [("left", "L_NAT")],
                    [("right", "R_NAT")],
                    Alignment.Matched,
                    Composition.OneToOne,
                    overallOutcome,
                    "P2",
                    new Dictionary<string, ExpectedFieldDiff>(StringComparer.Ordinal)
                    {
                        ["SettleDate"] = new(settleOutcome, []),
                        ["Price"] = new(FieldOutcome.WithinTolerance, []),
                        ["NetCash"] = new(cashOutcome, cashAttr),
                    }));
                matchedQtyLeft += 100m;
                matchedQtyRight += 100m;
            }

            // Scenario 3: Lifecycle chain with reversal CANC (#309) + Lone NEW (#307).
            {
                var cpty = "CP_A";
                var instr = "XS000002";
                var tDateChain = new DateOnly(2025, 6, 12);
                left.Add(MakeRow(
                    "L_LIFE",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateChain),
                    FormatDate(cpty, tDateChain),
                    SideToken(),
                    "250",
                    FormatPrice(100m, 0),
                    "25000.00"));
                right.Add(MakeRow(
                    "R_NEW",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateChain),
                    FormatDate(cpty, tDateChain),
                    SideToken(),
                    "500",
                    FormatPrice(100m, 0),
                    "50000.00",
                    action: "NEW"));
                // CANC carries reversal values ("B" with "-500" and invalid/blank trade fields) which must skip non-Identify trade rules (#309).
                right.Add(MakeRow(
                    "R_CANC",
                    cpty,
                    false,
                    instr,
                    null,
                    "not-a-date",
                    null,
                    "B",
                    "-500",
                    "not-a-price",
                    "not-a-cash",
                    action: "CANC",
                    target: "R_NEW"));
                right.Add(MakeRow(
                    "R_CORR",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateChain),
                    FormatDate(cpty, tDateChain),
                    SideToken(),
                    "250",
                    FormatPrice(100m, 0),
                    "25000.00",
                    action: "CORR",
                    target: "R_CANC"));

                expectedRows[("left", "L_LIFE")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("right", "R_NEW")] = new(Disposition.Suppressed, InputValidity.Valid, SuppressionReason.Superseded);
                expectedRows[("right", "R_CANC")] = new(Disposition.Suppressed, InputValidity.Valid, SuppressionReason.CancelEvent);
                expectedRows[("right", "R_CORR")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedEntries.Add(new(
                    [("left", "L_LIFE")],
                    [("right", "R_CORR")],
                    Alignment.Matched,
                    Composition.OneToOne | Composition.LifecycleCollapsed,
                    FieldOutcome.Exact,
                    "P2",
                    new Dictionary<string, ExpectedFieldDiff>(StringComparer.Ordinal)
                    {
                        ["SettleDate"] = new(FieldOutcome.Exact, []),
                        ["Price"] = new(FieldOutcome.Exact, []),
                        ["NetCash"] = new(FieldOutcome.Exact, []),
                    }));
                matchedQtyLeft += 250m;
                matchedQtyRight += 250m;

                // Lone NEW (#307): must NOT be flagged LifecycleCollapsed.
                var tDateLone = new DateOnly(2025, 6, 13);
                left.Add(MakeRow(
                    "L_LONE",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateLone),
                    FormatDate(cpty, tDateLone),
                    SideToken(),
                    "75",
                    FormatPrice(100m, 0),
                    "7500.00"));
                right.Add(MakeRow(
                    "R_LONE_NEW",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateLone),
                    FormatDate(cpty, tDateLone),
                    SideToken(),
                    "75",
                    FormatPrice(100m, 0),
                    "7500.00",
                    action: "NEW"));

                expectedRows[("left", "L_LONE")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("right", "R_LONE_NEW")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedEntries.Add(new(
                    [("left", "L_LONE")],
                    [("right", "R_LONE_NEW")],
                    Alignment.Matched,
                    Composition.OneToOne,
                    FieldOutcome.Exact,
                    "P2",
                    new Dictionary<string, ExpectedFieldDiff>(StringComparer.Ordinal)
                    {
                        ["SettleDate"] = new(FieldOutcome.Exact, []),
                        ["Price"] = new(FieldOutcome.Exact, []),
                        ["NetCash"] = new(FieldOutcome.Exact, []),
                    }));
                matchedQtyLeft += 75m;
                matchedQtyRight += 75m;
            }

            // Scenario 4: Package of 2 legs on left matched to 1 right row -> ManyToOne | Package (#308).
            {
                var cpty = "CP_B";
                var instr = "XS000001";
                var tDatePkg = new DateOnly(2025, 6, 14);
                left.Add(MakeRow(
                    "L_PKG_PAY",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDatePkg),
                    FormatDate(cpty, tDatePkg),
                    SideToken(),
                    "300",
                    FormatPrice(102m, 0),
                    "30600.00",
                    pkg: "PK-1",
                    role: "PAY"));
                left.Add(MakeRow(
                    "L_PKG_REC",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDatePkg),
                    FormatDate(cpty, tDatePkg),
                    SideToken(),
                    "300",
                    FormatPrice(99m, 0),
                    "600.00",
                    pkg: "PK-1",
                    role: "REC"));
                right.Add(MakeRow(
                    "R_PKG",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDatePkg),
                    FormatDate(cpty, tDatePkg),
                    SideToken(),
                    "300",
                    FormatPrice(102m, 0),
                    "30000.00"));

                expectedRows[("left", "L_PKG_PAY")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("left", "L_PKG_REC")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("right", "R_PKG")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedEntries.Add(new(
                    [("left", "L_PKG_PAY"), ("left", "L_PKG_REC")],
                    [("right", "R_PKG")],
                    Alignment.Matched,
                    Composition.ManyToOne | Composition.Package,
                    FieldOutcome.Exact,
                    "P2",
                    new Dictionary<string, ExpectedFieldDiff>(StringComparer.Ordinal)
                    {
                        ["SettleDate"] = new(FieldOutcome.Exact, []),
                        ["Price"] = new(FieldOutcome.Exact, []),
                        ["NetCash"] = new(FieldOutcome.Exact, []),
                    }));
                matchedQtyLeft += 300m;
                matchedQtyRight += 300m;
            }

            // Scenario 5: GroupSum allocation (ManyToOne) + LeftOnly row with blank Price (#274, #300).
            {
                var cpty = "CP_A";
                var instr = "XS000001";
                var tDateAlloc = new DateOnly(2025, 6, 15);
                left.Add(MakeRow(
                    "L_ALLOC_1",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateAlloc),
                    FormatDate(cpty, tDateAlloc),
                    SideToken(),
                    "40",
                    FormatPrice(100m, 0),
                    "4000.00"));
                left.Add(MakeRow(
                    "L_ALLOC_2",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateAlloc),
                    FormatDate(cpty, tDateAlloc),
                    SideToken(),
                    "60",
                    FormatPrice(100m, 0),
                    "6000.00"));
                right.Add(MakeRow(
                    "R_ALLOC",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateAlloc),
                    FormatDate(cpty, tDateAlloc),
                    SideToken(),
                    "100",
                    FormatPrice(100m, 0),
                    "10000.00"));

                expectedRows[("left", "L_ALLOC_1")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("left", "L_ALLOC_2")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedRows[("right", "R_ALLOC")] = new(Disposition.Matched, InputValidity.Valid, null);
                expectedEntries.Add(new(
                    [("left", "L_ALLOC_1"), ("left", "L_ALLOC_2")],
                    [("right", "R_ALLOC")],
                    Alignment.Matched,
                    Composition.ManyToOne,
                    FieldOutcome.Exact,
                    "P3",
                    new Dictionary<string, ExpectedFieldDiff>(StringComparer.Ordinal)
                    {
                        ["SettleDate"] = new(FieldOutcome.Exact, []),
                        ["Price"] = new(FieldOutcome.Exact, []),
                        ["NetCash"] = new(FieldOutcome.Exact, []),
                    }));
                matchedQtyLeft += 100m;
                matchedQtyRight += 100m;

                // LeftOnly row with blank Price ("   ") and blank SettleDate (""): stays Valid (#274, #275) and excluded from Price total in BuildSideBySide (#300).
                var tDateBlank = new DateOnly(2025, 6, 16);
                left.Add(MakeRow(
                    "L_BLANK_PRICE",
                    cpty,
                    false,
                    instr,
                    null,
                    FormatDate(cpty, tDateBlank),
                    "",
                    SideToken(),
                    "50",
                    "   ",
                    "5000.00"));

                expectedRows[("left", "L_BLANK_PRICE")] = new(Disposition.LeftOnly, InputValidity.Valid, null);
                expectedEntries.Add(new(
                    [("left", "L_BLANK_PRICE")],
                    [],
                    Alignment.LeftOnly,
                    Composition.None,
                    FieldOutcome.NotCompared,
                    null,
                    ImmutableDictionary<string, ExpectedFieldDiff>.Empty));
                leftOnlyQty += 50m;
            }

            return new GeneratedRun(
                left,
                right,
                expectedRows,
                expectedEntries,
                matchedQtyLeft,
                matchedQtyRight,
                leftOnlyQty,
                rightOnlyQty,
                leftOnlyPrice);
        }
    }
}
