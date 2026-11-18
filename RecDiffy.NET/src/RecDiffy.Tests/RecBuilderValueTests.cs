using RecDiffy.Results;

namespace RecDiffy.Tests;

// #511: a key member's text can name its values and the spellings that count as each.
public class RecBuilderValueTests
{
    private static readonly DateOnly Day = new(2026, 11, 18);

    private sealed record OurTrade(string TradeId, DateOnly TradeDate, string Isin, string Side, decimal Quantity, decimal Price);

    private sealed record BrokerConfirm(string ConfirmRef, DateOnly TradeDate, string Isin, string BuySell, decimal Qty, decimal Price);

    private static OurTrade Ours(string id, string side, decimal quantity = 100m) => new(id, Day, "US0378331005", side, quantity, 10m);

    private static BrokerConfirm Theirs(string id, string side, decimal quantity = 100m) => new(id, Day, "US0378331005", side, quantity, 10m);

    private static RecBuilder<OurTrade, BrokerConfirm> Keyed(IEnumerable<OurTrade> ours, IEnumerable<BrokerConfirm> theirs) =>
        RecBuilder.Between(ours, theirs)
            .MatchOn(trade => trade.Isin, confirm => confirm.Isin)
                .And(trade => trade.Quantity, confirm => confirm.Qty)
                .And(trade => trade.Side, confirm => confirm.BuySell);

    [Fact]
    public void Spellings_listed_for_a_value_match_each_other_on_both_sides()
    {
        var result = Keyed([Ours("T1", "B"), Ours("T2", "SELL", 50m)], [Theirs("C1", "BOT"), Theirs("C2", "SLD", 50m)])
            .Value("BUY", accepts: ["B", "BOT", "BUY"])
            .Value("SELL", accepts: ["S", "SLD", "SELL"])
            .Run();

        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
    }

