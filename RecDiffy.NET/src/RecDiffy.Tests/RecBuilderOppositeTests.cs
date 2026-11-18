using RecDiffy.Results;

namespace RecDiffy.Tests;

// #512: two values of a key member can be opposites, so a buy on one side matches a sell on the other.
public class RecBuilderOppositeTests
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

    private static RecBuilder<OurTrade, BrokerConfirm> Named(RecBuilder<OurTrade, BrokerConfirm> builder) =>
        builder
            .Value("BUY", accepts: ["B", "BOT", "BUY"])
            .Value("SELL", accepts: ["S", "SLD", "SELL"]);

    [Fact]
    public void A_buy_on_one_side_matches_a_sell_on_the_other_and_two_that_agree_do_not()
    {
        OurTrade[] ours = [Ours("T1", "B", 1500m), Ours("T2", "SLD", 400m), Ours("T3", "BOT", 10m)];
        BrokerConfirm[] theirs = [Theirs("C9", "SLD", 1500m), Theirs("C7", "SELL", 400m), Theirs("C5", "BUY", 10m)];

        var result = Named(Keyed(ours, theirs)).Opposite("BUY", "SELL").Run();

        var match = Assert.Single(result.Matched);
        Assert.Same(ours[0], Assert.Single(match.Left));
        Assert.Same(theirs[0], Assert.Single(match.Right));
        Assert.Equal(2, result.LeftOnly.Count);
        Assert.Equal(2, result.RightOnly.Count);
    }

    [Fact]
    public void The_profile_reads_the_right_side_as_the_opposite_and_the_left_side_as_it_is()
    {
        var result = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Opposite("BUY", "SELL").Run();

        Assert.Equal(
            TypedProfileRules.Table(("B", "BUY"), ("BOT", "BUY"), ("BUY", "BUY"), ("S", "SELL"), ("SLD", "SELL"), ("SELL", "SELL")),
            TypedProfileRules.SideTable(result.Profile, "left"));
        Assert.Equal(
            TypedProfileRules.Table(("B", "SELL"), ("BOT", "SELL"), ("BUY", "SELL"), ("S", "BUY"), ("SLD", "BUY"), ("SELL", "BUY")),
            TypedProfileRules.SideTable(result.Profile, "right"));
    }

    [Fact]
    public void Pairs_of_spellings_match_without_naming_any_values()
    {
        OurTrade[] ours = [Ours("T1", "BUY", 1500m), Ours("T2", "SELL", 400m)];
        BrokerConfirm[] theirs = [Theirs("C1", "S", 1500m), Theirs("C2", "B", 400m)];

        var result = Keyed(ours, theirs).Opposite(("BUY", "S"), ("SELL", "B")).Run();

        Assert.Equal(2, result.Matched.Count);
        Assert.Equal(TypedProfileRules.Table(("BUY", "BUY"), ("SELL", "SELL")), TypedProfileRules.SideTable(result.Profile, "left"));
        Assert.Equal(TypedProfileRules.Table(("S", "BUY"), ("B", "SELL")), TypedProfileRules.SideTable(result.Profile, "right"));
    }

    [Fact]
    public void Two_sides_that_use_the_same_words_from_their_own_view_match_when_they_differ()
    {
        var result = Keyed([Ours("T1", "BUY", 1500m), Ours("T2", "SELL", 400m)], [Theirs("C1", "SELL", 1500m), Theirs("C2", "SELL", 400m)])
            .Opposite("BUY", "SELL")
            .Run();

        var match = Assert.Single(result.Matched);
        Assert.Equal("T1", match.Left[0].TradeId);
        Assert.Equal("C1", match.Right[0].ConfirmRef);
    }

    [Fact]
    public void A_function_decides_which_spellings_are_opposites()
    {
        var result = Keyed([Ours("T1", "BUY", 1500m), Ours("T2", "SELL", 400m)], [Theirs("C1", "S", 1500m), Theirs("C2", "B", 400m)])
            .Opposite((ours, theirs) => (ours, theirs) is ("BUY", "S") or ("SELL", "B"))
            .Run();

        Assert.Equal(2, result.Matched.Count);
        Assert.Equal(TypedProfileRules.Table(("S", "BUY"), ("B", "SELL")), TypedProfileRules.SideTable(result.Profile, "right"));
    }

    [Fact]
    public void A_function_over_named_values_is_asked_about_values_and_not_spellings()
    {
        var asked = new List<(string, string)>();

        var result = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")]))
            .Opposite((ours, theirs) =>
            {
                asked.Add((ours, theirs));
                return ours != theirs;
            })
            .Run();

        Assert.Single(result.Matched);
        Assert.Equal(
            [("BUY", "BUY"), ("BUY", "SELL"), ("SELL", "BUY"), ("SELL", "SELL")],
            asked.OrderBy(pair => pair.Item1, StringComparer.Ordinal).ThenBy(pair => pair.Item2, StringComparer.Ordinal));
    }

    [Fact]
    public void A_spelling_nobody_listed_is_quarantined_never_guessed()
    {
        var result = Named(Keyed([Ours("T1", "BUY")], [Theirs("C5", "SOLD")])).Opposite("BUY", "SELL").Run();

        Assert.Empty(result.Matched);
        var quarantined = Assert.Single(result.Result.Rows, row => row.Disposition is Disposition.Quarantined);
        Assert.Equal("right", quarantined.Row.Source);
        Assert.Contains(quarantined.Diagnostics, diagnostic => diagnostic.Code == "UnknownSymbol");
    }

    [Fact]
    public void A_right_value_with_no_opposite_is_not_accepted()
    {
        var result = Keyed([Ours("T1", "BUY")], [Theirs("C1", "SELL"), Theirs("C2", "HOLD", 50m)])
            .Value("BUY", accepts: ["BUY"])
            .Value("SELL", accepts: ["SELL"])
            .Value("HOLD", accepts: ["HOLD"])
            .Opposite("BUY", "SELL")
            .Run();

        Assert.Single(result.Matched);
        Assert.Single(result.Result.Rows, row => row.Disposition is Disposition.Quarantined && row.Row.Source == "right");
    }

    [Fact]
    public void The_order_of_Value_and_Opposite_does_not_matter()
    {
        var after = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Opposite("BUY", "SELL").Run();
        var before = Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])
            .Opposite("BUY", "SELL")
            .Value("BUY", accepts: ["B", "BOT", "BUY"])
            .Value("SELL", accepts: ["S", "SLD", "SELL"])
            .Run();

        Assert.Single(before.Matched);
        Assert.Equal(after.Result.Run.ProfileHash, before.Result.Run.ProfileHash);
    }

    [Fact]
    public void The_relationship_is_symmetric_so_naming_it_the_other_way_round_gives_the_same_profile()
    {
        var one = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Opposite("BUY", "SELL").Run();
        var other = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Opposite("SELL", "BUY").Run();

        Assert.Equal(one.Result.Run.ProfileHash, other.Result.Run.ProfileHash);
    }

    [Fact]
    public void Declaring_opposites_changes_the_profile_hash()
    {
        var plain = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Run();
        var opposed = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Opposite("BUY", "SELL").Run();

        Assert.NotEqual(plain.Result.Run.ProfileHash, opposed.Result.Run.ProfileHash, StringComparer.Ordinal);
    }

    [Fact]
    public void A_pair_that_names_a_value_nobody_listed_is_refused_and_says_which_value_and_side()
    {
        var builder = Named(Keyed([Ours("T1", "B")], [Theirs("C1", "SLD")])).Opposite("BUY", "SOLD");

        var failure = Assert.Throws<InvalidOperationException>(() => builder.Profile);

        Assert.Contains("'SOLD' is not a value named for the left items", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_with_two_opposites_is_refused()
    {
        var builder = Keyed([Ours("T1", "BUY")], [Theirs("C1", "S")]).Opposite(("BUY", "S"), ("SELL", "S"));

        var failure = Assert.Throws<InvalidOperationException>(() => builder.Profile);

        Assert.Contains("'S' is the opposite of 'BUY' and 'SELL'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Opposite_needs_a_key_a_string_member_and_good_arguments()
    {
        var empty = RecBuilder.Between([Ours("T1", "BUY")], [Theirs("C1", "S")]);
        var afterCompare = Keyed([Ours("T1", "BUY")], [Theirs("C1", "S")]).Compare(trade => trade.Price, confirm => confirm.Price);
        var number = empty.MatchOn(trade => trade.Quantity, confirm => confirm.Qty);
        var keyed = Keyed([Ours("T1", "BUY")], [Theirs("C1", "S")]);

        Assert.Throws<InvalidOperationException>(() => empty.Opposite("BUY", "SELL"));
        Assert.Throws<InvalidOperationException>(() => afterCompare.Opposite("BUY", "SELL"));
        Assert.Throws<InvalidOperationException>(() => number.Opposite("BUY", "SELL"));
        Assert.Throws<ArgumentException>(() => keyed.Opposite());
        Assert.Throws<ArgumentException>(() => keyed.Opposite(" ", "SELL"));
        Assert.Throws<ArgumentException>(() => keyed.Opposite(("BUY", "")));
        Assert.Throws<ArgumentNullException>(() => keyed.Opposite((Func<string, string, bool>)null!));
        Assert.Throws<ArgumentNullException>(() => keyed.Opposite("BUY", null!));
    }

    [Fact]
    public void Two_functions_for_the_opposites_are_refused()
    {
        var decided = Keyed([Ours("T1", "BUY")], [Theirs("C1", "S")]).Opposite((ours, theirs) => ours != theirs);

        Assert.Throws<InvalidOperationException>(() => decided.Opposite((ours, theirs) => ours == theirs));
    }

    [Fact]
    public void Pairs_and_a_function_can_be_used_together()
    {
        var result = Keyed([Ours("T1", "BUY", 1500m), Ours("T2", "SELL", 400m)], [Theirs("C1", "S", 1500m), Theirs("C2", "B", 400m)])
            .Opposite(("BUY", "S"))
            .Opposite((ours, theirs) => (ours, theirs) is ("SELL", "B"))
            .Run();

        Assert.Equal(2, result.Matched.Count);
    }

    [Fact]
    public void Compared_members_still_compare_on_a_matched_pair()
    {
        var result = Named(Keyed([Ours("T1", "B") with { Price = 10m }], [Theirs("C1", "SLD") with { Price = 11m }]))
            .Opposite("BUY", "SELL")
            .Compare(trade => trade.Price, confirm => confirm.Price)
            .Run();

        var match = Assert.Single(result.Matched);
        Assert.Equal(FieldOutcome.Break, match.Outcome);
        Assert.Single(result.Breaks);
    }
}
