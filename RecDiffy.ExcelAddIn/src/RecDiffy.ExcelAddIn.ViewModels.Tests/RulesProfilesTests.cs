using System.Collections.Immutable;
using System.Data;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class RulesProfilesTests
{
    private static readonly RulesState Rules = new(
        [
            new FieldRule("Ref", "Trade ref", "REF", FieldType.Identifier, FieldRole.Key, 0, []),
            new FieldRule("Quantity", "Qty", "Quantity", FieldType.Decimal, FieldRole.Economic, 0, []),
            new FieldRule("Price", "Price", "Px", FieldType.Decimal, FieldRole.Economic, 2, []),
            new FieldRule("TradeDate", "Trade date", "Date", FieldType.Date, FieldRole.Economic, 0, ["dd.MM.yyyy"]),
            new FieldRule("Commission", "Fee", "Commission", FieldType.Decimal, FieldRole.Economic, 2, []),
            new FieldRule("Book", "Book", "Book", FieldType.Text, FieldRole.Text, 0, []),
        ],
        [new PassRule(["Ref"])],
        [
            new ComparisonRule("Quantity", ComparisonKind.Exact, null, null, null),
            new ComparisonRule("Price", ComparisonKind.Absolute, 0.01m, null, null),
            new ComparisonRule("TradeDate", ComparisonKind.Days, null, null, 1),
            new ComparisonRule("Commission", ComparisonKind.AbsoluteOrRelative, 0.5m, 0.001m, null),
        ]);

    private static readonly ProfileDocument Base = SetupProfiles.WithCultures(SetupProfiles.Draft(), "en-US", "en-US");

    [Fact]
    public void Rules_written_to_a_profile_read_back_unchanged()
    {
        var profile = SetupProfiles.Canonical(RulesProfiles.Document(Rules, Base));

        Assert.Equivalent(Rules, RulesProfiles.Read(profile), strict: true);
    }

    [Fact]
    public void A_draft_with_no_rules_reads_as_empty_rules()
    {
        Assert.Equivalent(RulesState.Empty, RulesProfiles.Read(Base), strict: true);
    }

    [Fact]
    public void Exact_comparisons_add_no_tolerance_and_passes_are_numbered_in_order()
    {
        var rules = Rules with { Passes = [new PassRule(["Ref"]), new PassRule(["Ref", "TradeDate"])] };

        var path = Assert.Single(RulesProfiles.Document(rules, Base).Paths);

        Assert.Equal(["P1", "P2"], path.Passes.Select(pass => pass.Id), StringComparer.Ordinal);
        Assert.NotNull(path.Diff);
        Assert.Equal(["Commission", "Price", "TradeDate"], path.Diff.Fields.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void The_engine_accepts_the_profile_and_reconciles_with_it()
    {
        var profile = RulesProfiles.Document(Rules, Base);
        using var left = Table(
            ["Trade ref", "Qty", "Price", "Trade date", "Fee", "Book"],
            ["T-1", 10m, 1.50m, new DateTime(2026, 3, 13), 1.00m, "A"],
            ["T-2", 10m, 1.50m, new DateTime(2026, 3, 13), 1.00m, "A"],
            ["T-3", 10m, 1.50m, new DateTime(2026, 3, 13), 1.00m, "A"],
            ["T-4", 10m, 1.50m, new DateTime(2026, 3, 13), 1.00m, "A"],
            ["T-6", 10m, 1.50m, new DateTime(2026, 3, 13), 1.00m, "A"]);
        using var right = Table(
            ["REF", "Quantity", "Px", "Date", "Commission", "Book"],
            ["T-1", 10m, 1.50m, "13.03.2026", 1.00m, "B"],
            ["T-2", 10m, 1.51m, "13.03.2026", 1.00m, "A"],
            ["T-3", 11m, 1.50m, "13.03.2026", 1.00m, "A"],
            ["T-5", 10m, 1.50m, "13.03.2026", 1.00m, "A"],
            ["T-6", 10m, 1.50m, "14.03.2026", 1.40m, "A"]);

        var result = Reconciler.Run(
            Reconciler.Compile(profile, RuleRegistry.CreateCore()),
            RecordSources.FromDataReader("left", left.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", right.CreateDataReader, new TabularOptions()),
            ReferenceDataSet.Empty);

        Assert.Empty(RulesProfiles.Check(profile));
        Assert.Equal(
            (Exact: 1, WithinTolerance: 2, Break: 1, LeftOnly: 1, RightOnly: 1),
            (result.Summary.Exact, result.Summary.WithinTolerance, result.Summary.Break, result.Summary.LeftOnly, result.Summary.RightOnly));
    }

    [Theory]
    [InlineData("a second path")]
    [InlineData("a routed path")]
    [InlineData("another pass kind")]
    [InlineData("a blocked pass")]
    [InlineData("a group size limit")]
    [InlineData("an attributed tolerance")]
    [InlineData("a tolerance on a carried field")]
    [InlineData("another normalization rule")]
    [InlineData("a value map")]
    [InlineData("a derived value")]
    [InlineData("a validation rule")]
    public void Profiles_with_options_the_tab_cannot_edit_are_not_read(string option)
    {
        var profile = RulesProfiles.Document(Rules, Base);
        var path = profile.Paths[0];
        var pass = path.Passes[0];

        var changed = option switch
        {
            "a second path" => profile with { Paths = [path, path with { Id = "other" }] },
            "a routed path" => WithPath(profile, path with { Route = new RouteDeclaration("route.byField", "Book", "A") }),
            "another pass kind" => WithPath(profile, path with { Passes = [pass with { Pass = "match.sharedRef" }] }),
            "a blocked pass" => WithPath(profile, path with { Passes = [pass with { Block = ["Book"] }] }),
            "a group size limit" => WithPath(profile, path with { Passes = [pass with { MaxGroupSize = 4 }] }),
            "an attributed tolerance" => WithDiff(profile, path, "Price", diff => diff with { Tolerance = diff.Tolerance with { Attributed = "Book" } }),
            "a tolerance on a carried field" => WithDiff(profile, path, "Book", _ => new FieldDiffDeclaration(new ToleranceDeclaration("Exact", null, null, null, null), null, null)),
            "another normalization rule" => WithLeft(profile, side => side with { Identify = [.. side.Identify, side.Identify[0] with { Rule = "text.upper" }] }),
            "a value map" => WithLeft(profile, side => side with { Identify = [side.Identify[0] with { Maps = Map("X", "Y") }, .. side.Identify.Skip(1)] }),
            "a derived value" => WithLeft(profile, side => side with { Derive = [side.Identify[0]] }),
            "a validation rule" => WithLeft(profile, side => side with { Validate = [side.Identify[0] with { Rule = "required.present" }] }),
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Unknown option."),
        };

        Assert.Null(RulesProfiles.Read(changed));
    }

    private static ProfileDocument WithPath(ProfileDocument profile, PathDeclaration path) => profile with { Paths = [path] };

    private static ProfileDocument WithDiff(ProfileDocument profile, PathDeclaration path, string field, Func<FieldDiffDeclaration, FieldDiffDeclaration> change)
    {
        var fields = (path.Diff?.Fields ?? ImmutableDictionary<string, FieldDiffDeclaration>.Empty).ToImmutableDictionary(StringComparer.Ordinal);
        var existing = fields.GetValueOrDefault(field) ?? new FieldDiffDeclaration(new ToleranceDeclaration("Exact", null, null, null, null), null, null);
        return WithPath(profile, path with { Diff = new DiffDeclaration(fields.SetItem(field, change(existing))) });
    }

    private static ProfileDocument WithLeft(ProfileDocument profile, Func<NormalizationSide, NormalizationSide> change) =>
        profile with
        {
            Normalization = profile.Normalization.ToImmutableDictionary(StringComparer.Ordinal).SetItem("left", change(profile.Normalization["left"])),
        };

    private static ImmutableDictionary<string, IReadOnlyDictionary<string, string>> Map(string from, string to) =>
        ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty.Add("map", ImmutableDictionary<string, string>.Empty.Add(from, to));

    private static DataTable Table(string[] columns, params object[][] rows)
    {
        var table = new DataTable { Locale = System.Globalization.CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        foreach (var column in columns)
            table.Columns.Add(column, typeof(object));
#pragma warning restore IDISP004
        foreach (var row in rows)
            table.Rows.Add(row);
        return table;
    }
}