    [Fact]
    public void Different_values_do_not_match()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "S")])
            .Value("BUY", accepts: ["B", "BUY"])
            .Value("SELL", accepts: ["S", "SELL"])
            .Run();

        Assert.Empty(result.Matched);
        Assert.Single(result.LeftOnly);
        Assert.Single(result.RightOnly);
    }

    [Fact]
    public void Each_side_can_have_its_own_spellings()
    {
        var result = Keyed([Ours("T1", "BUY"), Ours("T2", "SELL", 50m)], [Theirs("C1", "B"), Theirs("C2", "SLD", 50m)])
            .Value("BUY", ours: ["BUY"], theirs: ["B", "BOT"])
            .Value("SELL", ours: ["SELL"], theirs: ["S", "SLD"])
            .Run();

        Assert.Equal(2, result.Matched.Count);
    }

    [Fact]
    public void A_spelling_listed_for_one_side_only_is_not_accepted_on_the_other()
    {
        var result = Keyed([Ours("T1", "B")], [Theirs("C1", "B")])
            .Value("BUY", ours: ["BUY"], theirs: ["B"])
            .Run();

        Assert.Empty(result.Matched);
        var quarantined = Assert.Single(result.Result.Rows, row => row.Disposition is Disposition.Quarantined);
        Assert.Equal("left", quarantined.Row.Source);
    }

    [Fact]
    public void A_function_decides_the_value_of_each_spelling_for_both_sides()
    {
        var result = Keyed([Ours("T1", "B"), Ours("T2", "SLD", 50m)], [Theirs("C1", "BUY"), Theirs("C2", "S", 50m)])
            .Value(text => text switch
            {
                "B" or "BOT" or "BUY" => "BUY",
                "S" or "SLD" or "SELL" => "SELL",
                _ => null,
            })
            .Run();

        Assert.Equal(2, result.Matched.Count);
    }

    [Fact]
    public void A_function_for_each_side_decides_for_that_side()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BOT")])
            .Value(
                ours: text => text,
                theirs: text => text is "B" or "BOT" ? "BUY" : null)
            .Run();

        Assert.Single(result.Matched);
    }

    [Fact]
    public void A_spelling_nobody_listed_is_quarantined_as_unknown_and_never_guessed()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "SOLD")])
            .Value("BUY", accepts: ["B", "BUY"])
            .Value("SELL", accepts: ["S", "SELL"])
            .Run();

        Assert.Empty(result.Matched);
        var quarantined = Assert.Single(result.Result.Rows, row => row.Disposition is Disposition.Quarantined);
        Assert.Equal("right", quarantined.Row.Source);
        Assert.Contains(quarantined.Diagnostics, diagnostic => diagnostic.Code == "UnknownSymbol");
    }

    [Fact]
    public void A_spelling_is_matched_exactly_so_another_case_is_unknown()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "bot")])
            .Value("BUY", accepts: ["B", "BOT", "BUY"])
            .Run();

        Assert.Empty(result.Matched);
        Assert.Single(result.Result.Rows, row => row.Disposition is Disposition.Quarantined);
    }

    [Fact]
    public void Spaces_around_a_spelling_do_not_matter()
    {
        var result = Keyed([Ours("T1", " BUY ")], [Theirs("C1", "B")])
            .Value("BUY", accepts: [" B ", "BUY"])
            .Run();

        Assert.Single(result.Matched);
    }

    [Fact]
    public void A_function_is_asked_only_about_the_spellings_found_so_the_table_holds_those()
    {
        var asked = new List<string>();

        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B"), Theirs("C2", "B", 50m)])
            .Value(text =>
            {
                asked.Add(text);
                return text is "B" or "BUY" ? "BUY" : null;
            })
            .Run();

        Assert.Equal(["B", "BUY"], asked.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(["B"], TypedProfileRules.SideTable(result.Profile, "right").Keys, StringComparer.Ordinal);
        Assert.Equal(["BUY"], TypedProfileRules.SideTable(result.Profile, "left").Keys, StringComparer.Ordinal);
    }

    [Fact]
    public void The_profile_holds_every_listed_spelling_on_each_side_and_passes_nothing_through()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B")])
            .Value("BUY", ours: ["BUY"], theirs: ["B", "BOT"])
            .Run();

        Assert.Equal(TypedProfileRules.Table(("BUY", "BUY")), TypedProfileRules.SideTable(result.Profile, "left"));
        Assert.Equal(TypedProfileRules.Table(("B", "BUY"), ("BOT", "BUY")), TypedProfileRules.SideTable(result.Profile, "right"));
        var rule = TypedProfileRules.Of(result.Profile, "right").Single(candidate => candidate.To == "Side");
        Assert.Equal("false", rule.Passthrough, ignoreCase: true);
    }

    [Fact]
    public void The_profile_hash_changes_when_a_spelling_is_added()
    {
        var fewer = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B")]).Value("BUY", accepts: ["B", "BUY"]).Run();
        var more = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B")]).Value("BUY", accepts: ["B", "BOT", "BUY"]).Run();

        Assert.NotEqual(fewer.Result.Run.ProfileHash, more.Result.Run.ProfileHash, StringComparer.Ordinal);
    }

    [Fact]
    public void The_same_spellings_give_the_same_profile_hash()
    {
        var first = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B")]).Value("BUY", accepts: ["B", "BUY"]).Run();
        var second = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B")]).Value("BUY", accepts: ["BUY", "B"]).Run();

        Assert.Equal(first.Result.Run.ProfileHash, second.Result.Run.ProfileHash);
    }

    [Fact]
    public void A_member_that_names_no_values_is_still_matched_as_it_was()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BUY")]).Run();

        Assert.Single(result.Matched);
        var rules = TypedProfileRules.Of(result.Profile, "left").Select(rule => rule.Rule).ToArray();
        Assert.Contains("identifier.map", rules, StringComparer.Ordinal);
        Assert.DoesNotContain("symbol.map", rules, StringComparer.Ordinal);
    }

    [Fact]
    public void Values_can_be_named_on_a_later_pass()
    {
        var result = RecBuilder.Between([Ours("T1", "B")], [Theirs("C1", "BUY", 90m)])
            .MatchOn(trade => trade.Isin, confirm => confirm.Isin)
                .And(trade => trade.Quantity, confirm => confirm.Qty)
            .ThenMatchOn(trade => trade.Isin, confirm => confirm.Isin)
                .And(trade => trade.Side, confirm => confirm.BuySell)
                    .Value("BUY", accepts: ["B", "BUY"])
            .Run();

        Assert.Single(result.Matched);
    }

    [Fact]
    public void Naming_values_before_any_key_is_refused()
    {
        var builder = RecBuilder.Between([Ours("T1", "BUY")], [Theirs("C1", "BUY")]);

        Assert.Throws<InvalidOperationException>(() => builder.Value("BUY", accepts: ["B"]));
    }

    [Fact]
    public void Naming_values_after_a_compare_is_refused()
    {
        var builder = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BUY")])
            .Compare(trade => trade.Price, confirm => confirm.Price);

        Assert.Throws<InvalidOperationException>(() => builder.Value("BUY", accepts: ["B"]));
    }

    [Fact]
    public void Naming_values_of_a_member_that_is_not_a_string_is_refused()
    {
        var builder = RecBuilder.Between([Ours("T1", "BUY")], [Theirs("C1", "BUY")])
            .MatchOn(trade => trade.Quantity, confirm => confirm.Qty);

        var failure = Assert.Throws<InvalidOperationException>(() => builder.Value("BUY", accepts: ["B"]));

        Assert.Contains("Quantity", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_spelling_cannot_mean_two_values()
    {
        var builder = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BUY")]).Value("BUY", accepts: ["B", "BUY"]);

        var failure = Assert.Throws<ArgumentException>(() => builder.Value("SELL", accepts: ["B"]));

        Assert.Contains("'B' already means 'BUY'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_needs_a_name_and_a_spelling_needs_a_text()
    {
        var builder = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BUY")]);

        Assert.Throws<ArgumentException>(() => builder.Value(" ", accepts: ["B"]));
        Assert.Throws<ArgumentException>(() => builder.Value("BUY", accepts: []));
        Assert.Throws<ArgumentException>(() => builder.Value("BUY", accepts: [" "]));
        Assert.Throws<ArgumentException>(() => builder.Value("BUY", ours: [], theirs: []));
        Assert.Throws<ArgumentNullException>(() => builder.Value("BUY", accepts: null!));
        Assert.Throws<ArgumentNullException>(() => builder.Value((Func<string, string?>)null!));
    }

    [Fact]
    public void Listing_values_and_using_a_function_for_one_member_is_refused()
    {
        var listed = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BUY")]).Value("BUY", accepts: ["B"]);
        var decided = Keyed([Ours("T1", "BUY")], [Theirs("C1", "BUY")]).Value(text => text);

        Assert.Throws<InvalidOperationException>(() => listed.Value(text => text));
        Assert.Throws<InvalidOperationException>(() => decided.Value("BUY", accepts: ["B"]));
        Assert.Throws<InvalidOperationException>(() => decided.Value(text => text));
    }

    [Fact]
    public void A_builder_is_not_changed_by_naming_values_on_a_copy()
    {
        var plain = Keyed([Ours("T1", "BUY")], [Theirs("C1", "B")]);

        var named = plain.Value("BUY", accepts: ["B", "BUY"]);

        Assert.Empty(plain.Run().Matched);
        Assert.Single(named.Run().Matched);
    }
}
