using System.Collections.Immutable;
using System.Reflection;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class RegistryCoverageGateTests
{
    private static readonly RuleRegistry FullRegistry = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    private static readonly string[] RegisteredPasses =
    [
        "match.sharedRef",
        "match.naturalKey",
        "match.groupSum",
        "match.reducedKey",
    ];

    private static readonly string[] RegisteredRoutes =
    [
        "route.all",
        "route.byField",
    ];

    private static readonly string[] CompilerErrorCodes =
    [
        "UnsupportedFormat",
        "DuplicateField",
        "InvalidParameter",
        "UnknownCulture",
        "UnknownRule",
        "WrongPhase",
        "MissingParameter",
        "UnknownField",
        "FieldReadBeforeWrite",
        "AmbiguousWrite",
        "OverlappingRoute",
        "LimitExceeded",
        "TextKey",
        "FieldNotProduced",
        "ToleranceMismatch",
    ];

    private static readonly Lazy<string> OtherTestSources = new(() =>
    {
        var testDir = Path.Combine(ProfileCorpus.Source, "RecDiffy.Tests");
        return string.Join(
            '\n',
            Directory.EnumerateFiles(testDir, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !string.Equals(Path.GetFileName(file), nameof(RegistryCoverageGateTests) + ".cs", StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    });

    [Fact]
    public void Every_registered_normalization_rule_has_dedicated_test_coverage()
    {
        var rules = FullRegistry.Normalization.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(rules);

        var missing = rules
            .Where(rule => !OtherTestSources.Value.Contains($"\"{rule}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_registered_attribution_rule_has_dedicated_test_coverage()
    {
        var property = typeof(AttributionRegistry).GetProperty("Attributions", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, IAttribution>>(property.GetValue(FullRegistry.Attributions));
        var names = map.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(names);

        var missing = names
            .Where(name => !OtherTestSources.Value.Contains($"\"{name}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_registered_pass_route_and_package_aggregator_has_dedicated_test_coverage()
    {
        var aggregators = Enum.GetValues<AggregateKind>().Select(kind => kind.ToString()).ToArray();
        var missingPasses = RegisteredPasses
            .Where(pass => !OtherTestSources.Value.Contains($"\"{pass}\"", StringComparison.Ordinal))
            .ToArray();
        var missingRoutes = RegisteredRoutes
            .Where(route => !OtherTestSources.Value.Contains($"\"{route}\"", StringComparison.Ordinal))
            .ToArray();
        var missingAggregators = aggregators
            .Where(kind => !OtherTestSources.Value.Contains(kind, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(missingPasses);
        Assert.Empty(missingRoutes);
        Assert.Empty(missingAggregators);
    }

    [Fact]
    public void Every_compile_error_code_and_profile_problem_code_has_dedicated_test_coverage()
    {
        var problemCodes = typeof(ProfileProblemCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(problemCodes);

        var missingCompileCodes = CompilerErrorCodes
            .Where(code => !OtherTestSources.Value.Contains($"\"{code}\"", StringComparison.Ordinal))
            .ToArray();
        var missingProblemCodes = problemCodes
            .Where(code => !OtherTestSources.Value.Contains(code, StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(missingCompileCodes);
        Assert.Empty(missingProblemCodes);
    }

    [Fact]
    public void Every_package_aggregate_kind_executes_and_produces_expected_consolidated_value()
    {
        var document = new ProfileDocument
        {
            FormatVersion = 1,
            ProfileId = "gate-aggregators",
            Version = "1.0.0",
            Midpoint = MidpointMode.ToEven,
            Culture = "",
            SourceKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["left"] = "Id", ["right"] = "Id" },
            Fields =
            [
                new FieldDeclaration("Ref", FieldType.Identifier, FieldRole.Key, 0),
                new FieldDeclaration("Pkg", FieldType.Text, FieldRole.Package, 0),
                new FieldDeclaration("Role", FieldType.Symbol, FieldRole.Package, 0),
                new FieldDeclaration("SameVal", FieldType.Decimal, FieldRole.Economic, 2),
                new FieldDeclaration("SumVal", FieldType.Decimal, FieldRole.Economic, 2),
                new FieldDeclaration("RoleVal", FieldType.Decimal, FieldRole.Economic, 2),
                new FieldDeclaration("DiffVal", FieldType.Decimal, FieldRole.Economic, 2),
            ],
            Normalization = new Dictionary<string, NormalizationSide>(StringComparer.Ordinal)
            {
                ["left"] = SideRules(includePackage: true),
                ["right"] = SideRules(includePackage: false),
            },
            Consolidation = new Dictionary<string, ConsolidationSide>(StringComparer.Ordinal)
            {
                ["left"] = new ConsolidationSide(
                    null,
                    [
                        new PackagePlan(
                            "Pkg",
                            "Role",
                            ["PAY", "REC"],
                            [
                                new FieldAggregate("SameVal", AggregateKind.Same, null, null),
                                new FieldAggregate("SumVal", AggregateKind.Sum, null, null),
                                new FieldAggregate("RoleVal", AggregateKind.FromRole, "PAY", null),
                                new FieldAggregate("DiffVal", AggregateKind.DifferenceByRole, "PAY", "REC"),
                            ]),
                    ]),
            },
            Limits = new SearchLimits(8, 16, 8, 128),
            Paths =
            [
                new PathDeclaration(
                    "p",
                    new RouteDeclaration("route.all", null, null),
                    [new PassDeclaration("P1", "match.naturalKey", ["Ref"], [], [], null, null)],
                    new DiffDeclaration(new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)
                    {
                        ["SameVal"] = ExactDiff(),
                        ["SumVal"] = ExactDiff(),
                        ["RoleVal"] = ExactDiff(),
                        ["DiffVal"] = ExactDiff(),
                    })),
            ],
        };

        var coveredKinds = document.Consolidation["left"].Packages[0].Aggregates.Select(a => a.Kind).ToHashSet();
        Assert.Equal(Enum.GetValues<AggregateKind>().OrderBy(k => k), coveredKinds.OrderBy(k => k));

        var map = new SourceMap<AggRow>()
            .Text("Id", r => r.Id)
            .Text("Ref", r => r.Ref)
            .Text("Pkg", r => r.Pkg)
            .Text("Role", r => r.Role)
            .Text("SameVal", r => r.SameVal)
            .Text("SumVal", r => r.SumVal)
            .Text("RoleVal", r => r.RoleVal)
            .Text("DiffVal", r => r.DiffVal);

        AggRow[] left =
        [
            new("L1", "T-1", "PK-1", "PAY", "10.00", "30.00", "99.00", "100.00"),
            new("L2", "T-1", "PK-1", "REC", "10.00", "70.00", "44.00", "40.00"),
        ];
        AggRow[] right =
        [
            new("R1", "T-1", null, null, "10.00", "100.00", "99.00", "60.00"),
        ];

        var result = Reconciler.Run(
            Reconciler.Compile(document, FullRegistry),
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(Composition.ManyToOne | Composition.Package, entry.Composition);
    }

    private static NormalizationSide SideRules(bool includePackage) =>
        new(
            Identify: includePackage
                ?
                [
                    Rule("identifier.map", ("from", "Ref"), ("to", "Ref"), ("scheme", "shared")),
                    Rule("text.copy", ("from", "Pkg"), ("to", "Pkg")),
                    Rule("symbol.map", ("from", "Role"), ("to", "Role")),
                ]
                : [Rule("identifier.map", ("from", "Ref"), ("to", "Ref"), ("scheme", "shared"))],
            Parse:
            [
                Rule("decimal.parse", ("from", "SameVal"), ("to", "SameVal")),
                Rule("decimal.parse", ("from", "SumVal"), ("to", "SumVal")),
                Rule("decimal.parse", ("from", "RoleVal"), ("to", "RoleVal")),
                Rule("decimal.parse", ("from", "DiffVal"), ("to", "DiffVal")),
            ],
            Derive: [],
            Validate: []);

    private static RuleCall Rule(string name, params (string Key, string Value)[] strings) =>
        new(
            name,
            null,
            strings.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ImmutableDictionary<string, IReadOnlyList<string>>.Empty,
            ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty);

    private static FieldDiffDeclaration ExactDiff() =>
        new(new ToleranceDeclaration("Exact", null, null, null, null), null, null);

    private sealed record AggRow(
        string Id,
        string Ref,
        string? Pkg,
        string? Role,
        string SameVal,
        string SumVal,
        string RoleVal,
        string DiffVal);
}
