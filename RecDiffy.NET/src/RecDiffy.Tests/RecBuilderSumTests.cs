using System.Text.Json;
using RecDiffy.Results;

namespace RecDiffy.Tests;

public class RecBuilderSumTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    private static readonly Allocation[] ThreeAllocations =
    [
        new("US0378331005", Day, 300m),
        new("US0378331005", Day, 200m),
        new("US0378331005", Day, 500m),
    ];

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static RecBuilder<Allocation, Block> Summed(IEnumerable<Allocation> allocations, IEnumerable<Block> blocks) =>
        RecBuilder.Between(allocations, blocks)
            .MatchOn(allocation => allocation.Isin, block => block.Isin)
                .And(allocation => allocation.TradeDate, block => block.TradeDate)
            .Sum(allocation => allocation.Quantity, block => block.Quantity);

    [Fact]
    public void Three_left_rows_that_sum_to_one_right_row_are_one_match()
    {
        Block[] blocks = [new("US0378331005", Day, 1000m)];

        var result = Summed(ThreeAllocations, blocks).Run();

        var match = Assert.Single(result.Matched);
        Assert.Equal(3, match.Left.Count);
        Assert.All(ThreeAllocations, allocation => Assert.Contains(match.Left, row => ReferenceEquals(row, allocation)));
        Assert.Same(blocks[0], Assert.Single(match.Right));
        Assert.Equal(FieldOutcome.Exact, match.Outcome);
        Assert.Empty(result.Breaks);
        Assert.Empty(result.Ambiguous);
        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
    }

    [Fact]
    public void One_left_row_that_equals_the_sum_of_three_right_rows_is_one_match()
    {
        Block[] blocks = [new("US0378331005", Day, 1000m)];

        var result = RecBuilder.Between(blocks, ThreeAllocations)
            .MatchOn(block => block.Isin, allocation => allocation.Isin)
                .And(block => block.TradeDate, allocation => allocation.TradeDate)
            .Sum(block => block.Quantity, allocation => allocation.Quantity)
            .Run();

        var match = Assert.Single(result.Matched);
        Assert.Same(blocks[0], Assert.Single(match.Left));
        Assert.Equal(3, match.Right.Count);
        Assert.All(ThreeAllocations, allocation => Assert.Contains(match.Right, row => ReferenceEquals(row, allocation)));
        Assert.Equal(FieldOutcome.Exact, match.Outcome);
        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
    }

    [Fact]
    public void Rows_that_do_not_add_up_exactly_are_not_matched()
    {
        Block[] blocks = [new("US0378331005", Day, 1000.01m)];

        var result = Summed(ThreeAllocations, blocks).Run();

        Assert.Empty(result.Matched);
        Assert.Empty(result.Breaks);
        var contest = Assert.Single(result.Ambiguous);
        Assert.Equal(3, contest.Left.Count);
        Assert.Same(blocks[0], Assert.Single(contest.Right));
        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
    }

    [Fact]
    public void Rows_with_no_counterpart_in_their_block_stay_left_only_and_right_only()
    {
        Allocation[] allocations = [new("US0378331005", Day, 300m), new("US0378331005", Day, 200m)];
        Block[] blocks = [new("US5949181045", Day, 500m)];

        var result = Summed(allocations, blocks).Run();

        Assert.Empty(result.Matched);
        Assert.Empty(result.Ambiguous);
        Assert.Equal(2, result.LeftOnly.Count);
        Assert.Same(blocks[0], Assert.Single(result.RightOnly));
    }

    [Fact]
    public void Rows_outside_the_block_are_never_added_in()
    {
        Allocation[] allocations =
        [
            new("US0378331005", Day, 300m),
            new("US0378331005", Day, 200m),
            new("US5949181045", Day, 500m),
            new("US0378331005", Day.AddDays(1), 500m),
        ];
        Block[] blocks = [new("US0378331005", Day, 1000m)];

        var result = Summed(allocations, blocks).Run();

        Assert.Empty(result.Matched);
        Assert.Equal(2, Assert.Single(result.Ambiguous).Left.Count);
        Assert.Equal([allocations[2], allocations[3]], result.LeftOnly);
        Assert.Same(blocks[0], Assert.Single(Assert.Single(result.Ambiguous).Right));
        Assert.Empty(result.RightOnly);
    }

    [Fact]
    public void Each_block_sums_on_its_own_and_a_one_to_one_row_still_matches_by_key()
    {
        Allocation[] allocations =
        [
            new("US0378331005", Day, 300m),
            new("US0378331005", Day, 700m),
            new("US5949181045", Day, 250m),
            new("US5949181045", Day, 250m),
            new("US88160R1014", Day, 42m),
        ];
        Block[] blocks =
        [
            new("US0378331005", Day, 1000m),
            new("US5949181045", Day, 500m),
            new("US88160R1014", Day, 42m),
        ];

        var result = Summed(allocations, blocks).Run();

        Assert.Equal([2, 2, 1], result.Matched.Select(match => match.Left.Count));
        Assert.Equal([1, 1, 1], result.Matched.Select(match => match.Right.Count));
        Assert.All(result.Matched, match => Assert.Equal(FieldOutcome.Exact, match.Outcome));
        Assert.Empty(result.LeftOnly);
        Assert.Empty(result.RightOnly);
    }

    [Fact]
    public void The_profile_writes_one_group_sum_pass_for_each_side_after_the_natural_key_passes()
    {
        var profile = Summed(ThreeAllocations, [new("US0378331005", Day, 1000m)])
            .ThenMatchOn(allocation => allocation.Ticket, block => block.Ticket)
            .Profile;

        var passes = profile.Paths[0].Passes;
        Assert.Equal(
            ["P1:match.naturalKey:", "P2:match.naturalKey:", "P3:match.groupSum:left", "P4:match.groupSum:right"],
            passes.Select(pass => $"{pass.Id}:{pass.Pass}:{pass.Many}"),
            StringComparer.Ordinal);
        Assert.All(passes.Where(pass => pass.Pass is "match.groupSum"), pass =>
        {
            Assert.Equal(["Ticket"], pass.Block, StringComparer.Ordinal);
            Assert.Equal(["Quantity"], pass.Conserve, StringComparer.Ordinal);
            Assert.Empty(pass.Key);
        });
    }

    [Fact]
    public void The_json_shows_group_sum_passes_that_block_on_the_key_and_conserve_the_summed_field()
    {
        var json = Summed(ThreeAllocations, [new("US0378331005", Day, 1000m)]).Profile.ToJson();

        using var document = JsonDocument.Parse(json);
        var written = document.RootElement.GetProperty("paths")[0].GetProperty("passes").Deserialize<JsonPass[]>(Web)!;
        var groups = written.Where(pass => pass.Pass is "match.groupSum").ToArray();
        Assert.Equal(["left", "right"], groups.Select(pass => pass.Many), StringComparer.Ordinal);
        Assert.All(groups, pass =>
        {
            Assert.Equal(["Isin", "TradeDate"], pass.Block, StringComparer.Ordinal);
            Assert.Equal(["Quantity"], pass.Conserve, StringComparer.Ordinal);
        });
        Assert.Contains("\"match.groupSum\"", json, StringComparison.Ordinal);
        Assert.Contains("\"conserve\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summed_field_is_an_economic_decimal_and_a_key_stays_a_key()
    {
        var profile = Summed(ThreeAllocations, [new("US0378331005", Day, 1000m)]).Profile;

        Assert.Equal(
            ["Isin|Identifier|Key", "TradeDate|Date|Key", "Quantity|Decimal|Economic"],
            profile.Fields.Select(field => $"{field.Name}|{field.Type}|{field.Role}"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Calling_sum_again_conserves_another_field_in_the_same_passes()
    {
        Allocation[] allocations =
        [
            new("US0378331005", Day, 300m, 30m),
            new("US0378331005", Day, 700m, 70m),
        ];
        Block[] bigBlock = [new("US0378331005", Day, 1000m, 100m)];
        Block[] shortBlock = [new("US0378331005", Day, 1000m, 99m)];
        RecBuilder<Allocation, Block> Both(Block[] blocks) => Summed(allocations, blocks).Sum(allocation => allocation.Fee, block => block.Fee);

        var profile = Both(bigBlock).Profile;

        Assert.Equal(["P1", "P2", "P3"], profile.Paths[0].Passes.Select(pass => pass.Id), StringComparer.Ordinal);
        Assert.All(profile.Paths[0].Passes.Skip(1), pass => Assert.Equal(["Quantity", "Fee"], pass.Conserve, StringComparer.Ordinal));
        Assert.Single(Both(bigBlock).Run().Matched);
        Assert.Empty(Both(shortBlock).Run().Matched);
    }

    [Fact]
    public void Summing_the_same_pair_twice_conserves_it_once()
    {
        var profile = Summed(ThreeAllocations, [new("US0378331005", Day, 1000m)])
            .Sum(allocation => allocation.Quantity, block => block.Quantity)
            .Profile;

        Assert.All(profile.Paths[0].Passes.Skip(1), pass => Assert.Equal(["Quantity"], pass.Conserve, StringComparer.Ordinal));
    }

    [Fact]
    public void Sum_is_kept_by_every_other_builder_method_and_does_not_change_the_builder_it_was_called_on()
    {
        var plain = RecBuilder.Between(ThreeAllocations, [new Block("US0378331005", Day, 1000m)])
            .MatchOn(allocation => allocation.Isin, block => block.Isin)
            .And(allocation => allocation.TradeDate, block => block.TradeDate);
        var before = plain.Profile.ToJson();

        var summed = plain
            .Sum(allocation => allocation.Quantity, block => block.Quantity)
            .Named("allocations", "3.0.0")
            .Compare(allocation => allocation.Fee, block => block.Fee, tolerance: 0.5m);

        Assert.Equal(before, plain.Profile.ToJson());
        Assert.Equal("allocations", summed.Profile.ProfileId);
        Assert.Equal(["P1", "P2", "P3"], summed.Profile.Paths[0].Passes.Select(pass => pass.Id), StringComparer.Ordinal);
        Assert.Single(summed.Run().Matched);
    }

    [Fact]
    public void A_builder_that_never_calls_sum_writes_natural_key_passes_only()
    {
        var profile = RecBuilder.Between(ThreeAllocations, [new Block("US0378331005", Day, 1000m)])
            .MatchOn(allocation => allocation.Isin, block => block.Isin)
            .ThenMatchOn(allocation => allocation.TradeDate, block => block.TradeDate)
            .Compare(allocation => allocation.Quantity, block => block.Quantity)
            .Profile;

        Assert.Equal(["P1:match.naturalKey", "P2:match.naturalKey"], profile.Paths[0].Passes.Select(pass => $"{pass.Id}:{pass.Pass}"), StringComparer.Ordinal);
        Assert.DoesNotContain("groupSum", profile.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Sum_before_match_on_throws()
    {
        var start = RecBuilder.Between(ThreeAllocations, Array.Empty<Block>());

        var error = Assert.Throws<InvalidOperationException>(() => start.Sum(allocation => allocation.Quantity, block => block.Quantity));

        Assert.Contains("MatchOn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sum_on_a_member_that_is_not_a_decimal_throws()
    {
        var start = RecBuilder.Between(ThreeAllocations, Array.Empty<Block>())
            .MatchOn(allocation => allocation.Isin, block => block.Isin);

        var text = Assert.Throws<ArgumentException>(() => start.Sum(allocation => allocation.Isin, block => block.Isin));
        var date = Assert.Throws<ArgumentException>(() => start.Sum(allocation => allocation.TradeDate, block => block.TradeDate));
        Assert.Contains("Isin", text.Message, StringComparison.Ordinal);
        Assert.Contains("TradeDate", date.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => start.Sum(allocation => allocation.Quantity, block => block.Isin));
        Assert.Throws<ArgumentException>(() => start.Sum(allocation => allocation.Ratio, block => block.Ratio));
        Assert.Throws<ArgumentException>(() => start.Sum(allocation => allocation.Quantity + 1, block => block.Quantity));
    }

    private sealed record JsonPass(string Pass, string? Many, string[]? Block, string[]? Conserve);

    private sealed record Allocation(string Isin, DateOnly TradeDate, decimal Quantity, decimal Fee = 0m)
    {
        public string? Ticket => Isin;

        public double Ratio => 0;
    }

    private sealed record Block(string Isin, DateOnly TradeDate, decimal Quantity, decimal Fee = 0m)
    {
        public string? Ticket => Isin;

        public double Ratio => 0;
    }
}
