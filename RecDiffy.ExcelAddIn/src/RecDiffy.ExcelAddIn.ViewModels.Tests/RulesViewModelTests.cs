using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class RulesViewModelTests
{
    private static readonly SourceTable Left = new(["Ref", "Quantity", "Price", "Book"], [["T-1", 10m, 1.5m, "A"]]);

    private static readonly SourceTable Right = new(["REF", "quantity", "Px"], [["T-1", 10m, 1.5m]]);

    [Fact]
    public async Task Pairing_adds_a_field_for_each_column_both_sides_name_alike()
    {
        var rules = (await Shell()).Rules;

        rules.PairSameNamedColumnsCommand.Execute(null);

        Assert.Equal(
            [("Ref", "Ref", "REF"), ("Quantity", "Quantity", "quantity")],
            rules.Fields.Select(field => (field.Name, field.LeftColumn, field.RightColumn)));
        Assert.All(rules.Fields, field => Assert.Equal((FieldType.Text, FieldRole.Text), (field.Type, field.Role)));
    }

    [Fact]
    public async Task Pairing_again_keeps_your_edits_and_adds_only_what_is_missing()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[0].Type = FieldType.Identifier;
        rules.Fields[1].RemoveCommand.Execute(null);

        rules.PairSameNamedColumnsCommand.Execute(null);

        Assert.Equal(["Ref", "Quantity"], rules.Fields.Select(field => field.Name), StringComparer.Ordinal);
        Assert.Equal(FieldType.Identifier, rules.Fields[0].Type);
    }

    [Fact]
    public async Task Pairing_leaves_out_a_column_a_field_already_reads()
    {
        var rules = (await Shell()).Rules;
        rules.AddFieldCommand.Execute(null);
        rules.Fields[0].Name = "Trade";
        rules.Fields[0].LeftColumn = "Ref";
        rules.Fields[0].RightColumn = "REF";

        rules.PairSameNamedColumnsCommand.Execute(null);

        Assert.Equal(["Trade", "Quantity"], rules.Fields.Select(field => field.Name), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_new_field_says_what_it_still_needs()
    {
        var rules = (await Shell()).Rules;
        rules.AddFieldCommand.Execute(null);
        var field = rules.Fields[0];

        Assert.Equal("Name the field.", field.Problem);
        field.Name = "Ref";
        Assert.Equal("Choose the left column.", field.Problem);
        field.LeftColumn = "Ref";
        Assert.Equal("Choose the right column.", field.Problem);
        field.RightColumn = "REF";
        Assert.Null(field.Problem);
    }

    [Fact]
    public async Task Field_names_must_differ_even_in_case()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);

        rules.Fields[1].Name = "ref";

        Assert.All(rules.Fields, field => Assert.Equal("Another field already has this name.", field.Problem));
    }

    [Fact]
    public async Task A_column_the_data_no_longer_has_is_flagged()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);

        rules.Fields[0].LeftColumn = "Gone";

        Assert.Equal("The left data has no column 'Gone' any more.", rules.Fields[0].Problem);
    }

    [Fact]
    public async Task A_key_cannot_be_text()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);

        rules.Fields[0].Role = FieldRole.Key;

        Assert.Equal("A key can't be Text. Choose Identifier, Code, Number or Date.", rules.Fields[0].Problem);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("28", true)]
    [InlineData("29", false)]
    [InlineData("-1", false)]
    [InlineData("1.5", false)]
    [InlineData("", false)]
    public async Task Decimal_places_are_a_whole_number_from_0_to_28(string text, bool accepted)
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[1].Type = FieldType.Decimal;

        rules.Fields[1].ScaleText = text;

        Assert.Equal(accepted, rules.Fields[1].Problem is null);
    }

    [Fact]
    public async Task A_pass_asks_for_a_key_field_until_one_is_chosen()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.AddPassCommand.Execute(null);
        var pass = rules.Passes[0];

        Assert.Equal("Set a field's role to Key, then choose it here.", pass.Problem);
        rules.Fields[0].Type = FieldType.Identifier;
        rules.Fields[0].Role = FieldRole.Key;
        Assert.Equal(["Ref"], pass.Keys.Select(key => key.Field), StringComparer.Ordinal);
        Assert.Equal("Choose at least one key field.", pass.Problem);
        pass.Keys[0].IsSelected = true;
        Assert.Null(pass.Problem);
    }

    [Fact]
    public async Task Removing_a_pass_numbers_the_rest_again()
    {
        var rules = (await Shell()).Rules;
        rules.AddPassCommand.Execute(null);
        rules.AddPassCommand.Execute(null);

        rules.Passes[0].RemoveCommand.Execute(null);

        Assert.Equal("Pass 1", Assert.Single(rules.Passes).Title);
    }

    [Fact]
    public async Task A_compared_field_offers_only_the_comparisons_its_type_allows()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[1].Type = FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        var comparison = Assert.Single(rules.Comparisons);
        Assert.Equal("Quantity", comparison.Field);
        Assert.Equal(
            [ComparisonKind.Exact, ComparisonKind.Absolute, ComparisonKind.Relative, ComparisonKind.AbsoluteOrRelative],
            comparison.Kinds.Select(choice => choice.Kind));
        comparison.Kind = ComparisonKind.Absolute;

        rules.Fields[1].Type = FieldType.Date;

        Assert.Equal([ComparisonKind.Exact, ComparisonKind.Days], comparison.Kinds.Select(choice => choice.Kind));
        Assert.Equal(ComparisonKind.Exact, comparison.Kind);
    }

    [Fact]
    public async Task A_field_that_is_no_longer_compared_loses_its_comparison()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[1].Role = FieldRole.Economic;
        Assert.Single(rules.Comparisons);

        rules.Fields[1].Role = FieldRole.Text;

        Assert.Empty(rules.Comparisons);
        Assert.False(rules.HasComparisons);
    }

    [Theory]
    [InlineData(ComparisonKind.Absolute, "0.01", true)]
    [InlineData(ComparisonKind.Absolute, "abc", false)]
    [InlineData(ComparisonKind.Absolute, "-0.01", false)]
    [InlineData(ComparisonKind.Absolute, "", false)]
    [InlineData(ComparisonKind.Relative, "0.5", true)]
    [InlineData(ComparisonKind.Relative, "1,000", false)]
    [InlineData(ComparisonKind.Days, "2", true)]
    [InlineData(ComparisonKind.Days, "1.5", false)]
    [InlineData(ComparisonKind.Days, "-1", false)]
    public async Task A_tolerance_must_be_a_number_of_the_right_kind(ComparisonKind kind, string text, bool accepted)
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[1].Type = kind is ComparisonKind.Days ? FieldType.Date : FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        var comparison = rules.Comparisons[0];
        comparison.Kind = kind;

        (comparison.AmountText, comparison.PercentageText, comparison.DaysText) = (text, text, text);

        Assert.Equal(accepted, comparison.Problem is null);
    }

    [Fact]
    public async Task Amounts_are_read_in_the_Excel_culture()
    {
        var rules = (await Shell(CultureInfo.GetCultureInfo("de-DE"))).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[1].Type = FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        var comparison = rules.Comparisons[0];
        comparison.Kind = ComparisonKind.Absolute;

        comparison.AmountText = "0.01";
        Assert.Equal("Enter the amount as a number, for example 0,05.", comparison.Problem);
        comparison.AmountText = "0,01";

        Assert.Null(comparison.Problem);
        Assert.Equal(0.01m, rules.ToState().Comparisons[0].Absolute);
    }

    [Fact]
    public async Task A_percentage_is_kept_as_a_fraction()
    {
        var rules = (await Shell()).Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[1].Type = FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        var comparison = rules.Comparisons[0];

        comparison.Kind = ComparisonKind.Relative;
        comparison.PercentageText = "0.5";

        Assert.Equal(0.005m, rules.ToState().Comparisons[0].Relative);
    }

    [Fact]
    public async Task Valid_rules_leave_no_problems_and_a_change_after_a_run_is_noticed()
    {
        var shell = await Shell();
        MakeValid(shell.Rules);
        Assert.Empty(shell.Rules.Problems);
        Assert.True(shell.Rules.IsValid);
        shell.MarkRun();
        Assert.False(shell.IsChangedSinceRun);

        shell.Rules.Comparisons[0].AmountText = "0.02";
        Assert.True(shell.IsChangedSinceRun);
        shell.Rules.Comparisons[0].AmountText = "0.01";

        Assert.False(shell.IsChangedSinceRun);
    }

    [Fact]
    public async Task The_rules_survive_a_saved_setup_with_the_same_profile_hash()
    {
        var shell = await Shell();
        MakeValid(shell.Rules);
        shell.Rules.Comparisons[0].Kind = ComparisonKind.AbsoluteOrRelative;
        shell.Rules.Comparisons[0].PercentageText = "0.5";
        var setup = shell.ToSetup();

        var reopened = await Shell();
        reopened.Apply(SetupJson.Read(SetupJson.Write(setup)));

        Assert.Equal(setup, reopened.ToSetup());
        Assert.Equal(Hash(setup.Profile), Hash(reopened.ToSetup().Profile));
        Assert.Equal(["Ref", "Quantity"], reopened.Rules.Fields.Select(field => field.Name), StringComparer.Ordinal);
        Assert.Equal(("0.01", "0.5"), (reopened.Rules.Comparisons[0].AmountText, reopened.Rules.Comparisons[0].PercentageText));
        Assert.Equal(["Choose the left and right data on the Sources tab first."], reopened.Rules.Problems, StringComparer.Ordinal);
        await ChooseBoth(reopened);
        Assert.True(reopened.CanRun);
    }

    [Fact]
    public async Task A_profile_the_tab_cannot_edit_is_kept_as_it_is_and_can_still_run()
    {
        var shell = await Shell();
        MakeValid(shell.Rules);
        shell.Rules.Fields[0].Type = FieldType.Symbol;
        var setup = shell.ToSetup();
        var left = setup.Profile.Normalization["left"];
        var mapped = SetupProfiles.Canonical(setup.Profile with
        {
            Normalization = setup.Profile.Normalization.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem("left", left with { Identify = [left.Identify[0] with { Maps = Map("T-1", "T-01") }, .. left.Identify.Skip(1)] }),
        });

        shell.Apply(setup with { Profile = mapped });
        await ChooseBoth(shell);

        Assert.Equal("These rules use options this tab can't edit yet. They are kept as they are.", shell.Rules.NotEditable);
        Assert.Empty(shell.Rules.Fields);
        Assert.False(shell.Rules.AddFieldCommand.CanExecute(null));
        Assert.False(shell.Rules.PairSameNamedColumnsCommand.CanExecute(null));
        Assert.Equal(mapped.ToJson(), shell.ToSetup().Profile.ToJson());
        Assert.True(shell.CanRun);
    }

    private static void MakeValid(RulesViewModel rules)
    {
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[0].Type = FieldType.Identifier;
        rules.Fields[0].Role = FieldRole.Key;
        rules.Fields[1].Type = FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        rules.Fields[1].ScaleText = "0";
        rules.Comparisons[0].Kind = ComparisonKind.Absolute;
        rules.Comparisons[0].AmountText = "0.01";
        rules.AddPassCommand.Execute(null);
        rules.Passes[0].Keys[0].IsSelected = true;
    }

    private static async Task<ShellViewModel> Shell(CultureInfo? excelCulture = null)
    {
        var reads = 0;
        SourceRead ReadNext()
        {
            var table = reads++ % 2 == 0 ? Left : Right;
            return new SourceRead.Read(FakeExcel.Range, table, SourcePreview.Of(table));
        }

        var excel = new FakeExcel(new RangeChoice.Picked(FakeExcel.Range), _ => Task.FromResult(ReadNext()));
        var shell = new ShellViewModel(new SourcesViewModel(
            new SourceServices(excel, FakeCsv.Reading(), new FakeDialog(@"C:\data\Book.xlsx")),
            excelCulture ?? Make.ExcelCulture));
        await ChooseBoth(shell).ConfigureAwait(true);
        return shell;
    }

    private static async Task ChooseBoth(ShellViewModel shell)
    {
        await shell.Sources.Left.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.Sources.Right.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
    }

    private static string Hash(ProfileDocument profile) => Reconciler.Compile(profile, RuleRegistry.CreateCore()).Hash;

    private static ImmutableDictionary<string, IReadOnlyDictionary<string, string>> Map(string from, string to) =>
        ImmutableDictionary<string, IReadOnlyDictionary<string, string>>.Empty.Add("map", ImmutableDictionary<string, string>.Empty.Add(from, to));
}
